using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace ParseySharp.DynamoDb;

public static class ParsePathNavDynamoDb
{
  static bool IsDynamoNull(AttributeValue? av) => av is null || av.NULL == true;

  public static readonly ParsePathNav<AttributeValue> DynamoDb =
    ParsePathNav<AttributeValue>.Create(
      Prop: (av, name) =>
        av is null
          ? NavStep.NotApplicable<AttributeValue>(av!)
          : av.M is { } map
            ? (map.TryGetValue(name, out var child)
                ? Optional(child).Filter(c => !IsDynamoNull(c)).Match(
                    Some: c => NavStep.Value<AttributeValue>(c),
                    None: () => NavStep.Null<AttributeValue>())
                : NavStep.Absent<AttributeValue>())
            : NavStep.NotApplicable(av),

      Index: (av, i) =>
        av is null
          ? NavStep.NotApplicable<AttributeValue>(av!)
          : i < 0
            ? NavStep.NotApplicable(av)
            : av.L is { } list
              ? (i < list.Count
                  ? Optional(list[i]).Filter(c => !IsDynamoNull(c)).Match(
                      Some: c => NavStep.Value<AttributeValue>(c),
                      None: () => NavStep.Null<AttributeValue>())
                  : NavStep.Absent<AttributeValue>())
              : NavStep.NotApplicable(av),

      Unbox: av =>
      {
        if (av is null)
          return UnboxStep.Null();

        // Null
        if (av.NULL == true)
          return UnboxStep.Null();

        // String
        if (av.S != null)
          return UnboxStep.Value(av.S);

        // Number (encoded as string) with narrowing rules consistent with other navigators
        if (av.N != null)
        {
          var s = av.N;
          if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var m))
          {
            if (decimal.Truncate(m) == m)
            {
              // Integral: prefer int, then long, else preserve as string
              try
              {
                if (m <= int.MaxValue && m >= int.MinValue)
                  return UnboxStep.Value((int)m);
              }
              catch { /* fall through */ }
              try
              {
                if (m <= long.MaxValue && m >= long.MinValue)
                  return UnboxStep.Value((long)m);
              }
              catch { /* fall through */ }
              // Very large integer beyond Int64: preserve as string to avoid loss
              return UnboxStep.Value(s);
            }
            // Non-integral: surface as double when representable, else preserve as string
            try
            {
              var d = Convert.ToDouble(m, CultureInfo.InvariantCulture);
              if (double.IsFinite(d))
                return UnboxStep.Value(d);
            }
            catch { /* fall back to string below */ }
            return UnboxStep.Value(s);
          }
          // Not a valid decimal: keep raw string
          return UnboxStep.Value(s);
        }

        // Binary
        if (av.B != null)
        {
          var bytes = av.B.ToArray();
          return UnboxStep.Value(bytes);
        }

        // Binary set (only if non-empty)
        if (av.BS != null && av.BS.Count > 0)
        {
          var list = av.BS.Select(ms => ms.ToArray()).ToList();
          return UnboxStep.Value(list);
        }

        // String set (only if non-empty)
        if (av.SS != null && av.SS.Count > 0)
          return UnboxStep.Value(av.SS);

        // Number set (strings) (only if non-empty) -> apply same numeric policy element-wise; if any element overflows/loses precision, keep as strings for that element
        if (av.NS != null && av.NS.Count > 0)
        {
          var projected = new List<object>(av.NS.Count);
          foreach (var ns in av.NS)
          {
            if (decimal.TryParse(ns, NumberStyles.Number, CultureInfo.InvariantCulture, out var m2))
            {
              if (decimal.Truncate(m2) == m2)
              {
                if (m2 <= int.MaxValue && m2 >= int.MinValue) { projected.Add((int)m2); continue; }
                if (m2 <= long.MaxValue && m2 >= long.MinValue) { projected.Add((long)m2); continue; }
                projected.Add(ns); // very large integer
              }
              else
              {
                try
                {
                  var d2 = Convert.ToDouble(m2, CultureInfo.InvariantCulture);
                  if (double.IsFinite(d2)) { projected.Add(d2); continue; }
                  projected.Add(ns);
                }
                catch { projected.Add(ns); }
              }
            }
            else
            {
              projected.Add(ns);
            }
          }
          return UnboxStep.Value(projected);
        }

        // List: expose items so Seq can iterate (empty lists are valid in DynamoDB)
        if (av.L != null)
          return UnboxStep.Value(av.L);

        // Map: remain as node for key-based traversal (empty maps are valid in DynamoDB)
        if (av.M != null)
          return UnboxStep.Value(av);

        // Bool false (placed after other checks to avoid misclassification)
        if (av.BOOL.HasValue)
          return UnboxStep.Value(av.BOOL.Value);

        // Fallback: treat as Null
        return UnboxStep.Null();
      },
      CloneNode: x => x
    );
}
