using YamlDotNet.RepresentationModel;

namespace ParseySharp.YamlDotNet;

public static class ParsePathNavYaml
{
  public static readonly ParsePathNav<YamlNode> Yaml =
    new(
      Prop: (yn, name) =>
        yn is YamlMappingNode map
          ? (map.Children.TryGetValue(new YamlScalarNode(name), out var child)
              ? Right<object, Option<YamlNode>>(Optional(child))
              : Right<object, Option<YamlNode>>(None))
          : Left<object, Option<YamlNode>>(yn),

      Index: (yn, i) =>
        yn is YamlSequenceNode seq && i >= 0
          ? (i < seq.Children.Count
              ? Right<object, Option<YamlNode>>(Optional(seq.Children[i]))
              : Right<object, Option<YamlNode>>(None))
          : Left<object, Option<YamlNode>>(yn),

      Unbox: yn => yn switch
      {
        YamlScalarNode s => UnboxScalar(s),
        YamlSequenceNode or YamlMappingNode => Right<object, Unknown<object>>(Unknown.New<object>(yn)),
        _ => Left<object, Unknown<object>>(yn)
      }
    );

  static Either<object, Unknown<object>> UnboxScalar(YamlScalarNode s)
  {
    // Null in YAML: empty, "null", "~"
    var val = s.Value;
    if (val is null || val.Length == 0 || string.Equals(val, "null", StringComparison.OrdinalIgnoreCase) || val == "~")
      return Right<object, Unknown<object>>(Unknown.UnsafeFromOption<object>(None));

    // Booleans
    if (bool.TryParse(val, out var b))
      return Right<object, Unknown<object>>(Unknown.New<object>(b));

    // Integers (prefer int, fallback to long)
    if (long.TryParse(val, out var l))
    {
      if (l <= int.MaxValue && l >= int.MinValue)
        return Right<object, Unknown<object>>(Unknown.New<object>((int)l));
      return Right<object, Unknown<object>>(Unknown.New<object>(l));
    }

    // Floating point
    if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
      return Right<object, Unknown<object>>(Unknown.New<object>(d));

    // Fallback string
    return Right<object, Unknown<object>>(Unknown.New<object>(val));
  }
}
