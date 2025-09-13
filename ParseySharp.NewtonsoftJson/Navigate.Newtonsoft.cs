using Newtonsoft.Json.Linq;

namespace ParseySharp;

// Separate type to avoid redefining ParsePathNav from core
public static class ParsePathNavNewtonsoft
{
  public static readonly ParsePathNav<JToken> JsonNet =
    ParsePathNav<JToken>.Create(
      Prop: (jt, name) =>
        jt switch
        {
          JObject obj =>
            (obj.TryGetValue(name, out var child)
              ? Right<Unknown<JToken>, Option<JToken>>(Optional(child))
              : Right<Unknown<JToken>, Option<JToken>>(None)),
          _ => Left<Unknown<JToken>, Option<JToken>>(Unknown.New(jt))
        },

      Index: (jt, i) =>
        jt switch
        {
          JArray arr when i >= 0 =>
            (i < arr.Count
              ? Right<Unknown<JToken>, Option<JToken>>(Optional(arr[i]))
              : Right<Unknown<JToken>, Option<JToken>>(None)),
          _ => Left<Unknown<JToken>, Option<JToken>>(Unknown.New(jt))
        },

      Unbox: jt => jt switch
      {
        null => Right<Unknown<JToken>, Unknown<object>>(new Unknown<object>.None()),

        // Keep arrays/objects as-is (so Seq and nested parsing can work)
        JObject or JArray => Right<Unknown<JToken>, Unknown<object>>(Unknown.New<object>(jt)),

        // Null token => None
        JValue v when v.Type == JTokenType.Null
          => Right<Unknown<JToken>, Unknown<object>>(Unknown.UnsafeFromOption<object>(None)),

        // Strings
        JValue v when v.Type == JTokenType.String
          => Identity.Pure(v.Value<string>()).Map(x => x is null ? new Unknown<object>.None() : Unknown.New<object>(x)).Run(),

        // Booleans
        JValue v when v.Type == JTokenType.Boolean
          => Right<Unknown<JToken>, Unknown<object>>(Unknown.New<object>(v.Value<bool>())),

        // Integers (prefer Int32, then Int64); guard against provider overflow by Try-wrapping
        JValue v when v.Type == JTokenType.Integer
          => ((Func<Either<Unknown<JToken>, Unknown<object>>>)(() =>
            {
              var l = v.Value<long>();
              return (l <= int.MaxValue && l >= int.MinValue)
                ? Right<Unknown<JToken>, Unknown<object>>(Unknown.New<object>((int)l))
                : Right<Unknown<JToken>, Unknown<object>>(Unknown.New<object>(l));
            }))
            .Try<Unknown<JToken>, Either<Unknown<JToken>, Unknown<object>>>(_ => Unknown.New(jt))
            .Match(
              Left: _ => Right<Unknown<JToken>, Unknown<object>>(Unknown.New<object>(v.ToString())),
              Right: r => r
            ),

        // Floats/Decimals (use double)
        JValue v when v.Type == JTokenType.Float
          => Right<Unknown<JToken>, Unknown<object>>(Unknown.New<object>(v.Value<double>())),

        // Otherwise fall back to string representation (or treat as None if you prefer)
        JValue v
          => Right<Unknown<JToken>, Unknown<object>>(Unknown.New<object>(v.ToString())),

        _ => Left<Unknown<JToken>, Unknown<object>>(Unknown.New(jt))
      },
      CloneNode: x => x
    );
}

public static class ParseNewtonsoftJsonExtensions
{
  public static Func<JToken, Validation<Seq<ParsePathErr>, A>> ParseNewtonsoftJson<A>(this Parse<A> parser) =>
    ParseExtensions.RunWithNav(parser, ParsePathNavNewtonsoft.JsonNet);
}