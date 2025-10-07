using Amazon.DynamoDBv2.Model;
using ParseySharp.DynamoDb;

namespace ParseySharp;

public static class ParseDynamoDbExtensions
{
  public static Func<AttributeValue, Validation<Seq<ParsePathErr>, A>> ParseDynamoDb<A>(this Parse<A> parser) =>
    ParseExtensions.RunWithNav(parser, ParsePathNavDynamoDb.DynamoDb);
}
