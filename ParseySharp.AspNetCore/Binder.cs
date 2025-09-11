using Microsoft.AspNetCore.Mvc;

namespace ParseySharp.AspNetCore;

// Pure binder: chooses a handler and runs the parser; only side-effect is reading body
internal sealed class ParseBinder : IParseBinder
{
  private readonly IReadOnlyList<IContentHandler> _handlers;
  private readonly ParseySharpOptions _options;

  public ParseBinder(IEnumerable<IContentHandler> handlers, ParseySharpOptions options)
  {
    _handlers = handlers.ToList();
    _options = options;
  }

  public async Task<Validation<Seq<ParsePathErr>, A>> ParseAsync<A>(HttpRequest request, Parse<A> parser, CancellationToken ct)
  {
    // Select a content handler and parse via its carrier (format-agnostic)
    var handler = _handlers.FirstOrDefault(h => h.CanHandle(request))
      ?? _handlers.FirstOrDefault(h => string.Equals(request.ContentType, _options.DefaultContentType, StringComparison.OrdinalIgnoreCase))
      ?? _handlers.FirstOrDefault();

    if (handler is null)
      return Fail<Seq<ParsePathErr>, A>([ new ParsePathErr("No content handler registered", typeof(A).Name, None, []) ]);

    var carrierOrErr = await handler.ReadAsync(request, ct).ConfigureAwait(false);
    return carrierOrErr.Match(
      Left: err => Fail<Seq<ParsePathErr>, A>([ new ParsePathErr(err, typeof(A).Name, None, []) ]),
      Right: carrier => handler.Run(parser, carrier)
    );
  }
}

internal sealed class DefaultProblemMapper : IProblemMapper
{
  public ProblemDetails ToProblem(Seq<ParsePathErr> errors, int? statusCode = null)
  {
    var pd = new ProblemDetails
    {
      Title = "Invalid request",
      Status = statusCode ?? StatusCodes.Status400BadRequest,
      Detail = "One or more validation errors occurred while parsing the request body."
    };

    pd.Extensions["errors"] = errors.Map(e => new {
      message = e.Message,
      expected = e.Expected,
      actual = e.Actual,
      path = e.Path.ToArray()
    }).ToArray();

    return pd;
  }
}
