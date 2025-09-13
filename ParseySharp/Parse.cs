namespace ParseySharp;

using System.Text.Json;
using System.Xml.Linq;
using System.Text.Json.Nodes;
using System.Data;

#pragma warning disable IDE1006 // Naming Styles
public interface Parse<A>: K<Parse, A>
{
  public abstract Func<Unknown<B>, Validation<Seq<ParsePathErr>, A>> Run<B>(ParsePathNav<B> nav);
}
#pragma warning restore IDE1006 // Naming Styles

public class FilterParse<A, X>(Parse<A> pa, Func<A, Validation<Seq<ParsePathErr>, X>> f): Parse<X>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, X>> Run<B>(ParsePathNav<B> nav) =>
    input => pa.Run<B>(nav)(input).Bind(x => f(x));
}

public class FailParse<A>(Seq<ParsePathErr> errors): Parse<A>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, A>> Run<B>(ParsePathNav<B> nav) =>
    input => Fail<Seq<ParsePathErr>, A>(errors);
}

public class BindParse<A, X>(Parse<A> pa, Func<A, Parse<X>> f): Parse<X>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, X>> Run<B>(ParsePathNav<B> nav) =>
    input => pa.Run<B>(nav)(input).Bind(x => f(x).Run<B>(nav)(input));
}

public class MapParse<A, X>(Parse<A> pa, Func<A, X> f): Parse<X>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, X>> Run<B>(ParsePathNav<B> nav) =>
    input => pa.Run<B>(nav)(input).Map(f);
}

public class ApplyParse<A, B, C>(Parse<A> pa, Parse<B> pb, Func<A, B, C> f): Parse<C>
{
  public Func<Unknown<X>, Validation<Seq<ParsePathErr>, C>> Run<X>(ParsePathNav<X> nav) =>
    input => (pa.Run<X>(nav)(input), pb.Run<X>(nav)(input)).Apply(f).As();
}

public class PureParse<A>(A value): Parse<A>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, A>> Run<B>(ParsePathNav<B> nav) =>
    input => Success<Seq<ParsePathErr>, A>(value);
}

public class ValueParse<A>(Func<Unknown<object>, Validation<Seq<ParsePathErr>, A>> run): Parse<A>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, A>> Run<B>(ParsePathNav<B> nav) =>
    input => input.Match(
      None: () => run(new Unknown<object>.None()),
      Some: i => nav.Unbox(i).Match(
        Left: l => Fail<Seq<ParsePathErr>, A>([ParsePathErr.FromParseErr(new ParseErr("Could not unbox value", typeof(A).Name, l), [])]),
        Right: x => run(x)
      )
    );
}

public class OrElseParse<A>(Parse<A> p1, Parse<A> p2): Parse<A>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, A>> Run<B>(ParsePathNav<B> nav) =>
    input => p1.Run<B>(nav)(input).Match(
      Succ: r => Success<Seq<ParsePathErr>, A>(r),
      Fail: e1 => p2.Run<B>(nav)(input)
    );
}

public class PathParse<A>(string Name, ListZipper<PathSeg> Path, Parse<A> parser): Parse<A>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, A>> Run<B>(ParsePathNav<B> nav) =>
    input => Parse.PrefixErrors(PathParser.Navigate(nav, Path, Name, input).Bind(x => parser.Run<B>(nav)(x)),
      PathSegRender.ToStrings(Path.ToSeq()));
}

public class SeqParse<A>(Parse<A> parser): Parse<Seq<A>>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, Seq<A>>> Run<B>(ParsePathNav<B> nav) =>
    input => Parse.Seq<B>().Run<B>(nav)(input)
      .Bind(xs => xs.Zip(toSeq(Range(0, int.MaxValue))).Traverse(
        t => Parse.PrefixErrors(parser.Run(nav)(Unknown.New(t.First)), [$"[{t.Second}]"])));
}

public class OptionParse<A>(Parse<A> parser): Parse<Option<A>>
{
  public Func<Unknown<B>, Validation<Seq<ParsePathErr>, Option<A>>> Run<B>(ParsePathNav<B> nav) =>
    input => input.Match(
     None: () => Success<Seq<ParsePathErr>, Option<A>>(None),
     Some: i => nav.Unbox(i).Match(
       Left: l => Fail<Seq<ParsePathErr>, Option<A>>([ParsePathErr.FromParseErr(new ParseErr("Could not unbox value", typeof(A).Name, l), [])]),
       Right: x => x.Match(
        Some: x => parser.Run<B>(nav)(input).Map(Optional),
        None: () => Success<Seq<ParsePathErr>, Option<A>>(None))));
}

public static class ParseExtensions
{
  public static Parse<A> As<A>(this K<Parse, A> pa) =>
    (Parse<A>)pa;

  public static Parse<Option<A>> Option<A>(this Parse<A> parser) =>
    new OptionParse<A>(parser);

  public static Parse<B> Filter<A, B>(this Parse<A> parser, Func<A, Validation<Seq<ParsePathErr>, B>> f) =>
    new FilterParse<A, B>(parser, f);

  public static Parse<A> Filter<A>(this Parse<A> parser, Func<A, Option<string>> f) =>
    Filter(parser, x => f(x).Match(
      None: () => Success<Seq<ParsePathErr>, A>(x),
      Some: e => Fail<Seq<ParsePathErr>, A>([new ParsePathErr(e, typeof(A).Name, Optional(x), [])])));

  public static Parse<B> Filter<A, B>(this Parse<A> parser, Func<A, Either<string, B>> f) =>
    Filter(parser, x => f(x).Match(
      Right: b => Success<Seq<ParsePathErr>, B>(b),
      Left: e => Fail<Seq<ParsePathErr>, B>([new ParsePathErr(e, typeof(A).Name, Optional(x), [])])));

  public static Parse<A> OrElse<A>(this Parse<A> parser, Parse<A> other) =>
    new OrElseParse<A>(parser, other);

  public static Parse<Seq<A>> Seq<A>(this Parse<A> parser) =>
    new SeqParse<A>(parser);

  public static Parse<A> At<A>(this Parse<A> parser, PathSeg head, Seq<PathSeg> tail) =>
    new PathParse<A>(typeof(A).Name, ListZipper.FromCons(head, tail), parser);
    
  public static Func<Option<B>, Validation<Seq<ParsePathErr>, A>> RunNullableWithNav<A, B>(this Parse<A> parser, ParsePathNav<B> nav) =>
    input => parser.Run<B>(nav)(Unknown.UnsafeFromOption(input));

  public static Func<object, Validation<Seq<ParsePathErr>, A>> ParseObject<A>(this Parse<A> parser) => RunWithNav(parser, ParsePathNav.Object);

  public static Func<object, Validation<Seq<ParsePathErr>, A>> ParseReflect<A>(this Parse<A> parser) => RunWithNav(parser, ParsePathNav.Reflect);

  public static Func<JsonElement, Validation<Seq<ParsePathErr>, A>> ParseJson<A>(this Parse<A> parser) => RunWithNav(parser, ParsePathNav.Json);

  public static Func<XElement, Validation<Seq<ParsePathErr>, A>> ParseXml<A>(this Parse<A> parser) => RunWithNav(parser, ParsePathNav.Xml);

  public static Func<JsonNode, Validation<Seq<ParsePathErr>, A>> ParseJsonNode<A>(this Parse<A> parser) => RunWithNav(parser, ParsePathNav.Nodes);

  public static Func<DataRow, Validation<Seq<ParsePathErr>, A>> ParseDataRow<A>(this Parse<A> parser) =>
  row => parser.RunWithNav(ParsePathNav.Data)(row);

  public static Func<IDataRecord, Validation<Seq<ParsePathErr>, A>> ParseDataRecord<A>(this Parse<A> parser) =>
  rec => parser.RunWithNav(ParsePathNav.Data)(rec);

  public static Func<DataTable, Validation<Seq<ParsePathErr>, Seq<A>>> ParseDataTableSeq<A>(this Parse<A> parser) =>
    table => parser.Seq().RunWithNav(ParsePathNav.Data)(table.Rows.Cast<DataRow>().ToList());

  public static Func<IEnumerable<DataRow>, Validation<Seq<ParsePathErr>, Seq<A>>> ParseDataRowsSeq<A>(this Parse<A> parser) =>
    rows => parser.Seq().RunWithNav(ParsePathNav.Data)(rows.ToList());

  public static IEnumerable<Validation<Seq<ParsePathErr>, A>> ParseDataTableStream<A>(
    this Parse<A> parser, DataTable table)
  {
    foreach (DataRow row in table.Rows)
      yield return parser.ParseDataRow()(row);
  }

  public static IEnumerable<Validation<Seq<ParsePathErr>, A>> ParseDataRowsStream<A>(
    this Parse<A> parser, IEnumerable<DataRow> rows)
  {
    foreach (var row in rows)
      yield return parser.ParseDataRow()(row);
  }

  public static IEnumerable<Validation<Seq<ParsePathErr>, A>> ParseDataReaderStream<A>(
    this Parse<A> parser, IDataReader reader)
  {
    while (reader.Read())
      yield return parser.ParseDataRecord()(reader);
  }

  public static Func<B, Validation<Seq<ParsePathErr>, A>> RunWithNav<A, B>(this Parse<A> parser, ParsePathNav<B> nav) =>
    input => parser.Run<B>(nav)(Unknown.New(input));
}

public partial class Parse: Monad<Parse>, Applicative<Parse>
{
  public static K<Parse, B> Bind<A, B>(K<Parse, A> pa, Func<A, K<Parse, B>> f) =>
    new BindParse<A, B>(pa.As(), x => f(x).As());

  public static K<Parse, B> Map<A, B>(Func<A, B> f, K<Parse, A> pa) =>
    new MapParse<A, B>(pa.As(), f);

  public static K<Parse, B> Apply<A, B>(K<Parse, Func<A, B>> pf, K<Parse, A> pa) =>
    new ApplyParse<Func<A, B>, A, B>(pf.As(), pa.As(), (f, a) => f(a));

  public static K<Parse, A> Pure<A>(A a) => new PureParse<A>(a);

  public static Validation<Seq<ParsePathErr>, object> NotNull<A>(Unknown<object> input) =>
    input.Match(
      None: () => Fail<Seq<ParsePathErr>, object>([new ParsePathErr("Null or missing value", typeof(A).Name, None, [])]),
      Some: x => Success<Seq<ParsePathErr>, object>(x));

  public static Parse<A> As<A>() =>
    new ValueParse<A>(x =>
    NotNull<A>(x).Bind(x => x is A a
      ? Success<Seq<ParsePathErr>, A>(a)
      : Fail<Seq<ParsePathErr>, A>(
          [new ParsePathErr("Type mismatch", typeof(A).Name, Optional(x), [])])));

  public static Parse<Seq<A>> Seq<A>() =>
    new ValueParse<Seq<A>>(x => NotNull<A>(x).Bind(x => x is Seq<A> s
    ? Success<Seq<ParsePathErr>, Seq<A>>(s)
    : x is IEnumerable<A> e && e is not string
    ? Success<Seq<ParsePathErr>, Seq<A>>(toSeq(e))
    : Fail<Seq<ParsePathErr>, Seq<A>>([new ParsePathErr("Type mismatch", $"Seq<{typeof(A).Name}>", Optional(x), [])])));

  public static Parse<A> Fail<A>(Seq<ParsePathErr> errors) =>
    new FailParse<A>(errors);

  public static Parse<A> Fail<A>(string message) =>
    new FailParse<A>([new ParsePathErr(message, "", None, [])]);

  public static Validation<Seq<ParsePathErr>, X> PrefixErrors<X>(Validation<Seq<ParsePathErr>, X> input, Seq<string> pathPrefix) =>
    input.MapFail(err => err.Map(e => e.WithPrefix(pathPrefix)));
}