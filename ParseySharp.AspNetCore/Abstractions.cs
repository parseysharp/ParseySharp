namespace ParseySharp.AspNetCore;

// Options used by the binder. Provide a parameterless ctor so Options can activate it.
public sealed class ParseySharpOptions
{
  public NumericPreference NumericPreference { get; init; } = NumericPreference.PreferInt32;
  public NormalizationFlags Normalization { get; init; } = NormalizationFlags.Default;
  public string? DefaultContentType { get; init; } = "application/json";

  public ParseySharpOptions() {}
}

public enum NumericPreference { PreferInt32, PreferInt64 }

[Flags]
public enum NormalizationFlags
{
  Default = 0,
  // Example: normalize MessagePack array-of-[k,v] into Map
  KvArraysToMaps = 1 << 0,
}

// Stateless, format-specific content reader + parser adapter
public interface IContentHandler
{
  bool CanHandle(HttpRequest request);

  Task<Either<string, object>> ReadAsync(HttpRequest request, CancellationToken ct);

  Validation<Seq<ParsePathErr>, A> Run<A>(Parse<A> parser, object carrier);

  // Declares canonical content-types this handler supports (used for validation/OpenAPI helpers)
  IEnumerable<string> SupportedContentTypes { get; }
}

// Pure binder that composes handlers
public interface IParseBinder
{
  // Uses the specific parser supplied by the endpoint
  Task<Validation<Seq<ParsePathErr>, A>> ParseAsync<A>(HttpRequest request, Parse<A> parser, CancellationToken ct);
}

// Maps Validation failures to ProblemDetails
public interface IProblemMapper
{
  ProblemDetails ToProblem(Seq<ParsePathErr> errors, int? statusCode = null);
}

// Endpoint metadata to carry the exact parser the route wants to use
public sealed record ParserMetadata<A>(Parse<A> Parser);

// Endpoint metadata for Protobuf: optional per-endpoint decoder from raw bytes to an object (expected to be an IMessage)
public sealed record ProtobufDecoderMetadata(Func<ReadOnlyMemory<byte>, object> Decode);
