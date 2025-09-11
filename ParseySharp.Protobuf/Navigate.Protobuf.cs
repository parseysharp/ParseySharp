using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using System.Collections;
using System.Linq;

namespace ParseySharp;

public static class ParsePathNavProtobuf
{
  private static Either<object, Unknown<object>> UnboxNumber(double d)
  {
    // Is it an integer?
    if (Math.Abs(d - Math.Truncate(d)) < 1e-9)
    {
      var asLong = (long)Math.Truncate(d);
      if (asLong <= int.MaxValue && asLong >= int.MinValue)
        return Right<object, Unknown<object>>(Unknown.New<object>((int)asLong));
      return Right<object, Unknown<object>>(Unknown.New<object>(asLong));
    }
    // Non-integer: keep as double
    return Right<object, Unknown<object>>(Unknown.New<object>(d));
  }
  // Protobuf-aware navigator over 'object' to support scalars, messages, repeated and map fields.
  public static readonly ParsePathNav<object> Protobuf =
    new(
      Prop: (node, name) =>
        node switch
        {
          // WellKnownTypes.Struct behaves like an object: look up by key
          Struct s =>
            s.Fields.TryGetValue(name, out var sv)
              ? Right<object, Option<object>>(Optional((object)sv))
              : Right<object, Option<object>>(None),

          // If we're sitting on a Value that wraps a Struct, drill into its fields
          Value v when v.KindCase == Value.KindOneofCase.StructValue =>
            v.StructValue.Fields.TryGetValue(name, out var vv)
              ? Right<object, Option<object>>(Optional((object)vv))
              : Right<object, Option<object>>(None),

          IMessage msg =>
            FindField(msg, name).Match(
              None: () => Right<object, Option<object>>(None),
              Some: fd =>
              {
                var accessor = fd.Accessor;
                if (fd.HasPresence && !accessor.HasValue(msg))
                  return Right<object, Option<object>>(None);
                var value = accessor.GetValue(msg);
                return Right<object, Option<object>>(Optional(value));
              }
            ),
          _ => Left<object, Option<object>>(node)
        },

      Index: (node, i) =>
        node switch
        {
          // WellKnownTypes.ListValue behaves like an array
          ListValue lv when i >= 0 =>
            (i < lv.Values.Count)
              ? Right<object, Option<object>>(Optional((object)lv.Values[i]))
              : Right<object, Option<object>>(None),

          // Repeated fields implement IEnumerable but not non-generic IList, so enumerate safely
          IEnumerable en when node is not string && i >= 0 =>
            ((Func<Seq<object>>)(() => Seq(en.Cast<object>()))).Try<object, Seq<object>>(_ => node).Match(
              Left: _ => Left<object, Option<object>>(node),
              Right: xs => (i < xs.Count)
                ? Right<object, Option<object>>(Optional(xs[i]))
                : Right<object, Option<object>>(None)
            ),
          _ => Left<object, Option<object>>(node)
        },

      Unbox: node =>
        node switch
        {
          null => Right<object, Unknown<object>>(new Unknown<object>.None()),

          // Unwrap Value into CLR primitives / nested messages / sequences
          Value v => v.KindCase switch
          {
            Value.KindOneofCase.NullValue   => Right<object, Unknown<object>>(new Unknown<object>.None()),
            Value.KindOneofCase.StringValue => Right<object, Unknown<object>>(Unknown.New<object>(v.StringValue)),
            Value.KindOneofCase.BoolValue   => Right<object, Unknown<object>>(Unknown.New<object>(v.BoolValue)),
            Value.KindOneofCase.NumberValue => UnboxNumber(v.NumberValue),
            Value.KindOneofCase.StructValue => Right<object, Unknown<object>>(Unknown.New<object>(v.StructValue)),
            Value.KindOneofCase.ListValue   => Right<object, Unknown<object>>(Unknown.New<object>(v.ListValue.Values)),
            _                                => Right<object, Unknown<object>>(new Unknown<object>.None())
          },

          // ListValue should expose its Values for Seq parsing
          ListValue lv => Right<object, Unknown<object>>(Unknown.New<object>(lv.Values)),

          // Repeated (IEnumerable but not string) -> expose enumerable for Seq parsing
          IEnumerable en when node is not string => Right<object, Unknown<object>>(Unknown.New<object>(en)),

          // Primitives and enums
          int or long or double or float or bool or string => Right<object, Unknown<object>>(Unknown.New<object>(node)),
          uint or ulong => Right<object, Unknown<object>>(Unknown.New<object>(Convert.ToInt64(node))),
          System.Enum => Right<object, Unknown<object>>(Unknown.New<object>(Convert.ToInt32(node))),

          // Bytes
          byte[] or ByteString => Right<object, Unknown<object>>(Unknown.New<object>(node)),

          _ => Left<object, Unknown<object>>(node)
        }
    );

  static Option<FieldDescriptor> FindField(IMessage msg, string name)
  {
    var fd = msg.Descriptor.FindFieldByName(name);
    if (fd is not null) return Optional(fd);
    // Try JSON name
    fd = msg.Descriptor.Fields.InDeclarationOrder().FirstOrDefault(f => f.JsonName == name);
    return Optional(fd);
  }
}

public static class ParseProtobufExtensions
{
  public static Func<object, Validation<Seq<ParsePathErr>, A>> ParseProtobuf<A>(this Parse<A> parser) =>
    ParseExtensions.RunWithNav(parser, ParsePathNavProtobuf.Protobuf);
}
