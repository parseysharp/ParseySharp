using Avro;
using Avro.Generic;

namespace ParseySharp.Avro;

public static class ParsePathNavAvro
{
  // Navigator over Avro Generic values carried as 'object'
  public static readonly ParsePathNav<object> Avro =
    ParsePathNav<object>.Create(
      Prop: (node, name) =>
        node switch
        {
          GenericRecord rec =>
            (rec.Schema is RecordSchema rs && rs.Fields.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal)))
              ? Optional(rec[name]).Match(
                  Some: c => NavStep.Value<object>(c),
                  None: () => NavStep.Null<object>())
              : NavStep.Absent<object>(),
          _ => NavStep.NotApplicable(node)
        },

      Index: (node, i) =>
        node switch
        {
          System.Collections.IList list when i >= 0 =>
            (i < list.Count)
              ? Optional(list[i]).Match(
                  Some: c => NavStep.Value<object>(c),
                  None: () => NavStep.Null<object>())
              : NavStep.Absent<object>(),
          IEnumerable<object?> seq when node is not string && i >= 0 =>
            ((Func<Seq<object?>>)(() => Seq(seq)))
              .Try<object, Seq<object?>>(_ => node)
              .Match(
                Left: _ => NavStep.NotApplicable(node),
                Right: xs => (i < xs.Count)
                  ? Optional(xs[i]).Match(
                      Some: c => NavStep.Value<object>(c),
                      None: () => NavStep.Null<object>())
                  : NavStep.Absent<object>()
              ),
          _ => NavStep.NotApplicable(node)
        },

      Unbox: node => node switch
      {
        null => UnboxStep.Null(),
        string s => UnboxStep.Value(s),
        bool b => UnboxStep.Value(b),
        int i => UnboxStep.Value(i),
        long l => UnboxStep.Value(l),
        float f => UnboxStep.Value((double)f),
        double d => UnboxStep.Value(d),
        // Handle Avro decimal logical type when decoded as System.Decimal
        decimal m => ((Func<UnboxStep>)(() =>
        {
          // If integral, coerce to the narrowest integer type that fits; else surface as double
          if (decimal.Truncate(m) == m)
          {
            try
            {
              if (m <= int.MaxValue && m >= int.MinValue)
                return UnboxStep.Value((int)m);
            }
            catch {}
            try
            {
              if (m <= long.MaxValue && m >= long.MinValue)
                return UnboxStep.Value((long)m);
            }
            catch {}
          }
          return UnboxStep.Value((double)m);
        }))
        .Try<UnboxStep, UnboxStep>(_ => UnboxStep.NotApplicable(node))
        .Match(
          Left: na => na,
          Right: v => v
        ),
        // Handle Avro.Util.AvroDecimal
        AvroDecimal am => ((Func<UnboxStep>)(() =>
        {
          var m = AvroDecimal.ToDecimal(am);
          if (decimal.Truncate(m) == m)
          {
            try { if (m <= int.MaxValue && m >= int.MinValue) return UnboxStep.Value((int)m); } catch {}
            try { if (m <= long.MaxValue && m >= long.MinValue) return UnboxStep.Value((long)m); } catch {}
          }
          return UnboxStep.Value((double)m);
        }))
        .Try<UnboxStep, UnboxStep>(_ => UnboxStep.NotApplicable(node))
        .Match(
          Left: na => na,
          Right: v => v
        ),
        byte[] bytes => UnboxStep.Value(bytes),
        // Expose sequences (Avro arrays) and records as nodes for further traversal
        System.Collections.IEnumerable when node is not string => UnboxStep.Value(node),
        GenericRecord => UnboxStep.Value(node),
        // Avro Enum and Fixed: pass through as nodes (or map Fixed to bytes if desired later)
        GenericEnum => UnboxStep.Value(node),
        GenericFixed gf => UnboxStep.Value(gf.Value),
        _ => UnboxStep.NotApplicable(node)
      },
      CloneNode: x => x
    );
}
