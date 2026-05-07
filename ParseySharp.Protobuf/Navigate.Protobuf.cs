using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using System.Collections;

namespace ParseySharp;

public static class ParsePathNavProtobuf
{
  static bool IsProtoNull(object? o) =>
    o is null || (o is Value v && v.KindCase == Value.KindOneofCase.NullValue);

  private static UnboxStep UnboxNumber(double d)
  {
    if (Math.Abs(d - Math.Truncate(d)) < 1e-9)
    {
      var asLong = (long)Math.Truncate(d);
      if (asLong <= int.MaxValue && asLong >= int.MinValue)
        return UnboxStep.Value((int)asLong);
      return UnboxStep.Value(asLong);
    }
    return UnboxStep.Value(d);
  }
  // Protobuf-aware navigator over 'object' to support scalars, messages, repeated and map fields.
  public static readonly ParsePathNav<object> Protobuf =
    ParsePathNav<object>.Create(
      Prop: (node, name) =>
        node switch
        {
          // WellKnownTypes.Struct behaves like an object: look up by key
          Struct s =>
            s.Fields.TryGetValue(name, out var sv)
              ? Optional<object>(sv).Filter(c => !IsProtoNull(c)).Match(
                  Some: c => NavStep.Value<object>(c),
                  None: () => NavStep.Null<object>())
              : NavStep.Absent<object>(),

          // If we're sitting on a Value that wraps a Struct, drill into its fields
          Value v when v.KindCase == Value.KindOneofCase.StructValue =>
            v.StructValue.Fields.TryGetValue(name, out var vv)
              ? Optional<object>(vv).Filter(c => !IsProtoNull(c)).Match(
                  Some: c => NavStep.Value<object>(c),
                  None: () => NavStep.Null<object>())
              : NavStep.Absent<object>(),

          IMessage msg =>
            FindField(msg, name).Match(
              None: () => NavStep.Absent<object>(),
              Some: fd =>
              {
                var accessor = fd.Accessor;
                if (fd.HasPresence && !accessor.HasValue(msg))
                  return NavStep.Absent<object>();
                var value = accessor.GetValue(msg);
                return Optional(value).Filter(c => !IsProtoNull(c)).Match(
                  Some: c => NavStep.Value<object>(c),
                  None: () => NavStep.Null<object>());
              }
            ),
          _ => NavStep.NotApplicable(node)
        },

      Index: (node, i) =>
        node switch
        {
          // WellKnownTypes.ListValue behaves like an array
          ListValue lv when i >= 0 =>
            (i < lv.Values.Count)
              ? Optional<object>(lv.Values[i]).Filter(c => !IsProtoNull(c)).Match(
                  Some: c => NavStep.Value<object>(c),
                  None: () => NavStep.Null<object>())
              : NavStep.Absent<object>(),

          // Repeated fields implement IEnumerable but not non-generic IList, so enumerate safely
          IEnumerable en when node is not string && i >= 0 =>
            ((Func<Seq<object>>)(() => Seq(en.Cast<object>()))).Try<object, Seq<object>>(_ => node).Match(
              Left: _ => NavStep.NotApplicable(node),
              Right: xs => (i < xs.Count)
                ? Optional(xs[i]).Filter(c => !IsProtoNull(c)).Match(
                    Some: c => NavStep.Value<object>(c),
                    None: () => NavStep.Null<object>())
                : NavStep.Absent<object>()
            ),
          _ => NavStep.NotApplicable(node)
        },

      Unbox: node =>
        node switch
        {
          null => UnboxStep.Null(),

          // Unwrap Value into CLR primitives / nested messages / sequences
          Value v => v.KindCase switch
          {
            Value.KindOneofCase.NullValue   => UnboxStep.Null(),
            Value.KindOneofCase.StringValue => UnboxStep.Value(v.StringValue),
            Value.KindOneofCase.BoolValue   => UnboxStep.Value(v.BoolValue),
            Value.KindOneofCase.NumberValue => UnboxNumber(v.NumberValue),
            Value.KindOneofCase.StructValue => UnboxStep.Value(v.StructValue),
            Value.KindOneofCase.ListValue   => UnboxStep.Value(v.ListValue.Values),
            _                                => UnboxStep.Null()
          },

          // ListValue should expose its Values for Seq parsing
          ListValue lv => UnboxStep.Value(lv.Values),

          // Repeated (IEnumerable but not string) -> expose enumerable for Seq parsing
          IEnumerable en when node is not string => UnboxStep.Value(en),

          // Primitives and enums
          int or long or double or float or bool or string => UnboxStep.Value(node),
          uint or ulong => UnboxStep.Value(Convert.ToInt64(node)),
          System.Enum => UnboxStep.Value(Convert.ToInt32(node)),

          // Bytes
          byte[] or ByteString => UnboxStep.Value(node),

          _ => UnboxStep.NotApplicable(node)
        },
      CloneNode: x => x
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
