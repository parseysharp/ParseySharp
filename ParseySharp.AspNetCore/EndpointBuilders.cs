using Microsoft.AspNetCore.Routing;

namespace ParseySharp.AspNetCore;

public static class EndpointBuilders
{
  // Strict builder: parser is a required argument at route-definition time

  // Overload that allows async handler returning IResult
  public static RouteHandlerBuilder MapParsedPost<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<T, Task<IResult>> handler)
  {
    return app.MapPost(pattern, async (HttpContext ctx, IParseBinder binder, IProblemMapper problems) =>
    {
      var res = await binder.ParseAsync(ctx.Request, parser, ctx.RequestAborted);
      return await res.Match(
        Fail: errs => Task.FromResult<IResult>(Results.Problem(problems.ToProblem(errs))),
        Succ: val => handler(val)
      );
    });
  }

  

  public static RouteHandlerBuilder MapParsedGet<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<T, Task<IResult>> handler)
  {
    return app.MapGet(pattern, async (HttpContext ctx, IParseBinder binder, IProblemMapper problems) =>
    {
      // Let the binder select QueryStringContentHandler (registered by default) for GET
      var res = await binder.ParseAsync(ctx.Request, parser, ctx.RequestAborted);
      return await res.Match(
        Fail: errs => Task.FromResult<IResult>(Results.Problem(problems.ToProblem(errs))),
        Succ: val => handler(val)
      );
    });
  }

  

  public static RouteHandlerBuilder MapParsedPut<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<T, Task<IResult>> handler)
  {
    return app.MapPut(pattern, async (HttpContext ctx, IParseBinder binder, IProblemMapper problems) =>
    {
      var res = await binder.ParseAsync(ctx.Request, parser, ctx.RequestAborted);
      return await res.Match(
        Fail: errs => Task.FromResult<IResult>(Results.Problem(problems.ToProblem(errs))),
        Succ: val => handler(val)
      );
    });
  }

  

  public static RouteHandlerBuilder MapParsedDelete<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<T, Task<IResult>> handler)
  {
    return app.MapDelete(pattern, async (HttpContext ctx, IParseBinder binder, IProblemMapper problems) =>
    {
      var res = await binder.ParseAsync(ctx.Request, parser, ctx.RequestAborted);
      return await res.Match(
        Fail: errs => Task.FromResult<IResult>(Results.Problem(problems.ToProblem(errs))),
        Succ: val => handler(val)
      );
    });
  }
}
