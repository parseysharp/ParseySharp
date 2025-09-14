namespace ParseySharp.AspNetCore;

// Type-only markers used to describe file uploads in request models (for docs/UI).
// These have no runtime behavior and do not affect parsing.
public interface IFileFormat { string ContentType { get; } }

// Generic, format-agnostic marker used when a file input supports multiple formats.
// Swagger encoding will default to application/octet-stream for this format.
public sealed class AnyFormat : IFileFormat
{
  public string ContentType => "application/octet-stream";
}

public sealed record FileUpload;

public sealed record FileUpload<TFormat>() where TFormat : IFileFormat, new();

public sealed record FileUpload<TFormat, TModel>() where TFormat : IFileFormat, new();
