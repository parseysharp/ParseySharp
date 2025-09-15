namespace ParseySharp.AspNetCore;

// Handler for application/x-www-form-urlencoded
internal sealed class FormUrlEncodedContentHandler : IContentHandler
{
  public bool CanHandle(HttpRequest request)
  {
    var ct = request.ContentType;
    return !string.IsNullOrWhiteSpace(ct)
      && ct.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
  }

  public async Task<Either<string, object>> ReadAsync(HttpRequest request, CancellationToken ct)
  {
    try
    {
      var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
      var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
      foreach (var kv in form)
      {
        if (kv.Value.Count == 0) { dict[kv.Key] = null; continue; }
        if (kv.Value.Count == 1)
        {
          var v = kv.Value[0];
          dict[kv.Key] = v is null ? null : v.Length == 0 ? null : v;
          continue;
        }
        // For multi-value keys, normalize empty strings to null per element
        dict[kv.Key] = kv.Value.Select(v => v is null ? null : v.Length == 0 ? null : v).ToArray();
      }
      return Right<string, object>(dict);
    }
    catch (Exception ex)
    {
      return Left<string, object>($"Invalid form data: {ex.Message}");
    }
  }

  public Validation<Seq<ParsePathErr>, A> Run<A>(Parse<A> parser, object carrier)
  {
    return parser.ParseObject()(carrier);
  }

  public IEnumerable<string> SupportedContentTypes => new[] { "application/x-www-form-urlencoded" };
}
