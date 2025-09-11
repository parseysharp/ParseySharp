using ParseySharp;
using ParseySharp.AspNetCore;
using ParseySharp.Swashbuckle;
using Google.Protobuf.WellKnownTypes;

var builder = WebApplication.CreateBuilder(args);

// Register ParseySharp ASP.NET core with JSON handler
builder.Services
  .AddEndpointsApiExplorer()
  .AddSwaggerGen()
  .AddParseySharpCore()
  .AddParseySharpMessagePack()
  .AddParseySharpProtobuf()
  .AddParseySharpMultipart()
  .AddSwaggerGen(c =>
    c.OperationFilter<RequestModelOperationFilter>()
  );

// MVC controllers for /mvc routes (mirrors the Minimal API endpoints)
builder.Services
  .AddControllers()
  .AddParseySharpMvc();

builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });

// Define an explicit parser for Item (JSON body with fields kind/left/right)
var itemParser =
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
    rightOpt.Match(None: (int?)null, Some: x => x)
  )).As();

var app = builder.Build();

// Swagger UI for trying the endpoints and inspecting RequestType
app.UseSwagger();
app.UseSwaggerUI();

// Map MVC controllers
app.MapControllers();

app.MapParsedPost("/items", itemParser, (Item item) => Task.FromResult<IResult>(Results.Ok(new { received = item })))
   .SetRequestModel<Item>()
   .AcceptsJson()
   .AcceptsXml()
   .AcceptsFormUrlEncoded()
   .AcceptsMessagePack()
   .AcceptsProtobuf();

app.MapParsedGet("/items", itemParser, (Item item) => Task.FromResult<IResult>(Results.Ok(new { received = item })))
   .SetRequestModel<Item>()
   .AcceptsQueryString();

var tsParser =
  (Parse.As<long>().At("seconds", []),
   Parse.As<int>().At("nanos", []))
  .Apply((s, n) => ( BigGuys: s, LittleGuys: n ))
  .As();

app.MapParsedPost("/ts", tsParser, 
  ((long BigGuys, int LittleGuys) x) => Task.FromResult<IResult>(
    Results.Ok(new { x.BigGuys, x.LittleGuys })))
   .AcceptsProtobuf(Timestamp.Parser);

// NOTE on GET: typical GET requests do not carry bodies. Only use MapParsedGet
// if you purposefully send a body (e.g., application/json). Otherwise, prefer
// separate query/route parsers (not shown here), since the binder reads the body.

app.MapGet("/health", () => Results.Ok(new { ok = true }));

// --- CSV (eager) ---
var csvLineParser =
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
  .Apply((id, name, ageOpt) => new CsvRow(id, name, ageOpt)).As();

static Parse<WithFile<T>> withFileParser<T>(Parse<T> parser) =>
  (parser,
   Parse.As<string>().At("name", [])).Apply((rows, name) => new WithFile<T>(rows, name)).As();

app.MapParsedPost("/import-csv",
  withFileParser(ParseMultipart.CsvAt("file", hasHeader: true, csvLineParser)),
  (WithFile<Seq<CsvRow>> input)
  => Task.FromResult<IResult>(Results.Ok(new { input.name, results = input.value })))
  .AcceptsMultipart();

// --- CSV (streaming) ---
app.MapParsedPost("/import-csv-stream",
  withFileParser(ParseMultipart.CsvStream<CsvRow>("file", hasHeader: true, csvLineParser.As())),
  async (WithFile<IAsyncEnumerable<Validation<Seq<ParsePathErr>, CsvRow>>> input) =>
{
  var (bad, good) = await input.value.AsSourceT<IO, Validation<Seq<ParsePathErr>, CsvRow>>()
    .Collect()
    .Map(v => v.Map(v => v.ToEither()).Partition())
    .RunAsync();
  return Results.Ok(new { input.name, good, bad });
}).AcceptsMultipart();

// --- Avro OCF (eager) ---
app.MapParsedPost("/import-avro",
  withFileParser(ParseMultipartAvro.AvroAt("file", csvLineParser.Seq())),
  (WithFile<Seq<CsvRow>> input)
  => Task.FromResult<IResult>(Results.Ok(new { input.name, results = input.value })))
  .AcceptsMultipart();

// --- Avro OCF (streaming) ---
app.MapParsedPost("/import-avro-stream",
  withFileParser(ParseMultipartAvro.AvroStream("file", csvLineParser)),
  async (WithFile<IAsyncEnumerable<Validation<Seq<ParsePathErr>, CsvRow>>> input) =>
{
  var (bad, good) = await input.value.AsSource()
    .Collect()
    .Map(v => v.Map(v => v.ToEither()).Partition())
    .RunAsync();
  return Results.Ok(new { input.name, good, bad });
}).AcceptsMultipart();

app.MapParsedPost("/import-yaml",
  withFileParser(ParseMultipartYaml.YamlAt("file", csvLineParser.Seq())),
  (WithFile<Seq<CsvRow>> input)
  => Task.FromResult<IResult>(Results.Ok(new { input.name, results = input.value })))
  .AcceptsMultipart();

var ndjsonLineParser =
  (Parse.As<string>().At("id", []),
   Parse.As<int>().Option().At("count", []))
  .Apply((id, count) => new EventRow(id, count));

app.MapParsedPost("/import-ndjson",
  withFileParser(ParseMultipart.NdjsonAt("file", ndjsonLineParser.As())),
  (WithFile<Seq<EventRow>> input)
  => Task.FromResult<IResult>(Results.Ok(new { input.name, result = input.value })))
  .AcceptsMultipart();

// --- NDJSON (streaming) ---
app.MapParsedPost("/import-ndjson-stream",
  withFileParser(ParseMultipart.NdjsonStream("file", ndjsonLineParser.As())),
  async (WithFile<IAsyncEnumerable<Validation<Seq<ParsePathErr>, EventRow>>> input) =>
{
  var (bad, good) = await input.value.AsSource()
    .Collect()
    .Map(v => v.Map(v => v.ToEither()).Partition())
    .RunAsync();
  return Results.Ok(new { input.name, good, bad });
}).AcceptsMultipart();

app.Run();

// A simple DTO we'll bind to (must be declared before top-level statements)
public sealed record Item(string kind, string? left, int? right);
public sealed record CsvRow(string id, string name, Option<int> age);
public sealed record EventRow(string id, Option<int> count);
public sealed record WithFile<T>(T value, string name);