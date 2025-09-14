using Avro;

namespace ParseySharp.AspNetCore;

public static class AcceptFileFormatAvro
{
  // Avro OCF (.avro) -> Seq<T>
  public static FormatDef<Unit, T, LanguageExt.Seq<T>> Avro<T>()
    => FormatDef.New<T, Seq<T>>(
      Name: "avro",
      ContentType: "application/octet-stream",
      Build: (fileField, shape) => ParseMultipartAvro.AvroAt(fileField, shape.Seq())
    );

  // Avro with explicit writer schema -> Seq<T>
  public static FormatDef<Unit, T, LanguageExt.Seq<T>> Avro<T>(Schema writerSchema)
    => FormatDef.New<T, Seq<T>>(
      Name: "avro",
      ContentType: "application/octet-stream",
      Build: (fileField, shape) => ParseMultipartAvro.AvroAt(fileField, shape.Seq(), writerSchema)
    );

  // Avro OCF (streaming) -> IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>
  public static FormatDef<Unit, T, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>> AvroStream<T>()
    => FormatDef.New<T, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(
      Name: "avro",
      ContentType: "application/octet-stream",
      Build: (file, shape) => ParseMultipartAvro.AvroStream(file, shape)
    );

  // Avro (streaming-when) OCF-only streaming branch
  public static FormatDef<Func<FilePart, bool>, T, Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>> AvroStreamingWhen<T>()
    => new(
      Name: "avro",
      ContentType: "application/octet-stream",
      Build: Reader.ask<Func<FilePart, bool>>()
        .Map<Func<FilePart, Parse<T>, Parse<Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>>>>(choose =>
          (file, shape) => choose(file)
            ? ParseMultipartAvro.AvroStream(file, shape).Map(stream => Right<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(stream)).As()
            : ParseMultipartAvro.AvroAt(file, shape.Seq()).Map(seq => Left<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(seq)).As()
        )
    );

  // Avro (streaming-when) with explicit writer schema for eager branch
  public static FormatDef<Func<FilePart, bool>, T, Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>> AvroStreamingWhen<T>(Schema writerSchema)
    => new(
      Name: "avro",
      ContentType: "application/octet-stream",
      Build: Reader.ask<Func<FilePart, bool>>()
        .Map<Func<FilePart, Parse<T>, Parse<Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>>>>(choose =>
          (file, shape) => choose(file)
            ? ParseMultipartAvro.AvroStream(file, shape).Map(stream => Right<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(stream)).As()
            : ParseMultipartAvro.AvroAt(file, shape.Seq(), writerSchema).Map(seq => Left<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(seq)).As()
        )
    );
}
