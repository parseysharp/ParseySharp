using System.Text;

namespace ParseySharp.AspNetCore;

public static class TestHelpers
{
  // Creates a DefaultHttpContext with the given body and content-type and invokes the binder
  public static async Task<Validation<Seq<ParsePathErr>, A>> ParseBodyAsync<A>(IServiceProvider services, Parse<A> parser, string contentType, string body)
  {
    var ctx = new DefaultHttpContext();
    ctx.Request.ContentType = contentType;
    ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

    var binder = services.GetRequiredService<IParseBinder>();
    return await binder.ParseAsync<A>(ctx.Request, parser, CancellationToken.None);
  }
}
