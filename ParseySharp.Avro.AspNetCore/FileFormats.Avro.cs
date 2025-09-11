namespace ParseySharp.Avro.AspNetCore;

using ParseySharp.AspNetCore;

public sealed class AvroFormat : IFileFormat
{ public string ContentType => "application/x-avro"; }
