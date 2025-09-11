namespace ParseySharp.AspNetCore;

// Handler for GET requests that parse the query string into a simple dictionary<object>
// Keys are query parameter names; values are strings (first value if repeated) or string[] when multiple values are present.
// This aligns with typical GET semantics (no body). Parsers can choose to coerce strings to numbers, booleans, etc.
internal sealed class QueryStringContentHandler : IContentHandler
{
  public bool CanHandle(HttpRequest request)
    => HttpMethods.IsGet(request.Method);

  public Task<Either<string, object>> ReadAsync(HttpRequest request, CancellationToken ct)
  {
    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    foreach (var (key, values) in request.Query)
    {
      if (values.Count == 0) { dict[key] = null; continue; }
      if (values.Count == 1)
      {
        var v = values[0];
        dict[key] = v is null ? null : v.Length == 0 ? null : v;
        continue;
      }
      // For multi-value keys, normalize empty strings to null per element
      dict[key] = values.Select(v => v is null ? null : v.Length == 0 ? null : (object)v).ToArray();
    }
    // Use the object-based parser path (ParseObject) downstream
    return Task.FromResult(Right<string, object>(dict));
  }

  public Validation<Seq<ParsePathErr>, A> Run<A>(Parse<A> parser, object carrier)
  {
    return parser.ParseObject()(carrier);
  }

  // No content-types; this is method-based
  public IEnumerable<string> SupportedContentTypes => [];
}
