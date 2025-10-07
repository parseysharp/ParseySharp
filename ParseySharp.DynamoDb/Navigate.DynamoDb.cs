using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace ParseySharp.DynamoDb;

public static class ParsePathNavDynamoDb
{
  public static readonly ParsePathNav<AttributeValue> DynamoDb =
    ParsePathNav<AttributeValue>.Create(
      Prop: (av, name) =>
        av is null
          ? Right<Unknown<AttributeValue>, Option<AttributeValue>>(None)
          : av.M is { } map
            ? (map.TryGetValue(name, out var child)
                ? Right<Unknown<AttributeValue>, Option<AttributeValue>>(Optional(child))
                : Right<Unknown<AttributeValue>, Option<AttributeValue>>(None))
            : Left<Unknown<AttributeValue>, Option<AttributeValue>>(Unknown.New(av)),

      Index: (av, i) =>
        av is null
          ? Right<Unknown<AttributeValue>, Option<AttributeValue>>(None)
          : i < 0
            ? Left<Unknown<AttributeValue>, Option<AttributeValue>>(Unknown.New(av))
            : av.L is { } list
              ? (i < list.Count
                  ? Right<Unknown<AttributeValue>, Option<AttributeValue>>(Optional(list[i]))
                  : Right<Unknown<AttributeValue>, Option<AttributeValue>>(None))
              : Left<Unknown<AttributeValue>, Option<AttributeValue>>(Unknown.New(av)),

      Unbox: av =>
      {
        if (av is null)
          return Right<Unknown<AttributeValue>, Unknown<object>>(new Unknown<object>.None());

        // Null
        if (av.NULL == true)
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.UnsafeFromOption<object>(None));

        // String
        if (av.S != null)
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(av.S));

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
                  return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>((int)m));
              }
              catch { /* fall through */ }
              try
              {
                if (m <= long.MaxValue && m >= long.MinValue)
                  return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>((long)m));
              }
              catch { /* fall through */ }
              // Very large integer beyond Int64: preserve as string to avoid loss
              return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(s));
            }
            // Non-integral: surface as double when representable, else preserve as string
            try
            {
              var d = Convert.ToDouble(m, CultureInfo.InvariantCulture);
              if (double.IsFinite(d))
                return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(d));
            }
            catch { /* fall back to string below */ }
            return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(s));
          }
          // Not a valid decimal: keep raw string
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(s));
        }

        // Binary
        if (av.B != null)
        {
          var bytes = av.B.ToArray();
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(bytes));
        }

        // Binary set (only if non-empty)
        if (av.BS != null && av.BS.Count > 0)
        {
          var list = av.BS.Select(ms => ms.ToArray()).ToList();
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(list));
        }

        // String set (only if non-empty)
        if (av.SS != null && av.SS.Count > 0)
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(av.SS));

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
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(projected));
        }

        // List: expose items so Seq can iterate
        if (av.L != null)
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(av.L));

        // Map: remain as node for key-based traversal
        if (av.M != null)
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(av));

        // Bool (placed after other checks to avoid misclassification)
        if (av.BOOL == true)
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(true));
        if (av.BOOL == false)
          return Right<Unknown<AttributeValue>, Unknown<object>>(Unknown.New<object>(false));

        // Fallback: treat as None
        return Right<Unknown<AttributeValue>, Unknown<object>>(new Unknown<object>.None());
      },
      CloneNode: x => x
    );
}
