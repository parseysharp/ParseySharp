using Microsoft.AspNetCore.Mvc;
using ParseySharp.AspNetCore;
using ParseySharp.Avro.AspNetCore;
using ParseySharp.Yaml.AspNetCore;

namespace ParseySharp.SampleWeb.Controllers;

[ApiController]
[Route("mvc")] // keep endpoints under /mvc to avoid colliding with Minimal API samples
public sealed class ParsedMvcController : ControllerBase
{
    // Shared parsers (mirrors Program.cs)
    private static readonly Parse<Item> ItemParser =
      (Parse.As<string>().At("kind", []),
       Parse.As<string>().Option().At("left", []),
       Parse.As<int>()
         .OrElse(
           Parse.As<string>().Filter(
             s => int.TryParse(s, out var i)
               ? Right<string, int>(i)
               : Left<string, int>("Invalid integer")
           )
         )
         .Option()
         .Filter(x => x.Filter(_ => _ < 21).Map(_ => "Below 21!"))
         .At("right", []))
      .Apply((kind, leftOpt, rightOpt) => new Item(
        kind,
        leftOpt.Match(None: (string?)null, Some: x => x),
        rightOpt.Match(None: (int?)null, Some: x => x)))
      .As();

    private static readonly Parse<CsvRow> CsvLineParser =
      (Parse.As<string>().At("id", []),
       Parse.As<string>().At("name", []),
       Parse.As<int>().Option()
         .OrElse(
           Parse.As<string>().Filter(
             s => string.IsNullOrEmpty(s)
               ? Right<string, Option<int>>(None)
               : int.TryParse(s, out var i)
                 ? Right<string, Option<int>>(Some(i))
                 : Left<string, Option<int>>("Invalid integer")
           )
         )
         .At("age", []))
      .Apply((id, name, ageOpt) => new CsvRow(id, name, ageOpt))
      .As();

    private static Parse<WithFile<T>> WithFileParser<T>(Parse<T> parser) =>
      (parser, Parse.As<string>().At("name", [])).Apply((rows, name) => new WithFile<T>(rows, name)).As();

    private static readonly Parse<EventRow> NdjsonLineParser =
      (Parse.As<string>().At("id", []),
       Parse.As<int>().Option().At("count", []))
      .Apply((id, count) => new EventRow(id, count))
      .As();

    // Body (Item)
    [HttpPost("items")]
    [AcceptsJson]
    [AcceptsXml]
    [AcceptsFormUrlEncoded]
    [AcceptsMessagePack]
    [AcceptsProtobuf]
    [RequestModel<Item>]
    public Task<IActionResult> PostItem(CancellationToken ct)
      => this.ParsedAsync(ItemParser, item => Ok(new { received = item }), ct);

    // GET (query string)
    [HttpGet("items")]
    [AcceptsQueryString]
    [RequestModel<Item>]
    public Task<IActionResult> GetItem(CancellationToken ct)
      => this.ParsedAsync(ItemParser, item => Ok(new { received = item }), ct);

    // Payments (business-friendly example from README)
    [HttpPost("payments")]
    [AcceptsJson]
    [AcceptsXml]
    [AcceptsFormUrlEncoded]
    [AcceptsMessagePack]
    [AcceptsProtobuf]
    [RequestModel<PaymentMethodDoc>]
    public Task<IActionResult> PostPayment(CancellationToken ct)
      => this.ParsedAsync(Transactions.PaymentMethodParser, pm => Ok(new { received = pm }), ct);

    // CSV eager
    [HttpPost("import-csv")]
    [AcceptsMultipart]
    [RequestModel<ImportCsvDoc>]
    public Task<IActionResult> ImportCsv([FromForm] string name, CancellationToken ct)
      => this.ParsedAsync(
        WithFileParser(ParseMultipart.CsvAt("file", hasHeader: true, CsvLineParser)),
        input => Ok(new { input.name, results = input.value }),
        ct);

    // CSV streaming
    [HttpPost("import-csv-stream")]
    [AcceptsMultipart]
    [RequestModel<ImportCsvStreamDoc>]
    public Task<IActionResult> ImportCsvStream([FromForm] string name, CancellationToken ct)
      => this.ParsedAsync(
        WithFileParser(ParseMultipart.CsvStream<CsvRow>("file", hasHeader: true, CsvLineParser.As())),
        async input =>
        {
            var (bad, good) = await input.value.AsSourceT<IO, Validation<Seq<ParsePathErr>, CsvRow>>()
              .Collect()
              .Map(v => v.Map(v => v.ToEither()).Partition())
              .RunAsync();
            return Ok(new { input.name, good, bad });
        },
        ct);

    // Avro eager
    [HttpPost("import-avro")]
    [AcceptsMultipart]
    [RequestModel<ImportAvroDoc>]
    public Task<IActionResult> ImportAvro([FromForm] string name, CancellationToken ct)
      => this.ParsedAsync(
        WithFileParser(ParseMultipartAvro.AvroAt("file", CsvLineParser.Seq())),
        input => Ok(new { input.name, results = input.value }),
        ct);

    // Avro streaming
    [HttpPost("import-avro-stream")]
    [AcceptsMultipart]
    [RequestModel<ImportAvroStreamDoc>]
    public Task<IActionResult> ImportAvroStream([FromForm] string name, CancellationToken ct)
      => this.ParsedAsync(
        WithFileParser(ParseMultipartAvro.AvroStream("file", CsvLineParser)),
        async input =>
        {
            var (bad, good) = await input.value.AsSource()
              .Collect()
              .Map(v => v.Map(v => v.ToEither()).Partition())
              .RunAsync();
            return Ok(new { input.name, good, bad });
        },
        ct);

    // YAML eager
    [HttpPost("import-yaml")]
    [AcceptsMultipart]
    [RequestModel<ImportYamlDoc>]
    public Task<IActionResult> ImportYaml([FromForm] string name, CancellationToken ct)
      => this.ParsedAsync(
        WithFileParser(ParseMultipartYaml.YamlAt("file", CsvLineParser.Seq())),
        input => Ok(new { input.name, results = input.value }),
        ct);

    // NDJSON eager
    [HttpPost("import-ndjson")]
    [AcceptsMultipart]
    [RequestModel<ImportNdjsonDoc>]
    public Task<IActionResult> ImportNdjson([FromForm] string name, CancellationToken ct)
      => this.ParsedAsync(
        WithFileParser(ParseMultipart.NdjsonAt("file", NdjsonLineParser.As())),
        input => Ok(new { input.name, result = input.value }),
        ct);

    // NDJSON streaming
    [HttpPost("import-ndjson-stream")]
    [AcceptsMultipart]
    [RequestModel<ImportNdjsonStreamDoc>]
    public Task<IActionResult> ImportNdjsonStream([FromForm] string name, CancellationToken ct)
      => this.ParsedAsync(
        WithFileParser(ParseMultipart.NdjsonStream("file", NdjsonLineParser.As())),
        async input =>
        {
            var (bad, good) = await input.value.AsSource()
              .Collect()
              .Map(v => v.Map(v => v.ToEither()).Partition())
              .RunAsync();
            return Ok(new { input.name, good, bad });
        },
        ct);
}

// Doc-only type to describe the payment request shape (type-discriminated)
public sealed record PaymentMethodDoc(PaymentMethodType paymentMethodType, string? number, int? cvv, string? routingNumber, string? accountNumber);

// Doc-only type to describe the CSV multipart request shape (no impact on parsing)
public sealed record ImportCsvDoc(FileUpload<CsvFormat, CsvRow> file, string name);
public sealed record ImportCsvStreamDoc(FileUpload<CsvFormat, CsvRow> file, string name);
public sealed record ImportAvroDoc(FileUpload<AvroFormat, CsvRow> file, string name);
public sealed record ImportAvroStreamDoc(FileUpload<AvroFormat, CsvRow> file, string name);
public sealed record ImportYamlDoc(FileUpload<YamlFormat, CsvRow> file, string name);
public sealed record ImportNdjsonDoc(FileUpload<NdjsonFormat, EventRow> file, string name);
public sealed record ImportNdjsonStreamDoc(FileUpload<NdjsonFormat, EventRow> file, string name);
