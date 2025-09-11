using Newtonsoft.Json.Linq;

namespace ParseySharp;

// Separate type to avoid redefining ParsePathNav from core
public static class ParsePathNavNewtonsoft
{
  public static readonly ParsePathNav<JToken> JsonNet =
    new(
      Prop: (jt, name) =>
        jt switch
        {
          JObject obj =>
            (obj.TryGetValue(name, out var child)
              ? Right<object, Option<JToken>>(Optional(child))
              : Right<object, Option<JToken>>(None)),
          _ => Left<object, Option<JToken>>(jt)
        },

      Index: (jt, i) =>
        jt switch
        {
          JArray arr when i >= 0 =>
            (i < arr.Count
              ? Right<object, Option<JToken>>(Optional(arr[i]))
              : Right<object, Option<JToken>>(None)),
          _ => Left<object, Option<JToken>>(jt)
        },

      Unbox: jt => jt switch
      {
        null => Right<object, Unknown<object>>(new Unknown<object>.None()),

        // Keep arrays/objects as-is (so Seq and nested parsing can work)
        JObject or JArray => Right<object, Unknown<object>>(Unknown.New<object>(jt)),

        // Null token => None
        JValue v when v.Type == JTokenType.Null
          => Right<object, Unknown<object>>(Unknown.UnsafeFromOption<object>(None)),

        // Strings
        JValue v when v.Type == JTokenType.String
          => Right<object, Unknown<object>>(Unknown.New<object>(v.Value<string>()!)),

        // Booleans
        JValue v when v.Type == JTokenType.Boolean
          => Right<object, Unknown<object>>(Unknown.New<object>(v.Value<bool>())),

        // Integers (prefer Int32, then Int64); guard against provider overflow by Try-wrapping
        JValue v when v.Type == JTokenType.Integer
          => ((Func<Either<object, Unknown<object>>>)(() =>
            {
              var l = v.Value<long>();
              return (l <= int.MaxValue && l >= int.MinValue)
                ? Right<object, Unknown<object>>(Unknown.New<object>((int)l))
                : Right<object, Unknown<object>>(Unknown.New<object>(l));
            }))
            .Try<object, Either<object, Unknown<object>>>(_ => jt)
            .Match(
              Left: _ => Right<object, Unknown<object>>(Unknown.New<object>(v.ToString())),
              Right: r => r
            ),

        // Floats/Decimals (use double)
        JValue v when v.Type == JTokenType.Float
          => Right<object, Unknown<object>>(Unknown.New<object>(v.Value<double>())),

        // Otherwise fall back to string representation (or treat as None if you prefer)
        JValue v
          => Right<object, Unknown<object>>(Unknown.New<object>(v.ToString())),

        _ => Left<object, Unknown<object>>(jt)
      }
        );
}

public static class ParseNewtonsoftJsonExtensions
{
  public static Func<JToken, Validation<Seq<ParsePathErr>, A>> ParseNewtonsoftJson<A>(this Parse<A> parser) =>
    ParseExtensions.RunWithNav(parser, ParsePathNavNewtonsoft.JsonNet);
}