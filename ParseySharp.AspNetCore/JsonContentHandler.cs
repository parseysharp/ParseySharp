using System.Text.Json;

namespace ParseySharp.AspNetCore;

internal sealed class JsonContentHandler : IContentHandler
{
  private static bool IsJson(string? ct)
    => !string.IsNullOrWhiteSpace(ct) && (ct!.Contains("application/json", StringComparison.OrdinalIgnoreCase)
       || ct.Contains("+json", StringComparison.OrdinalIgnoreCase));

  public bool CanHandle(HttpRequest request) => IsJson(request.ContentType);

  public async Task<Either<string, object>> ReadAsync(HttpRequest request, CancellationToken ct)
  {
    try
    {
      // Allow empty bodies to be treated as null
      if (request.ContentLength is 0)
        return Right<string, object>(JsonDocument.Parse("null").RootElement);

      using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct).ConfigureAwait(false);
      // NOTE: we must copy the root element out because disposing the document invalidates it.
      var rootClone = JsonDocument.Parse(doc.RootElement.GetRawText()).RootElement;
      return Right<string, object>(rootClone);
    }
    catch (Exception ex)
    {
      return Left<string, object>($"Invalid JSON: {ex.Message}");
    }
  }

  public Validation<Seq<ParsePathErr>, A> Run<A>(Parse<A> parser, object carrier)
  {
    if (carrier is JsonElement elem)
      return parser.ParseJson()(elem);

    return Fail<Seq<ParsePathErr>, A>([ new ParsePathErr("Unsupported JSON carrier", typeof(A).Name, None, []) ]);
  }

  public IEnumerable<string> SupportedContentTypes => ["application/json"];
}
