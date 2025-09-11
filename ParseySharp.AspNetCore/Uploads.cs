namespace ParseySharp.AspNetCore;

// Type-only markers used to describe file uploads in request models (for docs/UI).
// These have no runtime behavior and do not affect parsing.
public interface IFileFormat { string ContentType { get; } }

public sealed record FileUpload;

public sealed record FileUpload<TFormat>() where TFormat : IFileFormat, new();

public sealed record FileUpload<TFormat, TModel>() where TFormat : IFileFormat, new();
