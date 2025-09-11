namespace ParseySharp.AspNetCore;

// Core multipart-friendly formats provided by the base ASP.NET Core package
public sealed class CsvFormat : IFileFormat
{ public string ContentType => "text/csv"; }

public sealed class NdjsonFormat : IFileFormat
{ public string ContentType => "application/x-ndjson"; }
