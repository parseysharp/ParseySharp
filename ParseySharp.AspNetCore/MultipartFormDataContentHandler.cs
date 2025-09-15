namespace ParseySharp.AspNetCore;

internal sealed class MultipartFormDataContentHandler : IContentHandler
{
  private static bool IsMultipart(string? ct)
    => !string.IsNullOrWhiteSpace(ct) && ct.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase);

  public bool CanHandle(HttpRequest request) => IsMultipart(request.ContentType);

  public async Task<Either<string, object>> ReadAsync(HttpRequest request, CancellationToken ct)
  {
    try
    {
      var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
      var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

      // Text fields: normalize empty string to null; handle multi-values
      foreach (var kv in form)
      {
        if (kv.Value.Count == 0) { dict[kv.Key] = null; continue; }
        if (kv.Value.Count == 1)
        {
          var v = kv.Value[0];
          dict[kv.Key] = v is null ? null : v.Length == 0 ? null : v;
          continue;
        }
        dict[kv.Key] = kv.Value.Select(v => v is null ? null : v.Length == 0 ? null : (object)v).ToArray();
      }

      // Files: one FilePart per file; group by field name, return single or array
      if (form.Files.Count > 0)
      {
        var groups = form.Files.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
          var parts = g.Select(f => new FilePart(
            Name: f.Name,
            FileName: f.FileName,
            ContentType: f.ContentType ?? string.Empty,
            Length: f.Length,
            OpenRead: () => f.OpenReadStream()
          )).ToArray();

          if (parts.Length == 1)
            dict[g.Key] = parts[0];
          else
            dict[g.Key] = parts;
        }
      }

      return Right<string, object>(dict);
    }
    catch (Exception ex)
    {
      return Left<string, object>($"Invalid multipart form-data: {ex.Message}");
    }
  }

  public Validation<Seq<ParsePathErr>, A> Run<A>(Parse<A> parser, object carrier)
  {
    return parser.ParseObject()(carrier);
  }

  public IEnumerable<string> SupportedContentTypes => new[] { "multipart/form-data" };
}
