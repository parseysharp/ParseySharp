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
    Func<B, Either<Unknown<B>, Option<B>>> getNext,
    Func<Option<B>, Validation<Seq<ParsePathErr>, B>> runNext,
    Option<B> input
  ) => input.Match(
    None: () => runNext(None).Map(Some),
    Some: i => getNext(i).Match<Validation<Seq<ParsePathErr>, Option<B>>>(
      Left: _ => runNext(None).Map(Some),
      Right: x => x.Match(
        None: () => path.Nexts.IsEmpty
          ? Success<Seq<ParsePathErr>, Option<B>>(None)
          : runNext(None).Map(Some),
        Some: x => runNext(x).Map(Some)
      )
    )
  );

  public static Validation<Seq<ParsePathErr>, Unknown<B>> NextStepU<B>(
    ListZipper<PathSeg> path,
    Func<B, Either<Unknown<B>, Option<B>>> getNext,
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
                    cur.Map(nav.CloneNode),
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
                  cur.Map(nav.CloneNode),
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

public record ParsePathNav<S>(
  Func<S, string, Either<Unknown<S>, Option<S>>> UnsafeProp,
  Func<S, int, Either<Unknown<S>, Option<S>>> UnsafeIndex,
  Func<S, Either<Unknown<S>, Unknown<object>>> UnsafeUnbox,
  Func<S, S> CloneNode
)
{

  public static ParsePathNav<S> Create(
    Func<S, string, Either<Unknown<S>, Option<S>>> Prop,
    Func<S, int, Either<Unknown<S>, Option<S>>> Index,
    Func<S, Either<Unknown<S>, Unknown<object>>> Unbox,
    Func<S, S> CloneNode)
    => new(Prop, Index, Unbox, CloneNode);

  public Func<S, string, Either<Unknown<S>, Option<S>>> Prop => (node, name) =>
    UnsafeProp(node, name).Match(
      Left: l => Left<Unknown<S>, Option<S>>(l.Map(CloneNode)),
      Right: r => r
    );

  public Func<S, int, Either<Unknown<S>, Option<S>>> Index => (node, i) =>
    UnsafeIndex(node, i).Match(
      Left: l => Left<Unknown<S>, Option<S>>(l.Map(CloneNode)),
      Right: r => r
    );

  public Func<S, Either<Unknown<S>, Unknown<object>>> Unbox => node =>
    UnsafeUnbox(node).Match(
      Left: l => Left<Unknown<S>, Unknown<object>>(l),
      Right: u => u.Match(
        Some: v => Right<Unknown<S>, Unknown<object>>(Unknown.UnsafeFromOption<object>(Optional(DeepOwn(v, CloneNode)))),
        None: () => Right<Unknown<S>, Unknown<object>>(new Unknown<object>.None())
      )
    );

  static object? DeepOwn(object? v, Func<S, S> clone)
  {
    if (v is null) return null;
    if (v is S node) return clone(node);
    if (v is string || v is bool || v is int || v is long || v is double || v is float || v is decimal || v is Guid || v is DateTime || v is DateTimeOffset)
      return v;
    if (v is byte[] bytes) return bytes.ToArray();

    if (v is System.Collections.IDictionary dict)
    {
      var obj = new Dictionary<string, object?>();
      foreach (System.Collections.DictionaryEntry de in dict)
      {
        var key = de.Key?.ToString() ?? "null";
        obj[key] = DeepOwn(de.Value, clone);
      }
      return obj;
    }

    if (v is System.Collections.IEnumerable seq && v is not string)
    {
      var list = new List<object?>();
      var allS = true;
      foreach (var item in seq)
      {
        var owned = DeepOwn(item, clone);
        list.Add(owned);
        if (owned is not S) allS = false;
      }
      if (allS)
      {
        // Preserve strong-typed sequence of carrier nodes to satisfy parsers expecting Seq<S>
        var cast = list.Cast<S>();
        return Seq(cast);
      }
      return list;
    }

    return v;
  }
}

public static class ParsePathNav
{
  public static readonly ParsePathNav<JsonElement> Json =
  ParsePathNav<JsonElement>.Create(
      Prop: (je, name) =>
        je.ValueKind == JsonValueKind.Object
          ? Right<Unknown<JsonElement>, Option<JsonElement>>(Optional(je.TryGetProperty(name, out var v) ? v : default))
          : Left<Unknown<JsonElement>, Option<JsonElement>>(Unknown.New(je)),

      Index: (je, i) =>
        je.ValueKind == JsonValueKind.Array && i >= 0
          ? Optional(toSeq(je.EnumerateArray())).Filter(x => x.Count > i).Match(
              None: () => Right<Unknown<JsonElement>, Option<JsonElement>>(None),
            Some: v => Right<Unknown<JsonElement>, Option<JsonElement>>(Optional(v[i]))
          )
          : Left<Unknown<JsonElement>, Option<JsonElement>>(Unknown.New(je)),

      Unbox: je => je.ValueKind switch
      {
        JsonValueKind.String => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.UnsafeFromOption(Optional<object>(je.GetString()))),
        JsonValueKind.Number => je.TryGetInt32(out var i) ? Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(i))
                              : je.TryGetInt64(out var l) ? Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(l))
                              : je.TryGetDouble(out var d) ? Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(d))
                              : Left<Unknown<JsonElement>, Unknown<object>>(Unknown.New(je)),
        JsonValueKind.True => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(true)),
        JsonValueKind.False => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(false)),
        JsonValueKind.Null => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.UnsafeFromOption<object>(None)),
        JsonValueKind.Undefined => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.UnsafeFromOption<object>(None)),
        JsonValueKind.Array => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(je.EnumerateArray())),
        JsonValueKind.Object => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(je)),
        _ => Left<Unknown<JsonElement>, Unknown<object>>(Unknown.New(je))
      },
      CloneNode: je =>
        je.ValueKind == JsonValueKind.Undefined
          ? JsonDocument.Parse("null").RootElement.Clone()
          : je.Clone()
    );

  public static readonly ParsePathNav<JsonNode> Nodes =
    ParsePathNav<JsonNode>.Create(
      Prop: (jn, name) =>
        jn is JsonObject obj
          ? (obj.TryGetPropertyValue(name, out var child)
              ? Right<Unknown<JsonNode>, Option<JsonNode>>(Optional(child))
              : Right<Unknown<JsonNode>, Option<JsonNode>>(None))
          : Left<Unknown<JsonNode>, Option<JsonNode>>(Unknown.New(jn)),

      Index: (jn, i) =>
        jn is JsonArray arr && i >= 0
          ? (i < arr.Count
              ? Right<Unknown<JsonNode>, Option<JsonNode>>(Optional(arr[i]))
              : Right<Unknown<JsonNode>, Option<JsonNode>>(None))
          : Left<Unknown<JsonNode>, Option<JsonNode>>(Unknown.New(jn)),

      Unbox: jn =>
        jn switch
        {
          null => Right<Unknown<JsonNode>, Unknown<object>>(new Unknown<object>.None()),
          JsonArray arr => Right<Unknown<JsonNode>, Unknown<object>>(Unknown.New<object>(arr)),
          JsonObject => Right<Unknown<JsonNode>, Unknown<object>>(Unknown.New<object>(jn)),
          JsonValue v =>
            v.TryGetValue<int>(out var iv)    ? Right<Unknown<JsonNode>, Unknown<object>>(Unknown.New<object>(iv)) :
            v.TryGetValue<long>(out var lv)   ? Right<Unknown<JsonNode>, Unknown<object>>(Unknown.New<object>(lv)) :
            v.TryGetValue<double>(out var dv) ? Right<Unknown<JsonNode>, Unknown<object>>(Unknown.New<object>(dv)) :
            v.TryGetValue<bool>(out var bv)   ? Right<Unknown<JsonNode>, Unknown<object>>(Unknown.New<object>(bv)) :
            v.TryGetValue<string>(out var sv) ? (string.IsNullOrWhiteSpace(sv)
                                                  ? Right<Unknown<JsonNode>, Unknown<object>>(Unknown.UnsafeFromOption<object>(None))
                                                  : Right<Unknown<JsonNode>, Unknown<object>>(Unknown.New<object>(sv)))
                                              : Right<Unknown<JsonNode>, Unknown<object>>(Unknown.UnsafeFromOption<object>(None)),
          _ => Left<Unknown<JsonNode>, Unknown<object>>(Unknown.New(jn))
        },
      CloneNode: jn => jn
    );

  public static readonly ParsePathNav<XElement> Xml =
    ParsePathNav<XElement>.Create(
      Prop: (xe, name) =>
        xe is null
          ? Left<Unknown<XElement>, Option<XElement>>(new Unknown<XElement>.None())
          : name.StartsWith('@')
            ? // Attribute access via @attr convention
              Optional(xe.Attribute(name[1..])).Match(
                Some: a => Right<Unknown<XElement>, Option<XElement>>(Optional(new XElement("@attr", a.Value))),
                None: () => Right<Unknown<XElement>, Option<XElement>>(None)
              )
            : toSeq(xe.Elements()).Find(e => e.Name.LocalName == name)
                .Match(
                  Some: e => Right<Unknown<XElement>, Option<XElement>>(Optional(e)),
                  None: () => Right<Unknown<XElement>, Option<XElement>>(None)
                ),

      Index: (xe, i) =>
        xe is null || i < 0
          ? Left<Unknown<XElement>, Option<XElement>>(xe is null ? new Unknown<XElement>.None() : Unknown.New(xe))
          : Optional(Seq(xe.Elements())).Filter(es => es.Count > i).Match(
              None: () => Right<Unknown<XElement>, Option<XElement>>(None),
              Some: es => Right<Unknown<XElement>, Option<XElement>>(Optional(es[i]))
            ),

      Unbox: xe =>
        xe is null
          ? Right<Unknown<XElement>, Unknown<object>>(new Unknown<object>.None())
          : xe.HasElements
            ? Right<Unknown<XElement>, Unknown<object>>(Unknown.New<object>(xe.Elements()))
            : string.IsNullOrWhiteSpace(xe.Value)
              ? Right<Unknown<XElement>, Unknown<object>>(Unknown.UnsafeFromOption<object>(None))
              : Right<Unknown<XElement>, Unknown<object>>(Unknown.New<object>(xe.Value)),
      CloneNode: xe => new XElement(xe)
    );

  public static readonly ParsePathNav<object> Object =
    ParsePathNav<object>.Create(
      Prop: (node, name) =>
        node switch
        {
          // For dictionary-like carriers, absence of a key should yield Optional(None), not Left.
          IReadOnlyDictionary<string, object?> rd => Right<Unknown<object>, Option<object>>(Optional(rd.TryGetValue(name, out var v1) ? v1 : null)),
          IDictionary<string, object?> d => Right<Unknown<object>, Option<object>>(Optional(d.TryGetValue(name, out var v2) ? v2 : null)),
          System.Collections.IDictionary legacy => Right<Unknown<object>, Option<object>>(Optional(legacy.Contains(name) ? legacy[name] : null)),
          _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)) 
        },

      Index: (node, i) =>
        node switch
        {
          IList<object?> list when i >= 0 && i < list.Count => Right<Unknown<object>, Option<object>>(Optional(list[i])),
          IEnumerable<object?> seq when node is not string && i >= 0 =>
              ((Func<Seq<object?>>)(() => Seq(seq))).Try<object, Seq<object?>>(_ => node).Match(
                Left: _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)),
                Right: xs => Optional(xs).Filter(x => x.Count > i).Match(
                  None: () => Right<Unknown<object>, Option<object>>(None),
                  Some: x => Right<Unknown<object>, Option<object>>(Optional(x[i]))
                )
              ),
          _ => Left<Unknown<object>, Option<object>>(Unknown.New(node))
        },

      Unbox: x => Right<Unknown<object>, Unknown<object>>(Unknown.New(x)),
      CloneNode: x => x
    );

  static Either<Unknown<object>, Option<object>> ReflectGet(object node, string name)
  {
    var t = node.GetType();

    var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
    if (p is not null && p.GetIndexParameters().Length == 0)
      return ((Func<object?>)(() => p.GetValue(node))).Try<object, object?>(_ => node).Match(
        Left: _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)),
        Right: v => Right<Unknown<object>, Option<object>>(UnwrapOptionToPath(v))
      );

    var pj = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
              .FirstOrDefault(pp =>
                  pp.GetIndexParameters().Length == 0 &&
                  pp.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == name);
    if (pj is not null)
      return ((Func<object?>)(() => pj.GetValue(node))).Try<object, object?>(_ => node).Match(
        Left: _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)),
        Right: v => Right<Unknown<object>, Option<object>>(UnwrapOptionToPath(v))
      );

    var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
    if (f is not null)
      return ((Func<object?>)(() => f.GetValue(node))).Try<object, object?>(_ => node).Match(
        Left: _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)),
        Right: v => Right<Unknown<object>, Option<object>>(UnwrapOptionToPath(v))
      );

    return Right<Unknown<object>, Option<object>>(None);
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
    ParsePathNav<object>.Create(
      Prop: ReflectGet,
      Index: Object.Index,
      Unbox: Object.Unbox,
      CloneNode: x => x
    );

  // ADO.NET data navigator that surfaces cell values (not rows)
  public static readonly ParsePathNav<object> Data =
    ParsePathNav<object>.Create(
      Prop: (node, name) =>
        node switch
        {
          IDataRecord r =>
            TryGetOrdinal(r, name).Match(
              None: () => Right<Unknown<object>, Option<object>>(None),
              Some: ord => ((Func<object?>)(() => r.IsDBNull(ord) ? null : r.GetValue(ord)))
                            .Try<object, object?>(_ => node)
                            .Match(
                              Left: _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)),
                              Right: v => Right<Unknown<object>, Option<object>>(Optional(v))
                            )
            ),
          DataRow row =>
            (row.Table is { } t && t.Columns.Contains(name))
              ? ((Func<object?>)(() => row[name]))
                  .Try<object, object?>(_ => node)
                  .Match(
                    Left: _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)),
                    Right: v => Right<Unknown<object>, Option<object>>(Optional(v is DBNull ? null : v))
                  )
              : Right<Unknown<object>, Option<object>>(None),
          _ => Left<Unknown<object>, Option<object>>(Unknown.New(node))
        },

      Index: (node, i) =>
        node switch
        {
          IDataRecord r =>
            (i < 0)
              ? Left<Unknown<object>, Option<object>>(Unknown.New(node))
              : (i < r.FieldCount)
                ? ((Func<object?>)(() => r.IsDBNull(i) ? null : r.GetValue(i)))
                    .Try<object, object?>(_ => node)
                    .Match(
                      Left: _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)),
                      Right: v => Right<Unknown<object>, Option<object>>(Optional(v))
                    )
                : Right<Unknown<object>, Option<object>>(None),
          DataRow row =>
            (i < 0 || row.Table is null)
              ? Left<Unknown<object>, Option<object>>(Unknown.New(node))
              : (i < row.Table.Columns.Count)
                ? ((Func<object?>)(() => row[i]))
                    .Try<object, object?>(_ => node)
                    .Match(
                      Left: _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)),
                      Right: v => Right<Unknown<object>, Option<object>>(Optional(v is DBNull ? null : v))
                    )
                : Right<Unknown<object>, Option<object>>(None),
          _ => Left<Unknown<object>, Option<object>>(Unknown.New(node))
        },

      Unbox: x => Object.Unbox(x),
      CloneNode: x => x
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