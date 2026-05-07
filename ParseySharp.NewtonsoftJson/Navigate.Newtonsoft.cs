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
              ? Optional(child).Filter(c => c.Type != JTokenType.Null).Match(
                  Some: c => NavStep.Value<JToken>(c),
                  None: () => NavStep.Null<JToken>())
              : NavStep.Absent<JToken>()),
          _ => NavStep.NotApplicable(jt)
        },

      Index: (jt, i) =>
        jt switch
        {
          JArray arr when i >= 0 =>
            (i < arr.Count
              ? Optional(arr[i]).Filter(c => c.Type != JTokenType.Null).Match(
                  Some: c => NavStep.Value<JToken>(c),
                  None: () => NavStep.Null<JToken>())
              : NavStep.Absent<JToken>()),
          _ => NavStep.NotApplicable(jt)
        },

      Unbox: jt => jt switch
      {
        null => UnboxStep.Null(),

        // Keep arrays/objects as-is (so Seq and nested parsing can work)
        JObject or JArray => UnboxStep.Value(jt),

        // Null token => Null
        JValue v when v.Type == JTokenType.Null
          => UnboxStep.Null(),

        // Strings
        JValue v when v.Type == JTokenType.String
          => Optional(v.Value<string>()).Match(
              Some: s => UnboxStep.Value(s),
              None: () => UnboxStep.Null()),

        // Booleans
        JValue v when v.Type == JTokenType.Boolean
          => UnboxStep.Value(v.Value<bool>()),

        // Integers (prefer Int32, then Int64); guard against provider overflow by Try-wrapping
        JValue v when v.Type == JTokenType.Integer
          => ((Func<UnboxStep>)(() =>
            {
              var l = v.Value<long>();
              return (l <= int.MaxValue && l >= int.MinValue)
                ? UnboxStep.Value((int)l)
                : UnboxStep.Value(l);
            }))
            .Try<UnboxStep, UnboxStep>(_ => UnboxStep.Value(v.ToString()))
            .Match(
              Left: fallback => fallback,
              Right: r => r
            ),

        // Floats/Decimals (use double)
        JValue v when v.Type == JTokenType.Float
          => UnboxStep.Value(v.Value<double>()),

        // Otherwise fall back to string representation (or treat as None if you prefer)
        JValue v
          => UnboxStep.Value(v.ToString()),

        _ => UnboxStep.NotApplicable(jt)
      },
      CloneNode: x => x
    );
}

public static class ParseNewtonsoftJsonExtensions
{
  public static Func<JToken, Validation<Seq<ParsePathErr>, A>> ParseNewtonsoftJson<A>(this Parse<A> parser) =>
    ParseExtensions.RunWithNav(parser, ParsePathNavNewtonsoft.JsonNet);
}
