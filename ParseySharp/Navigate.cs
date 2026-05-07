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

public sealed class NavStep<S>(OneOf<NotApplicable<S>, Value<S>, Null, Absent> input)
  : OneOfBase<NotApplicable<S>, Value<S>, Null, Absent>(input);

public static class NavStep
{
  public static NavStep<S> NotApplicable<S>(S source) => new(new NotApplicable<S>(source));
  public static NavStep<S> Value<S>(S v)              => new(new Value<S>(v));
  public static NavStep<S> Null<S>()                  => new(new Null());
  public static NavStep<S> Absent<S>()                => new(new Absent());
}

public static class NavStepExtensions
{
  public static U Match<S, U>(
    this NavStep<S> step,
    Func<NotApplicable<S>, U> NotApplicable,
    Func<Value<S>, U>         Value,
    Func<Null, U>             Null,
    Func<Absent, U>           Absent) =>
    step.Match<U>(NotApplicable, Value, Null, Absent);
}

public sealed class UnboxStep(OneOf<NotApplicable<object>, Value<object>, Null> input)
  : OneOfBase<NotApplicable<object>, Value<object>, Null>(input)
{
  public static UnboxStep NotApplicable(object source) => new(new NotApplicable<object>(source));
  public static new UnboxStep Value(object o)          => new(new Value<object>(o));
  public static UnboxStep Null()                       => new(new Null());
}

public static class UnboxStepExtensions
{
  public static U Match<U>(
    this UnboxStep step,
    Func<NotApplicable<object>, U> NotApplicable,
    Func<Value<object>, U>         Value,
    Func<Null, U>                  Null) =>
    step.Match<U>(NotApplicable, Value, Null);
}

public static class PathParser
{

  public static Validation<Seq<ParsePathErr>, Unknown<B>> NextStep<B>(
    ListZipper<PathSeg> path,
    Func<B, NavStep<B>> getNext,
    Func<Unknown<B>, Validation<Seq<ParsePathErr>, Unknown<B>>> failMissing,
    Unknown<B> input
  ) =>
    input.Match(
      Value: v => getNext(v.Get).Match(
        NotApplicable: _ => failMissing(input),
        Value:         w => Success<Seq<ParsePathErr>, Unknown<B>>(Unknown.Value(w.Get)),
        Null:          _ => path.Nexts.IsEmpty
          ? Success<Seq<ParsePathErr>, Unknown<B>>(Unknown.Null<B>())
          : failMissing(input),
        Absent:        _ => path.Nexts.IsEmpty
          ? Success<Seq<ParsePathErr>, Unknown<B>>(Unknown.Absent<B>())
          : failMissing(input)),
      Null:   _ => failMissing(input),
      Absent: _ => failMissing(input));

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
              NextStep<B>(
                z,
                b => nav.Prop(b, k.Name),
                u => Fail<Seq<ParsePathErr>, Unknown<B>>([new ParsePathErr(
                  $"Missing property {k.Name}",
                  Name,
                  u.ToOption().Map(nav.CloneNode),
                  PathSegRender.ToStrings(toSeq(z.Prevs.Reverse())))]),
                cur),
            PathSeg.Index ix =>
              NextStep<B>(
                z,
                b => nav.Index(b, ix.I),
                u => Fail<Seq<ParsePathErr>, Unknown<B>>([new ParsePathErr(
                  $"Missing index {ix.I}",
                  Name,
                  u.ToOption().Map(nav.CloneNode),
                  PathSegRender.ToStrings(toSeq(z.Prevs.Reverse())))]),
                cur),
            _ =>
              Fail<Seq<ParsePathErr>, Unknown<B>>([new ParsePathErr(
                $"Unknown path segment type {z.Focus}",
                Name,
                cur.ToOption(),
                PathSegRender.ToStrings(toSeq(z.Prevs.Reverse())))])
          }
          select next);

}

public record ParsePathNav<S>(
  Func<S, string, NavStep<S>> UnsafeProp,
  Func<S, int, NavStep<S>>    UnsafeIndex,
  Func<S, UnboxStep>          UnsafeUnbox,
  Func<S, S>                  CloneNode
)
{

  public static ParsePathNav<S> Create(
    Func<S, string, NavStep<S>> Prop,
    Func<S, int, NavStep<S>>    Index,
    Func<S, UnboxStep>          Unbox,
    Func<S, S>                  CloneNode)
    => new(Prop, Index, Unbox, CloneNode);

  public NavStep<S> Prop(S input, string key) =>
    UnsafeProp(input, key).Match(
      NotApplicable: na => NavStep.NotApplicable(CloneNode(na.Source)),
      Value:         v  => NavStep.Value(CloneNode(v.Get)),
      Null:          _  => NavStep.Null<S>(),
      Absent:        _  => NavStep.Absent<S>());

  public NavStep<S> Index(S input, int i) =>
    UnsafeIndex(input, i).Match(
      NotApplicable: na => NavStep.NotApplicable(CloneNode(na.Source)),
      Value:         v  => NavStep.Value(CloneNode(v.Get)),
      Null:          _  => NavStep.Null<S>(),
      Absent:        _  => NavStep.Absent<S>());

  public UnboxStep Unbox(S input) =>
    UnsafeUnbox(input).Match(
      NotApplicable: na => UnboxStep.NotApplicable(na.Source),
      Value:         v  => UnboxStep.Value(DeepOwn(v.Get, CloneNode)!),
      Null:          _  => UnboxStep.Null());

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
          ? (je.TryGetProperty(name, out var v)
              ? (v.ValueKind == JsonValueKind.Null || v.ValueKind == JsonValueKind.Undefined
                  ? NavStep.Null<JsonElement>()
                  : NavStep.Value<JsonElement>(v))
              : NavStep.Absent<JsonElement>())
          : NavStep.NotApplicable(je),

      Index: (je, i) =>
        je.ValueKind == JsonValueKind.Array && i >= 0
          ? Optional(toSeq(je.EnumerateArray())).Filter(x => x.Count > i).Match(
              None: () => NavStep.Absent<JsonElement>(),
              Some: v => v[i].ValueKind == JsonValueKind.Null || v[i].ValueKind == JsonValueKind.Undefined
                ? NavStep.Null<JsonElement>()
                : NavStep.Value<JsonElement>(v[i])
            )
          : NavStep.NotApplicable(je),

      Unbox: je => je.ValueKind switch
      {
        JsonValueKind.String => Optional(je.GetString()).Match(
          Some: s => UnboxStep.Value(s),
          None: () => UnboxStep.Null()),
        JsonValueKind.Number => je.TryGetInt32(out var i) ? UnboxStep.Value(i)
                              : je.TryGetInt64(out var l) ? UnboxStep.Value(l)
                              : je.TryGetDouble(out var d) ? UnboxStep.Value(d)
                              : UnboxStep.NotApplicable(je),
        JsonValueKind.True => UnboxStep.Value(true),
        JsonValueKind.False => UnboxStep.Value(false),
        JsonValueKind.Null => UnboxStep.Null(),
        JsonValueKind.Undefined => UnboxStep.Null(),
        JsonValueKind.Array => UnboxStep.Value(je.EnumerateArray()),
        JsonValueKind.Object => UnboxStep.Value(je),
        _ => UnboxStep.NotApplicable(je)
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
              ? Optional(child).Match(
                  Some: c => NavStep.Value<JsonNode>(c),
                  None: () => NavStep.Null<JsonNode>())
              : NavStep.Absent<JsonNode>())
          : NavStep.NotApplicable(jn),

      Index: (jn, i) =>
        jn is JsonArray arr && i >= 0
          ? (i < arr.Count
              ? Optional(arr[i]).Match(
                  Some: c => NavStep.Value<JsonNode>(c),
                  None: () => NavStep.Null<JsonNode>())
              : NavStep.Absent<JsonNode>())
          : NavStep.NotApplicable(jn),

      Unbox: jn =>
        jn switch
        {
          null => UnboxStep.Null(),
          JsonArray arr => UnboxStep.Value(arr),
          JsonObject => UnboxStep.Value(jn),
          JsonValue v =>
            v.TryGetValue<int>(out var iv)    ? UnboxStep.Value(iv) :
            v.TryGetValue<long>(out var lv)   ? UnboxStep.Value(lv) :
            v.TryGetValue<double>(out var dv) ? UnboxStep.Value(dv) :
            v.TryGetValue<bool>(out var bv)   ? UnboxStep.Value(bv) :
            v.TryGetValue<string>(out var sv) ? UnboxStep.Value(sv)
                                              : UnboxStep.Null(),
          _ => UnboxStep.NotApplicable(jn)
        },
      CloneNode: jn => jn
    );

  public static readonly ParsePathNav<XElement> Xml =
    ParsePathNav<XElement>.Create(
      Prop: (xe, name) =>
        xe is null
          ? NavStep.NotApplicable<XElement>(xe!)
          : name.StartsWith('@')
            ? // Attribute access via @attr convention
              Optional(xe.Attribute(name[1..])).Match(
                Some: a => NavStep.Value<XElement>(new XElement("@attr", a.Value)),
                None: () => NavStep.Absent<XElement>()
              )
            : toSeq(xe.Elements()).Find(e => e.Name.LocalName == name)
                .Match(
                  Some: e => NavStep.Value<XElement>(e),
                  None: () => NavStep.Absent<XElement>()
                ),

      Index: (xe, i) =>
        xe is null || i < 0
          ? NavStep.NotApplicable<XElement>(xe!)
          : Optional(Seq(xe.Elements())).Filter(es => es.Count > i).Match(
              None: () => NavStep.Absent<XElement>(),
              Some: es => NavStep.Value<XElement>(es[i])
            ),

      Unbox: xe =>
        xe is null
          ? UnboxStep.Null()
          : xe.HasElements
            ? UnboxStep.Value(xe.Elements())
            : string.IsNullOrWhiteSpace(xe.Value)
              ? UnboxStep.Null()
              : UnboxStep.Value(xe.Value),
      CloneNode: xe => new XElement(xe)
    );

  public static readonly ParsePathNav<object> Object =
    ParsePathNav<object>.Create(
      Prop: (node, name) =>
        node switch
        {
          // For dictionary-like carriers, absence of a key should yield Absent, not NotApplicable.
          IReadOnlyDictionary<string, object?> rd =>
            rd.TryGetValue(name, out var v1)
              ? Optional(v1).Match(
                  Some: c => NavStep.Value<object>(c),
                  None: () => NavStep.Null<object>())
              : NavStep.Absent<object>(),
          IDictionary<string, object?> d =>
            d.TryGetValue(name, out var v2)
              ? Optional(v2).Match(
                  Some: c => NavStep.Value<object>(c),
                  None: () => NavStep.Null<object>())
              : NavStep.Absent<object>(),
          System.Collections.IDictionary legacy =>
            legacy.Contains(name)
              ? Optional(legacy[name]).Match(
                  Some: c => NavStep.Value<object>(c),
                  None: () => NavStep.Null<object>())
              : NavStep.Absent<object>(),
          _ => NavStep.NotApplicable(node)
        },

      Index: (node, i) =>
        node switch
        {
          IList<object?> list when i >= 0 && i < list.Count =>
            Optional(list[i]).Match(
              Some: c => NavStep.Value<object>(c),
              None: () => NavStep.Null<object>()),
          IEnumerable<object?> seq when node is not string && i >= 0 =>
              ((Func<Seq<object?>>)(() => Seq(seq))).Try<object, Seq<object?>>(_ => node).Match(
                Left: _ => NavStep.NotApplicable(node),
                Right: xs => Optional(xs).Filter(x => x.Count > i).Match(
                  None: () => NavStep.Absent<object>(),
                  Some: x => Optional(x[i]).Match(
                    Some: c => NavStep.Value<object>(c),
                    None: () => NavStep.Null<object>())
                )
              ),
          _ => NavStep.NotApplicable(node)
        },

      Unbox: x => x is null ? UnboxStep.Null() : UnboxStep.Value(x),
      CloneNode: x => x
    );

  static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, string), Func<object, Option<object>>?> _pocoCache = new();

  static Option<object> UnwrapOptionTyped<T>(Option<T> opt) =>
    opt.Match<Option<object>>(Some: x => Optional<object>(x), None: () => None);

  static Func<object, Option<object>>? BuildGetter(Type type, string name)
  {
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

    var prop = type.GetProperty(name, flags);
    if (prop is not null && prop.GetIndexParameters().Length > 0) prop = null;

    prop ??= type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                 .FirstOrDefault(p =>
                     p.GetIndexParameters().Length == 0 &&
                     p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == name);

    if (prop is not null)
      return CompileAccessor(type, prop, prop.PropertyType);

    var field = type.GetField(name, flags);
    return field is not null ? CompileAccessor(type, field, field.FieldType) : null;
  }

  static Func<object, Option<object>> CompileAccessor(Type ownerType, MemberInfo member, Type memberType)
  {
    var param = System.Linq.Expressions.Expression.Parameter(typeof(object), "obj");
    var cast = System.Linq.Expressions.Expression.Convert(param, ownerType);
    System.Linq.Expressions.Expression access = member switch
    {
      PropertyInfo p => System.Linq.Expressions.Expression.Property(cast, p),
      FieldInfo f => System.Linq.Expressions.Expression.Field(cast, f),
      _ => throw new InvalidOperationException()
    };

    if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Option<>))
    {
      var unwrap = typeof(ParsePathNav)
        .GetMethod(nameof(UnwrapOptionTyped), BindingFlags.Static | BindingFlags.NonPublic)!
        .MakeGenericMethod(memberType.GetGenericArguments()[0]);
      return System.Linq.Expressions.Expression
        .Lambda<Func<object, Option<object>>>(
          System.Linq.Expressions.Expression.Call(unwrap, access), param)
        .Compile();
    }

    var boxed = System.Linq.Expressions.Expression.Convert(access, typeof(object));
    var rawGetter = System.Linq.Expressions.Expression
      .Lambda<Func<object, object?>>(boxed, param)
      .Compile();
    return obj => Optional(rawGetter(obj));
  }

  static NavStep<object> ReflectGet(object node, string name)
  {
    var getter = _pocoCache.GetOrAdd((node.GetType(), name), key => BuildGetter(key.Item1, key.Item2));
    if (getter is null)
      return NavStep.Absent<object>();
    try
    {
      return getter(node).Match(
        Some: c => NavStep.Value<object>(c),
        None: () => NavStep.Null<object>());
    }
    catch
    {
      return NavStep.NotApplicable(node);
    }
  }

  public static readonly ParsePathNav<object> Poco =
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
              None: () => NavStep.Absent<object>(),
              Some: ord => ((Func<object?>)(() => r.IsDBNull(ord) ? null : r.GetValue(ord)))
                            .Try<object, object?>(_ => node)
                            .Match(
                              Left: _ => NavStep.NotApplicable(node),
                              Right: v => Optional(v).Match(
                                Some: c => NavStep.Value<object>(c),
                                None: () => NavStep.Null<object>())
                            )
            ),
          DataRow row =>
            (row.Table is { } t && t.Columns.Contains(name))
              ? ((Func<object?>)(() => row[name]))
                  .Try<object, object?>(_ => node)
                  .Match(
                    Left: _ => NavStep.NotApplicable(node),
                    Right: v => Optional(v is DBNull ? null : v).Match(
                      Some: c => NavStep.Value<object>(c),
                      None: () => NavStep.Null<object>())
                  )
              : NavStep.Absent<object>(),
          _ => NavStep.NotApplicable(node)
        },

      Index: (node, i) =>
        node switch
        {
          IDataRecord r =>
            (i < 0)
              ? NavStep.NotApplicable(node)
              : (i < r.FieldCount)
                ? ((Func<object?>)(() => r.IsDBNull(i) ? null : r.GetValue(i)))
                    .Try<object, object?>(_ => node)
                    .Match(
                      Left: _ => NavStep.NotApplicable(node),
                      Right: v => Optional(v).Match(
                        Some: c => NavStep.Value<object>(c),
                        None: () => NavStep.Null<object>())
                    )
                : NavStep.Absent<object>(),
          DataRow row =>
            (i < 0 || row.Table is null)
              ? NavStep.NotApplicable(node)
              : (i < row.Table.Columns.Count)
                ? ((Func<object?>)(() => row[i]))
                    .Try<object, object?>(_ => node)
                    .Match(
                      Left: _ => NavStep.NotApplicable(node),
                      Right: v => Optional(v is DBNull ? null : v).Match(
                        Some: c => NavStep.Value<object>(c),
                        None: () => NavStep.Null<object>())
                    )
                : NavStep.Absent<object>(),
          _ => NavStep.NotApplicable(node)
        },

      Unbox: x => Object.UnsafeUnbox(x),
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
