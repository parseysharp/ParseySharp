using Avro.Generic;
using ParseySharp.Avro;

namespace ParseySharp;

public static class ParseAvroExtensions
{
  public static Func<object, Validation<Seq<ParsePathErr>, A>> ParseAvro<A>(this Parse<A> parser) =>
    carrier => ParseExtensions.RunWithNav(parser, ParsePathNavAvro.Avro)(carrier);
}
