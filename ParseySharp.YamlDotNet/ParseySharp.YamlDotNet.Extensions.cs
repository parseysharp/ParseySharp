using YamlDotNet.RepresentationModel;

namespace ParseySharp;

public static class ParseYamlExtensions
{
  public static Func<YamlNode, Validation<Seq<ParsePathErr>, A>> ParseYamlNode<A>(this Parse<A> parser) =>
    ParseExtensions.RunWithNav(parser, ParseySharp.YamlDotNet.ParsePathNavYaml.Yaml);
}
