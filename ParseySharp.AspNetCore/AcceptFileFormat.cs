namespace ParseySharp.AspNetCore;

public static class AcceptFileFormat
{
  
  public static FormatDef<Unit, T, LanguageExt.Seq<T>> XmlRows<T>()
    => FormatDef.New<T, Seq<T>>(
      Name: "xml",
      ContentType: "application/xml",
      Build: (file, shape) => ParseMultipart.XmlAt(file, shape.Seq())
    );

  public static FormatDef<Unit, T, T> Xml<T>()
    => FormatDef.New<T, T>(
      Name: "xml",
      ContentType: "application/xml",
      Build: (file, shape) => ParseMultipart.XmlAt(file, shape)
    );

  public static FormatDef<Unit, T, T> Json<T>()
    => FormatDef.New<T, T>(
      Name: "json",
      ContentType: "application/json",
      Build: (file, shape) => ParseMultipart.JsonAt(file, shape)
    );

  // CSV rows -> Seq<T>
  public static FormatDef<Unit, T, LanguageExt.Seq<T>> Csv<T>(bool hasHeader = true)
    => FormatDef.New<T, Seq<T>>(
      Name: "csv",
      ContentType: "text/csv",
      Build: (file, shape) => ParseMultipart.CsvAt(file, hasHeader, shape)
    );

  // NDJSON -> Seq<T>
  public static FormatDef<Unit, T, LanguageExt.Seq<T>> Ndjson<T>()
    => FormatDef.New<T, Seq<T>>(
      Name: "ndjson",
      ContentType: "application/x-ndjson",
      Build: (file, shape) => ParseMultipart.NdjsonAt(file, shape)
    );

  // CSV (streaming) -> IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>
  public static FormatDef<Unit, T, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>> CsvStream<T>(bool hasHeader = true)
    => FormatDef.New<T, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(
      Name: "csv",
      ContentType: "text/csv",
      Build: (file, shape) => ParseMultipart.CsvStream(file, hasHeader, shape)
    );

  // NDJSON (streaming) -> IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>
  public static FormatDef<Unit, T, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>> NdjsonStream<T>()
    => FormatDef.New<T, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(
      Name: "ndjson",
      ContentType: "application/x-ndjson",
      Build: (file, shape) => ParseMultipart.NdjsonStream(file, shape)
    );

  // CSV (streaming-when) -> Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>
  public static FormatDef<Func<FilePart, bool>, T, Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>> CsvStreamingWhen<T>(bool hasHeader = true)
    => new(
      Name: "csv",
      ContentType: "text/csv",
      Build: Reader.ask<Func<FilePart, bool>>()
        .Map<Func<FilePart, Parse<T>, Parse<Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>>>>(choose =>
          (file, shape) => choose(file)
            ? ParseMultipart.CsvStream(file, hasHeader, shape).Map(stream => Right<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(stream)).As()
            : ParseMultipart.CsvAt(file, hasHeader, shape).Map(seq => Left<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(seq)).As()
        )
    );

  // NDJSON (streaming-when) -> Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>
  public static FormatDef<Func<FilePart, bool>, T, Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>> NdjsonStreamingWhen<T>()
    => new(
      Name: "ndjson",
      ContentType: "application/x-ndjson",
      Build: Reader.ask<Func<FilePart, bool>>()
        .Map<Func<FilePart, Parse<T>, Parse<Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>>>>(choose =>
          (file, shape) => choose(file)
            ? ParseMultipart.NdjsonStream(file, shape).Map(stream => Right<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(stream)).As()
            : ParseMultipart.NdjsonAt(file, shape).Map(seq => Left<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(seq)).As()
        )
    );
}