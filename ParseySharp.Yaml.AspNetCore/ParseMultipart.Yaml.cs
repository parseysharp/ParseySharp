using YamlDotNet.RepresentationModel;

namespace ParseySharp.AspNetCore;

public static class ParseMultipartYaml
{
  public static Parse<T> YamlAt<T>(string name, Parse<T> parser)
    => ParseMultipart.FileAt(name).Bind(fp => YamlAt(fp, parser)).As();

  public static Parse<T> YamlAt<T>(FilePart fp, Parse<T> parser)
    => Parse.Pure(fp).As().Filter(fp =>
    {
      try
      {
        using var s = fp.OpenRead();
        using var sr = new StreamReader(s);
        var text = sr.ReadToEnd();
        if (string.IsNullOrWhiteSpace(text))
          return Fail<Seq<ParsePathErr>, T>([
            new ParsePathErr("Empty YAML", typeof(T).Name, Some((object)fp.FileName), [])
          ]);

        var ystream = new YamlStream();
        using var tr = new StringReader(text);
        ystream.Load(tr);
        if (ystream.Documents.Count == 0)
          return Fail<Seq<ParsePathErr>, T>([
            new ParsePathErr("Empty YAML document", typeof(T).Name, Some((object)fp.FileName), [])
          ]);

        var root = ystream.Documents[0].RootNode;
        return parser.ParseYamlNode()(root);
      }
      catch (Exception ex)
      {
        return Fail<Seq<ParsePathErr>, T>([
          new ParsePathErr($"Invalid YAML: {ex.Message}", typeof(T).Name, Some((object)fp.FileName), [])
        ]);
      }
    });
}
