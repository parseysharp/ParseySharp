using Microsoft.AspNetCore.Mvc;

namespace ParseySharp.AspNetCore;

public static class MvcParsedExtensions
{
  // Async handler overload
  public static async Task<IActionResult> ParsedAsync<A>(
    this ControllerBase controller,
    Parse<A> parser,
    Func<A, Task<IActionResult>> onSuccess,
    CancellationToken ct = default)
  {
    var http = controller.HttpContext;
    var binder = http.RequestServices.GetRequiredService<IParseBinder>();
    var problems = http.RequestServices.GetRequiredService<IProblemMapper>();

    var parsed = await binder.ParseAsync(http.Request, parser, ct).ConfigureAwait(false);
    return await parsed.Match(
      Fail: errs => Task.FromResult<IActionResult>(controller.BadRequest(problems.ToProblem(errs))),
      Succ: onSuccess
    ).ConfigureAwait(false);
  }

  // Sync handler overload (still returns Task<IActionResult> to fit MVC best practices)
  public static Task<IActionResult> ParsedAsync<A>(
    this ControllerBase controller,
    Parse<A> parser,
    Func<A, IActionResult> onSuccess,
    CancellationToken ct = default)
    => controller.ParsedAsync(parser, a => Task.FromResult(onSuccess(a)), ct);
}
