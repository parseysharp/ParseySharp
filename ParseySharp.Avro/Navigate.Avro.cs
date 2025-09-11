using Avro;
using Avro.Generic;

namespace ParseySharp.Avro;

public static class ParsePathNavAvro
{
  // Navigator over Avro Generic values carried as 'object'
  public static readonly ParsePathNav<object> Avro =
    new(
      Prop: (node, name) =>
        node switch
        {
          GenericRecord rec =>
            (rec.Schema is RecordSchema rs && rs.Fields.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal)))
              ? Right<object, Option<object>>(Optional(rec[name]))
              : Right<object, Option<object>>(None),
          _ => Left<object, Option<object>>(node)
        },

      Index: (node, i) =>
        node switch
        {
          System.Collections.IList list when i >= 0 =>
            (i < list.Count)
              ? Right<object, Option<object>>(Optional(list[i]))
              : Right<object, Option<object>>(None),
          IEnumerable<object?> seq when node is not string && i >= 0 =>
            ((Func<Seq<object?>>)(() => Seq(seq)))
              .Try<object, Seq<object?>>(_ => node)
              .Match(
                Left: _ => Left<object, Option<object>>(node),
                Right: xs => (i < xs.Count)
                  ? Right<object, Option<object>>(Optional(xs[i]))
                  : Right<object, Option<object>>(None)
              ),
          _ => Left<object, Option<object>>(node)
        },

      Unbox: node => node switch
      {
        null => Right<object, Unknown<object>>(new Unknown<object>.None()),
        string s => Right<object, Unknown<object>>(Unknown.New<object>(s)),
        bool b => Right<object, Unknown<object>>(Unknown.New<object>(b)),
        int i => Right<object, Unknown<object>>(Unknown.New<object>(i)),
        long l => Right<object, Unknown<object>>(Unknown.New<object>(l)),
        float f => Right<object, Unknown<object>>(Unknown.New<object>((double)f)),
        double d => Right<object, Unknown<object>>(Unknown.New<object>(d)),
        // Handle Avro decimal logical type when decoded as System.Decimal
        decimal m => ((Func<Unknown<object>>)(() =>
        {
          // If integral, coerce to the narrowest integer type that fits; else surface as double
          if (decimal.Truncate(m) == m)
          {
            try
            {
              if (m <= int.MaxValue && m >= int.MinValue)
                return Unknown.New<object>((int)m);
            }
            catch {}
            try
            {
              if (m <= long.MaxValue && m >= long.MinValue)
                return Unknown.New<object>((long)m);
            }
            catch {}
          }
          return Unknown.New<object>((double)m);
        }))
        .Try<object, Unknown<object>>(_ => node)
        .Match(
          Left: _ => Left<object, Unknown<object>>(node),
          Right: v => Right<object, Unknown<object>>(v)
        ),
        // Handle Avro.Util.AvroDecimal
        AvroDecimal am => ((Func<Unknown<object>>)(() =>
        {
          var m = AvroDecimal.ToDecimal(am);
          if (decimal.Truncate(m) == m)
          {
            try { if (m <= int.MaxValue && m >= int.MinValue) return Unknown.New<object>((int)m); } catch {}
            try { if (m <= long.MaxValue && m >= long.MinValue) return Unknown.New<object>((long)m); } catch {}
          }
          return Unknown.New<object>((double)m);
        }))
        .Try<object, Unknown<object>>(_ => node)
        .Match(
          Left: _ => Left<object, Unknown<object>>(node),
          Right: v => Right<object, Unknown<object>>(v)
        ),
        byte[] bytes => Right<object, Unknown<object>>(Unknown.New<object>(bytes)),
        // Expose sequences (Avro arrays) and records as nodes for further traversal
        System.Collections.IEnumerable when node is not string => Right<object, Unknown<object>>(Unknown.New<object>(node)),
        GenericRecord => Right<object, Unknown<object>>(Unknown.New<object>(node)),
        // Avro Enum and Fixed: pass through as nodes (or map Fixed to bytes if desired later)
        GenericEnum => Right<object, Unknown<object>>(Unknown.New<object>(node)),
        GenericFixed gf => Right<object, Unknown<object>>(Unknown.New<object>(gf.Value)),
        _ => Left<object, Unknown<object>>(node)
      }
    );
}
