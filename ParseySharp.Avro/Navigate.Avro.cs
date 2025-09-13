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
              ? Right<Unknown<object>, Option<object>>(Optional(rec[name]))
              : Right<Unknown<object>, Option<object>>(None),
          _ => Left<Unknown<object>, Option<object>>(Unknown.New(node))
        },

      Index: (node, i) =>
        node switch
        {
          System.Collections.IList list when i >= 0 =>
            (i < list.Count)
              ? Right<Unknown<object>, Option<object>>(Optional(list[i]))
              : Right<Unknown<object>, Option<object>>(None),
          IEnumerable<object?> seq when node is not string && i >= 0 =>
            ((Func<Seq<object?>>)(() => Seq(seq)))
              .Try<object, Seq<object?>>(_ => node)
              .Match(
                Left: _ => Left<Unknown<object>, Option<object>>(Unknown.New(node)),
                Right: xs => (i < xs.Count)
                  ? Right<Unknown<object>, Option<object>>(Optional(xs[i]))
                  : Right<Unknown<object>, Option<object>>(None)
              ),
          _ => Left<Unknown<object>, Option<object>>(Unknown.New(node))
        },

      Unbox: node => node switch
      {
        null => Right<Unknown<object>, Unknown<object>>(new Unknown<object>.None()),
        string s => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(s)),
        bool b => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(b)),
        int i => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(i)),
        long l => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(l)),
        float f => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>((double)f)),
        double d => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(d)),
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
          Left: _ => Left<Unknown<object>, Unknown<object>>(Unknown.New(node)),
          Right: v => Right<Unknown<object>, Unknown<object>>(v)
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
          Left: _ => Left<Unknown<object>, Unknown<object>>(Unknown.New(node)),
          Right: v => Right<Unknown<object>, Unknown<object>>(v)
        ),
        byte[] bytes => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(bytes)),
        // Expose sequences (Avro arrays) and records as nodes for further traversal
        System.Collections.IEnumerable when node is not string => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(node)),
        GenericRecord => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(node)),
        // Avro Enum and Fixed: pass through as nodes (or map Fixed to bytes if desired later)
        GenericEnum => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(node)),
        GenericFixed gf => Right<Unknown<object>, Unknown<object>>(Unknown.New<object>(gf.Value)),
        _ => Left<Unknown<object>, Unknown<object>>(Unknown.New(node))
      },
      CloneNode: x => x
    );
}
