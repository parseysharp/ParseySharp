using System.Xml.Linq;

namespace ParseySharp.AspNetCore;

internal sealed class XmlContentHandler : IContentHandler
{
  private static bool IsXml(string? ct)
    => !string.IsNullOrWhiteSpace(ct) && (ct!.Contains("application/xml", StringComparison.OrdinalIgnoreCase)
       || ct.Contains("text/xml", StringComparison.OrdinalIgnoreCase)
       || ct.Contains("+xml", StringComparison.OrdinalIgnoreCase));

  public bool CanHandle(HttpRequest request) => IsXml(request.ContentType);

  public async Task<Either<string, object>> ReadAsync(HttpRequest request, CancellationToken ct)
  {
    try
    {
      if (request.ContentLength is 0)
        return Left<string, object>("Empty XML body");

      using var reader = new StreamReader(request.Body);
      var content = await reader.ReadToEndAsync().ConfigureAwait(false);
      if (string.IsNullOrWhiteSpace(content))
        return Left<string, object>("Empty XML body");

      var x = XElement.Parse(content);
      return Right<string, object>(x);
    }
    catch (Exception ex)
    {
      return Left<string, object>($"Invalid XML: {ex.Message}");
    }
  }

  public Validation<Seq<ParsePathErr>, A> Run<A>(Parse<A> parser, object carrier)
  {
    if (carrier is XElement xe)
      return parser.ParseXml()(xe);

    return Fail<Seq<ParsePathErr>, A>([ new ParsePathErr("Unsupported XML carrier", typeof(A).Name, None, []) ]);
  }

  public IEnumerable<string> SupportedContentTypes => new[] { "application/xml", "text/xml" };
}
