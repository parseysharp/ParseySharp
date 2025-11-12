using Microsoft.AspNetCore.Routing;

namespace ParseySharp.AspNetCore;

public static class EndpointBuilders
{

  public static RouteHandlerBuilder MapParsedPost<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<HttpContext, T, Task<IResult>> handler)
  {
    return app.MapPost(pattern, async (HttpContext ctx, IParseBinder binder, IProblemMapper problems) =>
    {
      var res = await binder.ParseAsync(ctx.Request, parser, ctx.RequestAborted).ConfigureAwait(false);
      return await res.Match(
        Fail: errs => Task.FromResult<IResult>(Results.Problem(problems.ToProblem(errs))),
        Succ: val => handler(ctx, val)
      ).ConfigureAwait(false);
    });
  }

  public static RouteHandlerBuilder MapParsedPost<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<IServiceProvider, T, Task<IResult>> handler) =>
    app.MapParsedPost(pattern, parser, (ctx, val) => handler(ctx.RequestServices, val));

  
  public static RouteHandlerBuilder MapParsedPost<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<T, Task<IResult>> handler) =>
    app.MapParsedPost(pattern, parser, 
      (HttpContext _, T val) => handler(val));

  public static RouteHandlerBuilder MapParsedGet<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<HttpContext, T, Task<IResult>> handler)
  {
    return app.MapGet(pattern, async (HttpContext ctx, IParseBinder binder, IProblemMapper problems) =>
    {
      // Let the binder select QueryStringContentHandler (registered by default) for GET
      var res = await binder.ParseAsync(ctx.Request, parser, ctx.RequestAborted).ConfigureAwait(false);
      return await res.Match(
        Fail: errs => Task.FromResult<IResult>(Results.Problem(problems.ToProblem(errs))),
        Succ: val => handler(ctx, val)
      ).ConfigureAwait(false);
    });
  }

  public static RouteHandlerBuilder MapParsedGet<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<IServiceProvider, T, Task<IResult>> handler) =>
    app.MapParsedGet(pattern, parser, (ctx, val) => handler(ctx.RequestServices, val));

  public static RouteHandlerBuilder MapParsedGet<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<T, Task<IResult>> handler) =>
    app.MapParsedGet(pattern, parser, 
      (HttpContext _, T val) => handler(val));

  public static RouteHandlerBuilder MapParsedPut<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<HttpContext, T, Task<IResult>> handler)
  {
    return app.MapPut(pattern, async (HttpContext ctx, IParseBinder binder, IProblemMapper problems) =>
    {
      var res = await binder.ParseAsync(ctx.Request, parser, ctx.RequestAborted).ConfigureAwait(false);
      return await res.Match(
        Fail: errs => Task.FromResult<IResult>(Results.Problem(problems.ToProblem(errs))),
        Succ: val => handler(ctx, val)
      ).ConfigureAwait(false);
    });
  }

  public static RouteHandlerBuilder MapParsedPut<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<IServiceProvider, T, Task<IResult>> handler) =>
    app.MapParsedPut(pattern, parser, (ctx, val) => handler(ctx.RequestServices, val));

  public static RouteHandlerBuilder MapParsedPut<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<T, Task<IResult>> handler) =>
    app.MapParsedPut(pattern, parser, 
      (HttpContext _, T val) => handler(val));

  public static RouteHandlerBuilder MapParsedDelete<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<HttpContext, T, Task<IResult>> handler)
  {
    return app.MapDelete(pattern, async (HttpContext ctx, IParseBinder binder, IProblemMapper problems) =>
    {
      var res = await binder.ParseAsync(ctx.Request, parser, ctx.RequestAborted).ConfigureAwait(false);
      return await res.Match(
        Fail: errs => Task.FromResult<IResult>(Results.Problem(problems.ToProblem(errs))),
        Succ: val => handler(ctx, val)
      ).ConfigureAwait(false);
    });
  }

  public static RouteHandlerBuilder MapParsedDelete<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<IServiceProvider, T, Task<IResult>> handler) =>
    app.MapParsedDelete(pattern, parser, (ctx, val) => handler(ctx.RequestServices, val));

  public static RouteHandlerBuilder MapParsedDelete<T>(
    this IEndpointRouteBuilder app,
    string pattern,
    Parse<T> parser,
    Func<T, Task<IResult>> handler) =>
    app.MapParsedDelete(pattern, parser, 
      (HttpContext _, T val) => handler(val));
}
