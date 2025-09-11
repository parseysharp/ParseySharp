namespace ParseySharp;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Xml.Linq;
using System.Text.Json.Nodes;
using System.Data;

public abstract record PathSeg
{
  public sealed record Key(string Name) : PathSeg;
  public sealed record Index(int I) : PathSeg;

  public static PathSeg KeyOf(string name) => new Key(name);
  public static PathSeg IndexOf(int i) => new Index(i);

  public static implicit operator PathSeg(string name) => new Key(name);
  public static implicit operator PathSeg(int i) => new Index(i);
}

public static class PathSegRender
{
  public static string ToStr(PathSeg seg) =>
    seg switch { PathSeg.Key k => k.Name, PathSeg.Index i => $"[{i.I}]", _ => "?" };

  public static string Prefix(PathSeg seg) =>
    seg switch { PathSeg.Key => ".", PathSeg.Index => "", _ => "" };

  public static Seq<string> ToStrings(Seq<PathSeg> segs) =>
    segs.Map(ToStr);

  public static string Print(Seq<PathSeg> segs) =>
    segs.Tail.Fold(segs.Head.Map(ToStr).IfNone(""), (acc, seg) => acc + Prefix(seg) + ToStr(seg));

}

public record class ParseErr(string Message, string Expected, object Actual);

public record class ParsePathErr(string Message, string Expected, object Actual, Seq<string> Path)
{
  public static ParsePathErr FromParseErr(ParseErr err, ListZipper<PathSeg> path) =>
    new(err.Message, err.Expected, err.Actual, PathSegRender.ToStrings(path.ToSeq()));

  public static ParsePathErr FromParseErr(ParseErr err, Seq<string> path) =>
    new(err.Message, err.Expected, err.Actual, path);

  public ParsePathErr WithPrefix(Seq<string> path) =>
    new(Message, Expected, Actual, path + Path);
}

public static class PathParser
{

  public static Validation<Seq<ParsePathErr>, Option<B>> NextStep<B>(
    ListZipper<PathSeg> path,
    Func<B, Either<object, Option<B>>> getNext,
    Func<Option<B>, Validation<Seq<ParsePathErr>, B>> runNext,
    Option<B> input
  ) => input.Match(
    None: () => runNext(None).Map(Some),
    Some: i => getNext(i).Match<Validation<Seq<ParsePathErr>, Option<B>>>(
      // TODO - handle wrong type better
      Left: (object x) => runNext(None).Map(Some),
      Right: (Option<B> x) => x.Match(
        None: () => path.Nexts.IsEmpty
          ? Success<Seq<ParsePathErr>, Option<B>>(None)
          : runNext(None).Map(Some),
        Some: x => runNext(x).Map(Some)
      )
    )
  );

  public static Validation<Seq<ParsePathErr>, Unknown<B>> NextStepU<B>(
    ListZipper<PathSeg> path,
    Func<B, Either<object, Option<B>>> getNext,
    Func<Option<B>, Validation<Seq<ParsePathErr>, B>> runNext,
    Unknown<B> input
  ) => NextStep(path, getNext, runNext, input.ToOption()).Map(Unknown.UnsafeFromOption);

  public static Validation<Seq<ParsePathErr>, Unknown<B>> Navigate<B>(
    ParsePathNav<B> nav,
    ListZipper<PathSeg> path,
    string Name,
    Unknown<B> input
  ) =>
    path.Fold(
        Success<Seq<ParsePathErr>, Unknown<B>>(input),
        (acc, z) =>
          from cur in acc
          from next in z.Focus switch
          {
            PathSeg.Key k =>
              NextStepU<B>(
                z,
                cur => nav.Prop(cur, k.Name),
                x => OptionExtensions.ToValidation<Seq<ParsePathErr>, B>(
                  x,
                  [new ParsePathErr(
                    $"Missing property {k.Name}",
                    Name,
                    Optional(cur),
                  PathSegRender.ToStrings(toSeq(z.Prevs.Reverse()))
                )]),
                cur
              ),
            PathSeg.Index ix =>
              NextStepU<B>(
                z,
                cur => nav.Index(cur, ix.I),
                x => OptionExtensions.ToValidation<Seq<ParsePathErr>, B>(
                  x,
                  [new ParsePathErr(
                    $"Missing index {ix.I}",
                  Name,
                  Optional(cur),
                  PathSegRender.ToStrings(toSeq(z.Prevs.Reverse()))
                )]),
                cur
              ),
            _ =>
              Fail<Seq<ParsePathErr>, Unknown<B>>([new ParsePathErr(
                $"Unknown path segment type {z.Focus}",
                Name,
                Optional(cur),
                PathSegRender.ToStrings(toSeq(z.Prevs.Reverse()))
              )])
          }
          select next);

}

public readonly record struct ParsePathNav<S>(
  Func<S, string, Either<object, Option<S>>> Prop,
  Func<S, int, Either<object, Option<S>>> Index,
  Func<S, Either<object, Unknown<object>>> Unbox
);

public static class ParsePathNav
{
  public static readonly ParsePathNav<JsonElement> Json =
  new(
      Prop: (je, name) =>
        je.ValueKind == JsonValueKind.Object
          ? Right<object, Option<JsonElement>>(Optional(je.TryGetProperty(name, out var v) ? v : default))
          : Left<object, Option<JsonElement>>(je),

      Index: (je, i) =>
        je.ValueKind == JsonValueKind.Array && i >= 0
          ? Optional(toSeq(je.EnumerateArray())).Filter(x => x.Count > i).Match(
              None: () => Right<object, Option<JsonElement>>(None),
            Some: v => Right<object, Option<JsonElement>>(Optional(v[i]))
          )
          : Left<object, Option<JsonElement>>(je),

      Unbox: je => je.ValueKind switch
      {
        JsonValueKind.String => Right<object, Unknown<object>>(Unknown.UnsafeFromOption(Optional<object>(je.GetString()))),
        JsonValueKind.Number => je.TryGetInt32(out var i) ? Right<object, Unknown<object>>(Unknown.New<object>(i))
                              : je.TryGetInt64(out var l) ? Right<object, Unknown<object>>(Unknown.New<object>(l))
                              : je.TryGetDouble(out var d) ? Right<object, Unknown<object>>(Unknown.New<object>(d))
                              : Left<object, Unknown<object>>(je),
        JsonValueKind.True => Right<object, Unknown<object>>(Unknown.New<object>(true)),
        JsonValueKind.False => Right<object, Unknown<object>>(Unknown.New<object>(false)),
        JsonValueKind.Null => Right<object, Unknown<object>>(Unknown.UnsafeFromOption<object>(None)),
        JsonValueKind.Array => Right<object, Unknown<object>>(Unknown.New<object>(je.EnumerateArray())),
        _ => Left<object, Unknown<object>>(je)
      }
    );

  public static readonly ParsePathNav<JsonNode> Nodes =
    new(
      Prop: (jn, name) =>
        jn is JsonObject obj
          ? (obj.TryGetPropertyValue(name, out var child)
              ? Right<object, Option<JsonNode>>(Optional(child))
              : Right<object, Option<JsonNode>>(None))
          : Left<object, Option<JsonNode>>(jn),

      Index: (jn, i) =>
        jn is JsonArray arr && i >= 0
          ? (i < arr.Count
              ? Right<object, Option<JsonNode>>(Optional(arr[i]))
              : Right<object, Option<JsonNode>>(None))
          : Left<object, Option<JsonNode>>(jn),

      Unbox: jn =>
        jn switch
        {
          null => Left<object, Unknown<object>>(jn!),
          JsonArray arr => Right<object, Unknown<object>>(Unknown.New<object>(arr)),
          JsonObject => Right<object, Unknown<object>>(Unknown.New<object>(jn)),
          JsonValue v =>
            v.TryGetValue<int>(out var iv)    ? Right<object, Unknown<object>>(Unknown.New<object>(iv)) :
            v.TryGetValue<long>(out var lv)   ? Right<object, Unknown<object>>(Unknown.New<object>(lv)) :
            v.TryGetValue<double>(out var dv) ? Right<object, Unknown<object>>(Unknown.New<object>(dv)) :
            v.TryGetValue<bool>(out var bv)   ? Right<object, Unknown<object>>(Unknown.New<object>(bv)) :
            v.TryGetValue<string>(out var sv) ? (string.IsNullOrWhiteSpace(sv)
                                                  ? Right<object, Unknown<object>>(Unknown.UnsafeFromOption<object>(None))
                                                  : Right<object, Unknown<object>>(Unknown.New<object>(sv)))
                                              : Right<object, Unknown<object>>(Unknown.UnsafeFromOption<object>(None)),
          _ => Left<object, Unknown<object>>(jn)
        }
    );

  public static readonly ParsePathNav<XElement> Xml =
    new(
      Prop: (xe, name) =>
        xe is null
          ? Left<object, Option<XElement>>(None)
          : name.StartsWith('@')
            ? // Attribute access via @attr convention
              Optional(xe.Attribute(name[1..])).Match(
                Some: a => Right<object, Option<XElement>>(Optional(new XElement("@attr", a.Value))),
                None: () => Right<object, Option<XElement>>(None)
              )
            : toSeq(xe.Elements()).Find(e => e.Name.LocalName == name)
                .Match(
                  Some: e => Right<object, Option<XElement>>(Optional(e)),
                  None: () => Right<object, Option<XElement>>(None)
                ),

      Index: (xe, i) =>
        xe is null || i < 0
          ? Left<object, Option<XElement>>(xe!)
          : Optional(Seq(xe.Elements())).Filter(es => es.Count > i).Match(
              None: () => Right<object, Option<XElement>>(None),
              Some: es => Right<object, Option<XElement>>(Optional(es[i]))
            ),

      Unbox: xe =>
        xe is null
          ? Right<object, Unknown<object>>(new Unknown<object>.None())
          : xe.HasElements
            ? Right<object, Unknown<object>>(Unknown.New<object>(xe.Elements()))
            : string.IsNullOrWhiteSpace(xe.Value)
              ? Right<object, Unknown<object>>(Unknown.UnsafeFromOption<object>(None))
              : xe.Value switch
                {
                  var s when int.TryParse(s, out var iv)    => Right<object, Unknown<object>>(Unknown.New<object>(iv)),
                  var s when long.TryParse(s, out var lv)   => Right<object, Unknown<object>>(Unknown.New<object>(lv)),
                  var s when double.TryParse(s, out var dv) => Right<object, Unknown<object>>(Unknown.New<object>(dv)),
                  var s when bool.TryParse(s, out var bv)   => Right<object, Unknown<object>>(Unknown.New<object>(bv)),
                  var s                                     => Right<object, Unknown<object>>(Unknown.New<object>(s))
                }
    );

  public static readonly ParsePathNav<object> Object =
    new(
      Prop: (node, name) =>
        node switch
        {
          // For dictionary-like carriers, absence of a key should yield Optional(None), not Left.
          IReadOnlyDictionary<string, object?> rd => Right<object, Option<object>>(Optional(rd.TryGetValue(name, out var v1) ? v1 : null)),
          IDictionary<string, object?> d => Right<object, Option<object>>(Optional(d.TryGetValue(name, out var v2) ? v2 : null)),
          System.Collections.IDictionary legacy => Right<object, Option<object>>(Optional(legacy.Contains(name) ? legacy[name] : null)),
          _ => Left<object, Option<object>>(node) 
        },

      Index: (node, i) =>
        node switch
        {
          IList<object?> list when i >= 0 && i < list.Count => Right<object, Option<object>>(Optional(list[i])),
          IEnumerable<object?> seq when node is not string && i >= 0 =>
              ((Func<Seq<object?>>)(() => Seq(seq))).Try<object, Seq<object?>>(_ => node).Match(
                Left: _ => Left<object, Option<object>>(node),
                Right: xs => Optional(xs).Filter(x => x.Count > i).Match(
                  None: () => Right<object, Option<object>>(None),
                  Some: x => Right<object, Option<object>>(Optional(x[i]))
                )
              ),
          _ => Left<object, Option<object>>(node)
        },

      Unbox: x => Right<object, Unknown<object>>(Unknown.New(x))
    );

  static Either<object, Option<object>> ReflectGet(object node, string name)
  {
    var t = node.GetType();

    var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
    if (p is not null && p.GetIndexParameters().Length == 0)
      return ((Func<object?>)(() => p.GetValue(node))).Try<object, object?>(_ => node).Match(
        Left: _ => Left<object, Option<object>>(node),
        Right: v => Right<object, Option<object>>(UnwrapOptionToPath(v))
      );

    var pj = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
              .FirstOrDefault(pp =>
                  pp.GetIndexParameters().Length == 0 &&
                  pp.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == name);
    if (pj is not null)
      return ((Func<object?>)(() => pj.GetValue(node))).Try<object, object?>(_ => node).Match(
        Left: _ => Left<object, Option<object>>(node),
        Right: v => Right<object, Option<object>>(UnwrapOptionToPath(v))
      );

    var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
    if (f is not null)
      return ((Func<object?>)(() => f.GetValue(node))).Try<object, object?>(_ => node).Match(
        Left: _ => Left<object, Option<object>>(node),
        Right: v => Right<object, Option<object>>(UnwrapOptionToPath(v))
      );

    return Right<object, Option<object>>(None);
  }

  static Option<object> UnwrapOptionToPath(object? v)
  {
    if (v is null) return None;
    var t = v.GetType();
    if (t.IsGenericType && t.GetGenericTypeDefinition().FullName == "LanguageExt.Option`1")
    {
      var attempt = ((Func<Option<object>>)(() =>
      {
        var isNone = false; object? inner = null;
        var ifNone = t.GetMethod("IfNone", [ typeof(Action) ]);
        var ifSome = t.GetMethod("IfSome", [ typeof(Action<>).MakeGenericType(t.GetGenericArguments()[0]) ]);
        if (ifNone is null || ifSome is null) return Optional(v); // fallback: keep opaque

        ifNone.Invoke(v, [ (Action)(() => isNone = true) ]);
        if (isNone) return None;

        var param = System.Linq.Expressions.Expression.Parameter(t.GetGenericArguments()[0], "x");
        var body  = System.Linq.Expressions.Expression.Call(
                      System.Linq.Expressions.Expression.Constant(new Action<object?>(o => inner = o)),
                      nameof(Action<object?>.Invoke),
                      null,
                      System.Linq.Expressions.Expression.Convert(param, typeof(object))
                    );
        var del = System.Linq.Expressions.Expression.Lambda(
                    typeof(Action<>).MakeGenericType(t.GetGenericArguments()[0]), body, param).Compile();

        ifSome.Invoke(v, [ del ]);
        return Optional(inner);
      })).Try<Option<object>, Option<object>>(_ => Optional(v));

      return attempt.Match(
        Left: opt => opt,
        Right: opt => opt
      );
    }
    return Optional(v);
  }

  public static readonly ParsePathNav<object> Reflect =
    new(
      Prop: ReflectGet,
      Index: Object.Index,
      Unbox: Object.Unbox
    );

  // ADO.NET data navigator that surfaces cell values (not rows)
  public static readonly ParsePathNav<object> Data =
    new(
      Prop: (node, name) =>
        node switch
        {
          IDataRecord r =>
            TryGetOrdinal(r, name).Match(
              None: () => Right<object, Option<object>>(None),
              Some: ord => ((Func<object?>)(() => r.IsDBNull(ord) ? null : r.GetValue(ord)))
                            .Try<object, object?>(_ => node)
                            .Match(
                              Left: _ => Left<object, Option<object>>(node),
                              Right: v => Right<object, Option<object>>(Optional(v))
                            )
            ),
          DataRow row =>
            (row.Table is { } t && t.Columns.Contains(name))
              ? ((Func<object?>)(() => row[name]))
                  .Try<object, object?>(_ => node)
                  .Match(
                    Left: _ => Left<object, Option<object>>(node),
                    Right: v => Right<object, Option<object>>(Optional(v is DBNull ? null : v))
                  )
              : Right<object, Option<object>>(None),
          _ => Left<object, Option<object>>(node)
        },

      Index: (node, i) =>
        node switch
        {
          IDataRecord r =>
            (i < 0)
              ? Left<object, Option<object>>(node)
              : (i < r.FieldCount)
                ? ((Func<object?>)(() => r.IsDBNull(i) ? null : r.GetValue(i)))
                    .Try<object, object?>(_ => node)
                    .Match(
                      Left: _ => Left<object, Option<object>>(node),
                      Right: v => Right<object, Option<object>>(Optional(v))
                    )
                : Right<object, Option<object>>(None),
          DataRow row =>
            (i < 0 || row.Table is null)
              ? Left<object, Option<object>>(node)
              : (i < row.Table.Columns.Count)
                ? ((Func<object?>)(() => row[i]))
                    .Try<object, object?>(_ => node)
                    .Match(
                      Left: _ => Left<object, Option<object>>(node),
                      Right: v => Right<object, Option<object>>(Optional(v is DBNull ? null : v))
                    )
                : Right<object, Option<object>>(None),
          _ => Left<object, Option<object>>(node)
        },

      Unbox: x => Object.Unbox(x)
    );

  static Option<int> TryGetOrdinal(IDataRecord r, string name)
  {
    try
    {
      var ord = r.GetOrdinal(name);
      return (ord >= 0 && ord < r.FieldCount) ? Optional(ord) : None;
    }
    catch
    {
      return None;
    }
  }
}