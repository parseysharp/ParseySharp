using Google.Protobuf.WellKnownTypes;
using Google.Protobuf;

namespace ParseySharp.AspNetCore;

internal sealed class ProtobufContentHandler : IContentHandler
{
  private static bool IsProtobuf(string? ct)
    => !string.IsNullOrWhiteSpace(ct) && (
         ct!.Contains("application/x-protobuf", StringComparison.OrdinalIgnoreCase)
      || ct.Contains("application/protobuf", StringComparison.OrdinalIgnoreCase)
    );

  public bool CanHandle(HttpRequest request) => IsProtobuf(request.ContentType);

  public async Task<Either<string, object>> ReadAsync(HttpRequest request, CancellationToken ct)
  {
    try
    {
      using var ms = new MemoryStream();
      await request.Body.CopyToAsync(ms, ct);
      var bytes = ms.ToArray();
      if (bytes.Length == 0)
        return Right<string, object>(new Struct()); // treat empty as empty object

      // If the endpoint provided a decoder (via AcceptsProtobuf overload), use it.
      var decoder = request.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ProtobufDecoderMetadata>()?.Decode;
      if (decoder is not null)
      {
        try
        {
          var msg = decoder(bytes);
          return Right<string, object>(msg);
        }
        catch (Exception ex)
        {
          return Left<string, object>($"Invalid Protobuf: {ex.Message}");
        }
      }

      // Try schema-less WellKnownTypes in order: Struct, Value, ListValue
      // This gives us a carrier without requiring a generated IMessage type.
      try { return Right<string, object>(Struct.Parser.ParseFrom(bytes)); }
      catch {}
      try { return Right<string, object>(Value.Parser.ParseFrom(bytes)); }
      catch {}
      try { return Right<string, object>(ListValue.Parser.ParseFrom(bytes)); }
      catch {}

      return Left<string, object>("Unsupported Protobuf payload: expected Struct, Value, or ListValue");
    }
    catch (Exception ex)
    {
      return Left<string, object>($"Invalid Protobuf: {ex.Message}");
    }
  }

  public Validation<Seq<ParsePathErr>, A> Run<A>(Parse<A> parser, object carrier)
  {
    if (carrier is IMessage || carrier is Struct || carrier is Value || carrier is ListValue)
      return parser.ParseProtobuf()(carrier);

    return Fail<Seq<ParsePathErr>, A>([ new ParsePathErr("Unsupported Protobuf carrier", typeof(A).Name, None, []) ]);
  }

  public IEnumerable<string> SupportedContentTypes => new[] { "application/x-protobuf", "application/protobuf" };
}
