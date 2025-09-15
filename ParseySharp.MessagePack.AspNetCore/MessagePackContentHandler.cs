using ParseySharp;
using ParseySharp.MessagePack;

namespace ParseySharp.AspNetCore;

internal sealed class MessagePackContentHandler : IContentHandler
{
  private static bool IsMsgPack(string? ct)
    => !string.IsNullOrWhiteSpace(ct) && (
         ct!.Contains("application/x-msgpack", StringComparison.OrdinalIgnoreCase)
      || ct.Contains("application/msgpack", StringComparison.OrdinalIgnoreCase)
    );

  public bool CanHandle(HttpRequest request) => IsMsgPack(request.ContentType);

  public async Task<Either<string, object>> ReadAsync(HttpRequest request, CancellationToken ct)
  {
    try
    {
      // Read entire body to an owned buffer
      using var ms = new MemoryStream();
      await request.Body.CopyToAsync(ms, ct).ConfigureAwait(false);
      var bytes = ms.ToArray();

      // Allow empty to be treated as nil/document root missing
      if (bytes.Length == 0)
        return Right<string, object>(new MsgNode.Nil());

      var nodeOrErr = MessagePackBuilder.FromBytes(bytes);
      return nodeOrErr.Match<Either<string, object>>(
        Right: n => Right<string, object>(n),
        Left: err => Left<string, object>(err)
      );
    }
    catch (Exception ex)
    {
      return Left<string, object>($"Invalid MessagePack: {ex.Message}");
    }
  }

  public Validation<Seq<ParsePathErr>, A> Run<A>(Parse<A> parser, object carrier)
  {
    if (carrier is MsgNode node)
      return parser.ParseMessagePackNode()(node);

    return Fail<Seq<ParsePathErr>, A>([ new ParsePathErr("Unsupported MessagePack carrier", typeof(A).Name, None, []) ]);
  }

  public IEnumerable<string> SupportedContentTypes => new[] { "application/x-msgpack", "application/msgpack" };
}
