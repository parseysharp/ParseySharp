namespace ParseySharp.AspNetCore;

public sealed record FilePart(
  string Name,
  string FileName,
  string ContentType,
  long Length,
  Func<Stream> OpenRead
);
