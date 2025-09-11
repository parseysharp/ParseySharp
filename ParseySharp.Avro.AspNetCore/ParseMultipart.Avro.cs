using Avro;
using Avro.File;
using Avro.Generic;

namespace ParseySharp.AspNetCore;

public static class ParseMultipartAvro
{
  // Unified helper: auto-detect OCF; otherwise require writer schema via overload
  public static Parse<T> AvroAt<T>(string name, Parse<T> parser)
    => ParseMultipart.FileAt(name).Filter(fp =>
    {
      try
      {
        using var s = fp.OpenRead();
        var top = DecodeTopOcFile(s).Match(
          Left: err => (object?)null,
          Right: val => val
        );
        if (top is null)
          return Fail<Seq<ParsePathErr>, T>([
            new ParsePathErr("Raw Avro datum is not supported; please upload an Avro Object Container File (.avro)", typeof(T).Name, Some((object)fp.FileName), [])
          ]);

        return ParseExtensions.RunWithNav(parser, ParseySharp.Avro.ParsePathNavAvro.Avro)(top);
      }
      catch (Exception ex)
      {
        return Fail<Seq<ParsePathErr>, T>([
          new ParsePathErr($"Invalid Avro: {ex.Message}", typeof(T).Name, Some((object)name), [])
        ]);
      }
    });

  public static Parse<T> AvroAt<T>(string name, Parse<T> parser, Schema writerSchema)
    => ParseMultipart.FileAt(name).Filter(fp =>
    {
      try
      {
        using var s = fp.OpenRead();
        var eTop = IsOcFile(s)
          ? DecodeTopOcFile(s)
          : DecodeTopRaw(s, writerSchema);

        return eTop.Match(
          Left: err => Fail<Seq<ParsePathErr>, T>([
            new ParsePathErr(err, typeof(T).Name, Some((object)name), [])
          ]),
          Right: top => ParseExtensions.RunWithNav(parser, ParseySharp.Avro.ParsePathNavAvro.Avro)(top)
        );
      }
      catch (Exception ex)
      {
        return Fail<Seq<ParsePathErr>, T>([
          new ParsePathErr($"Invalid Avro: {ex.Message}", typeof(T).Name, Some((object)name), [])
        ]);
      }
    });

  // STREAMING OCF: yield per-record validations without materializing entire file
  public static Parse<IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>> AvroStream<T>(string name, Parse<T> elementParser)
    => ParseMultipart.FileAt(name).Filter(fp =>
      Success<Seq<ParsePathErr>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(
        AvroStreamIterator(fp, elementParser)
      )
    );

  // Detect Avro OCF by magic bytes 'Obj' 0x4F 62 6A 01
  private static bool IsOcFile(Stream stream)
  {
    if (!stream.CanSeek)
    {
      // Wrap to allow peeking
      using var ms = new MemoryStream();
      stream.CopyTo(ms);
      ms.Position = 0;
      var result = IsOcFile(ms);
      ms.Position = 0;
      return result;
    }
    var pos = stream.Position;
    try
    {
      Span<byte> header = stackalloc byte[4];
      var read = stream.Read(header);
      stream.Position = pos;
      return read == 4 && header[0] == 0x4F && header[1] == 0x62 && header[2] == 0x6A && header[3] == 0x01;
    }
    finally
    {
      stream.Position = pos;
    }
  }

  // Decode top-level value from an OCF (.avro) file: typically a sequence of GenericRecord
  private static Either<string, object> DecodeTopOcFile(Stream s)
  {
    if (!IsOcFile(s)) return Left<string, object>("Not OCF");
    using var reader = DataFileReader<object>.OpenReader(s);
    var items = new List<object>();
    while (reader.HasNext()) items.Add(reader.Next()!);
    // If there's exactly one item and it's not a collection, pass it directly; else pass the sequence
    if (items.Count == 1 && items[0] is not System.Collections.IEnumerable)
      return Right<string, object>(items[0]);
    return Right<string, object>(items);
  }

  // Decode a single top-level Avro datum using a provided writer schema (raw bytes)
  private static Either<string, object> DecodeTopRaw(Stream s, Schema writerSchema)
  {
    try
    {
      var decoder = new BinaryDecoder(s);
      var datumReader = new GenericDatumReader<object>(writerSchema, writerSchema);
      #pragma warning disable CS8625 // Allow null reuse per Avro API contract
      var obj = datumReader.Read(null, decoder);
      #pragma warning restore CS8625
      return Right<string, object>(obj!);
    }
    catch (Exception ex)
    {
      return Left<string, object>($"Invalid raw Avro: {ex.Message}");
    }
  }

  private static async IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>> AvroStreamIterator<T>(
    FilePart fp,
    Parse<T> elementParser,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    using var s = fp.OpenRead();
    // Ensure the async iterator contains an await to avoid CS1998 while preserving current behavior
    await Task.Yield();
    if (!IsOcFile(s))
    {
      yield return Fail<Seq<ParsePathErr>, T>([
        new ParsePathErr("Streaming is only supported for Avro Object Container Files (.avro)", typeof(T).Name, Some((object)fp.FileName), [])
      ]);
      yield break;
    }

    using var reader = DataFileReader<object>.OpenReader(s);
    var idx = 0;
    while (reader.HasNext())
    {
      Validation<Seq<ParsePathErr>, T> result;
      try
      {
        var item = reader.Next()!;
        result = ParseExtensions.RunWithNav(elementParser, ParseySharp.Avro.ParsePathNavAvro.Avro)(item)
          .MapFail(es => es.Map(e => e.WithPrefix([idx.ToString()])));
      }
      catch (Exception ex)
      {
        result = Fail<Seq<ParsePathErr>, T>([
          new ParsePathErr($"Invalid Avro record: {ex.Message}", typeof(T).Name, Some((object)fp.FileName), [])
        ]);
      }
      yield return result;
      idx++;
      if (cancellationToken.IsCancellationRequested) yield break;
    }
  }
}
