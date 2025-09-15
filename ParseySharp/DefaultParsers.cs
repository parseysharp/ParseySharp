using System;
using System.Globalization;

namespace ParseySharp;

// Extension surface: additional strict and flexible parsers with sensible defaults
public partial class Parse
{
  // System.DateTimeOffset: expects ISO-8601 (Roundtrip) string
  public static Parse<System.DateTimeOffset> DateTimeOffset(
    DateTimeStyles styles = DateTimeStyles.RoundtripKind,
    CultureInfo? culture = null)
    => As<string>().Filter(s =>
      System.DateTimeOffset.TryParse(s, culture ?? CultureInfo.InvariantCulture, styles, out var dto)
        ? Right<string, System.DateTimeOffset>(dto)
        : Left<string, System.DateTimeOffset>("Invalid timestamp (expected ISO-8601 Roundtrip): " + s)
    ).As();

  // System.DateTimeOffsetFlex: accepts string (Roundtrip) or epoch seconds (long/double)
  public static Parse<System.DateTimeOffset> DateTimeOffsetFlex(
    bool epochIsMilliseconds = false,
    DateTimeStyles styles = DateTimeStyles.RoundtripKind,
    CultureInfo? culture = null)
    => As<object>().Filter(obj =>
    {
      switch (obj)
      {
        case string s:
          return System.DateTimeOffset.TryParse(s, culture ?? CultureInfo.InvariantCulture, styles, out var dto)
            ? Right<string, System.DateTimeOffset>(dto)
            : Left<string, System.DateTimeOffset>("Invalid timestamp (expected ISO-8601 Roundtrip): " + s);
        case int i:
          return epochIsMilliseconds
            ? Right<string, System.DateTimeOffset>(System.DateTimeOffset.FromUnixTimeMilliseconds(i))
            : Right<string, System.DateTimeOffset>(System.DateTimeOffset.FromUnixTimeSeconds(i));
        case long l:
          return epochIsMilliseconds
            ? Right<string, System.DateTimeOffset>(System.DateTimeOffset.FromUnixTimeMilliseconds(l))
            : Right<string, System.DateTimeOffset>(System.DateTimeOffset.FromUnixTimeSeconds(l));
        case double d:
          if (double.IsNaN(d) || double.IsInfinity(d))
            return Left<string, System.DateTimeOffset>("Invalid timestamp: non-finite number");
          var epoch = epochIsMilliseconds ? (long)Math.Round(d) : (long)Math.Round(d);
          return epochIsMilliseconds
            ? Right<string, System.DateTimeOffset>(System.DateTimeOffset.FromUnixTimeMilliseconds(epoch))
            : Right<string, System.DateTimeOffset>(System.DateTimeOffset.FromUnixTimeSeconds(epoch));
        default:
          return Left<string, System.DateTimeOffset>($"Invalid timestamp type: {obj?.GetType().Name ?? "null"}");
      }
    }).As();

  // Decimal: accepts numeric JSON types only (int/long/double/decimal)
  public static Parse<decimal> Decimal(CultureInfo? culture = null)
    => As<object>().Filter(obj =>
    {
      switch (obj)
      {
        case decimal m: return Right<string, decimal>(m);
        case int i: return Right<string, decimal>(i);
        case long l: return Right<string, decimal>(l);
        case double d:
          if (double.IsNaN(d) || double.IsInfinity(d))
            return Left<string, decimal>("Invalid amount: non-finite number");
          try { return Right<string, decimal>(Convert.ToDecimal(d, culture ?? CultureInfo.InvariantCulture)); }
          catch { return Left<string, decimal>("Invalid amount: cannot convert number to decimal"); }
        default:
          return Left<string, decimal>($"Invalid amount type: {obj?.GetType().Name ?? "null"}");
      }
    }).As();

  // DecimalFlex: accepts number OR string (InvariantCulture by default)
  public static Parse<decimal> DecimalFlex(NumberStyles styles = NumberStyles.Number, CultureInfo? culture = null)
    => As<object>().Filter(obj =>
    {
      switch (obj)
      {
        case decimal m: return Right<string, decimal>(m);
        case int i: return Right<string, decimal>(i);
        case long l: return Right<string, decimal>(l);
        case double d:
          if (double.IsNaN(d) || double.IsInfinity(d))
            return Left<string, decimal>("Invalid amount: non-finite number");
          try { return Right<string, decimal>(Convert.ToDecimal(d, culture ?? CultureInfo.InvariantCulture)); }
          catch { return Left<string, decimal>("Invalid amount: cannot convert number to decimal"); }
        case string s:
          return decimal.TryParse(s, styles, culture ?? CultureInfo.InvariantCulture, out var dec)
            ? Right<string, decimal>(dec)
            : Left<string, decimal>($"Invalid amount: {s}");
        default:
          return Left<string, decimal>($"Invalid amount type: {obj?.GetType().Name ?? "null"}");
      }
    }).As();

  public static Parse<int> Int32Flex(NumberStyles styles = NumberStyles.Integer, CultureInfo? culture = null)
    => As<object>().Filter(obj =>
    {
      switch (obj)
      {
        case int i: return Right<string, int>(i);
        case string s:
          return int.TryParse(s, styles, culture ?? CultureInfo.InvariantCulture, out var v)
            ? Right<string, int>(v)
            : Left<string, int>($"Invalid integer: {s}");
        default:
          return Left<string, int>($"Invalid integer type: {obj?.GetType().Name ?? "null"}");
      }
    }).As();

  public static Parse<long> Int64Flex(NumberStyles styles = NumberStyles.Integer, CultureInfo? culture = null)
    => As<object>().Filter(obj =>
    {
      switch (obj)
      {
        case long l: return Right<string, long>(l);
        case int i: return Right<string, long>(i);
        case string s:
          return long.TryParse(s, styles, culture ?? CultureInfo.InvariantCulture, out var v)
            ? Right<string, long>(v)
            : Left<string, long>($"Invalid integer: {s}");
        default:
          return Left<string, long>($"Invalid integer type: {obj?.GetType().Name ?? "null"}");
      }
    }).As();

  // BoolFlex: accepts bool OR "true"/"false" strings (case-insensitive)
  public static Parse<bool> BoolFlex()
    => As<object>().Filter(obj =>
    {
      switch (obj)
      {
        case bool b: return Right<string, bool>(b);
        case string s when string.Equals(s, "true", StringComparison.OrdinalIgnoreCase):
          return Right<string, bool>(true);
        case string s when string.Equals(s, "false", StringComparison.OrdinalIgnoreCase):
          return Right<string, bool>(false);
        default:
          return Left<string, bool>($"Invalid bool: {obj}");
      }
    }).As();

  // System.Guid: expects string, default format "D"
  public static Parse<System.Guid> Guid(string? format = "D")
    => As<string>().Filter(s =>
    {
      if (string.IsNullOrWhiteSpace(format) || string.Equals(format, "D", StringComparison.Ordinal))
        return System.Guid.TryParseExact(s, "D", out var g)
          ? Right<string, System.Guid>(g)
          : Left<string, System.Guid>("Invalid GUID (expected format D): " + s);
      return System.Guid.TryParseExact(s, format!, out var g2)
        ? Right<string, System.Guid>(g2)
        : Left<string, System.Guid>($"Invalid GUID (expected format {format}): {s}");
    }).As();

  // Non-empty string (trims by default)
  public static Parse<string> NonEmptyString(bool trim = true)
    => As<string>().Filter(s =>
    {
      var v = trim ? s?.Trim() : s;
      return string.IsNullOrWhiteSpace(v)
        ? Left<string, string>("String must be non-empty")
        : Right<string, string>(v!);
    }).As();

  // Enum: expects string name; case-insensitive by default
  public static Parse<TEnum> Enum<TEnum>(bool ignoreCase = true)
    where TEnum : struct, Enum
    => As<string>().Filter(s =>
      System.Enum.TryParse<TEnum>(s, ignoreCase, out var e)
        ? Right<string, TEnum>(e)
        : Left<string, TEnum>($"Invalid {typeof(TEnum).Name}: {s}")
    ).As();
}
