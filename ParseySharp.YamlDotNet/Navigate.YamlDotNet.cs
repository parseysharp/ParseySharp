using YamlDotNet.RepresentationModel;

namespace ParseySharp.YamlDotNet;

public static class ParsePathNavYaml
{
  public static readonly ParsePathNav<YamlNode> Yaml =
    ParsePathNav<YamlNode>.Create(
      Prop: (yn, name) =>
        yn is YamlMappingNode map
          ? (map.Children.TryGetValue(new YamlScalarNode(name), out var child)
              ? Optional(child).Filter(c => !IsYamlNull(c)).Match(
                  Some: c => NavStep.Value<YamlNode>(c),
                  None: () => NavStep.Null<YamlNode>())
              : NavStep.Absent<YamlNode>())
          : NavStep.NotApplicable(yn),

      Index: (yn, i) =>
        yn is YamlSequenceNode seq && i >= 0
          ? (i < seq.Children.Count
              ? Optional(seq.Children[i]).Filter(c => !IsYamlNull(c)).Match(
                  Some: c => NavStep.Value<YamlNode>(c),
                  None: () => NavStep.Null<YamlNode>())
              : NavStep.Absent<YamlNode>())
          : NavStep.NotApplicable(yn),

      Unbox: yn => yn switch
      {
        YamlScalarNode s => UnboxScalar(s),
        YamlSequenceNode or YamlMappingNode => UnboxStep.Value(yn),
        _ => UnboxStep.NotApplicable(yn)
      },
      CloneNode: x => x
    );

  static UnboxStep UnboxScalar(YamlScalarNode s)
  {
    var val = s.Value;
    if (IsYamlNull(s))
      return UnboxStep.Null();

    return UnboxStep.Value(val!);
  }

  static bool IsYamlNull(YamlNode n) =>
    n is YamlScalarNode s &&
    (s.Value is null
      || s.Value.Length == 0
      || string.Equals(s.Value, "null", StringComparison.OrdinalIgnoreCase)
      || s.Value == "~");
}
