# ParseySharp

Write your parser once. Run it everywhere (JSON, XML, YAML, Protobuf, MessagePack, Avro). First-class ASP.NET Core integration.

This repository hosts the ParseySharp core and its ecosystem packages.

- Core: `ParseySharp/`
- ASP.NET Core: `ParseySharp.AspNetCore/`
- Carrier adapters: `ParseySharp.*` (YamlDotNet, Protobuf, MessagePack, Avro, etc.)
- Swagger helpers: `ParseySharp.Swashbuckle/`
- Samples: `Samples/`

## Install

Add packages as needed (pick from the table below):

```bash
dotnet add package ParseySharp
# Optional adapters / integrations
# dotnet add package ParseySharp.AspNetCore
# dotnet add package ParseySharp.YamlDotNet
# dotnet add package ParseySharp.Protobuf
# dotnet add package ParseySharp.MessagePack
# dotnet add package ParseySharp.Avro
# dotnet add package ParseySharp.Swashbuckle
```

## 30‑second Quick Start

Build one parser and reuse it across carriers.

```csharp
using ParseySharp;

public sealed record Item(string kind, string? left, int? right);

var itemParser =
  (Parse.As<string>().At("kind", []),
   Parse.As<string>().Option().At("left", []),
   Parse.As<int>().Option().At("right", []))
  .Apply((kind, leftOpt, rightOpt) => new Item(
    kind,
    leftOpt.Match(None: (string?)null, Some: x => x),
    rightOpt.Match(None: (int?)null, Some: x => x)
  ))
  .As();

// JSON
var json = """{ "kind":"left", "left":"hello", "right":42 }""";
var okJson = itemParser.ParseJson()(System.Text.Json.JsonDocument.Parse(json).RootElement);

// XML
var xml = System.Xml.Linq.XElement.Parse("<item><kind>left</kind><left>hello</left><right>42</right></item>");
var okXml = itemParser.ParseXml()(xml);

// Protobuf (WellKnownTypes)
var pb = Google.Protobuf.WellKnownTypes.Struct.Parser.ParseJson(json);
var okPb = itemParser.ParseProtobuf()(pb);

// Protobuf (schema-backed generated IMessage types)
var ts = new Google.Protobuf.WellKnownTypes.Timestamp { Seconds = 123, Nanos = 456 };
var tsParser = (
  Parse.As<long>().At("seconds", []),
  Parse.As<int>().At("nanos", [])
).Apply((s, n) => (seconds: s, nanos: n)).As();
var okTs = tsParser.ParseProtobuf()(ts);
```

## Concepts in 60 seconds

- `Parse.As<T>()` — atomic parser for `T`
- `.At("field", [])` — navigate to a field/path
- `.Option()` — optional field (Option<T>)
- `.Apply((...) => new T(...))` — map tuple results into your type
- `.As()` — finalize the parser
- Return type: `Validation<Seq<ParsePathErr>, T>` with path-aware errors

## Practical example: payment method (business-friendly)

A common pattern is a discriminated payload: a `type` field decides which fields are required. Here’s a payment method example that maps to concrete types.

```csharp
using ParseySharp;

public abstract record PaymentMethod;
public sealed record Card(string number, int cvv) : PaymentMethod;
public sealed record Paypal(string email) : PaymentMethod;

// Dependent parser: shape depends on the value of "type"
var paymentParser = (
  from kind in Parse.As<string>().At("type", [])
  from pm in kind switch
  {
    "card" => (
      Parse.As<string>().At("number", []),
      Parse.As<int>().At("cvv", [])
    ).Apply((number, cvv) => (PaymentMethod)new Card(number, cvv)).As(),
    "paypal" => (
      Parse.As<string>().At("email", [])
    ).Apply(email => (PaymentMethod)new Paypal(email)).As(),
    _ => Parse.Fail<PaymentMethod>($"Unsupported type: {kind}")
  }
  select pm
).As();

// JSON input (works the same for XML/YAML/etc.)
var cardJson = """{ "type":"card", "number":"4111111111111111", "cvv": 123 }""";
var okCard = paymentParser.ParseJson()(System.Text.Json.JsonDocument.Parse(cardJson).RootElement);

var paypalJson = """{ "type":"paypal", "email":"user@example.com" }""";
var okPaypal = paymentParser.ParseJson()(System.Text.Json.JsonDocument.Parse(paypalJson).RootElement);
```

This uses C# LINQ query syntax over `Parse<T>` to express dependent parsing (the second `from` depends on the first’s value). The same `paymentParser` runs over JSON, XML, YAML, Protobuf, MessagePack, Avro, etc.

## Recipe index

- Optional fields: `.Option()`
- Alternatives: `p1.OrElse(p2)`
- Validation: `.Filter(pred => Left("why"))`
- Arrays and streaming: `parser.Seq()`, streaming variants
- Combine fields into a record: tuple + `.Apply(...)`
- Normalize types: string→int via `.Filter` or `OrElse`

See `ParseySharp.Tests/` and `Samples/` for working recipes.

## ASP.NET Core in two lines

Minimal API:

```csharp
app.MapParsedPost("/items", itemParser, (Item x) => Results.Ok(x))
   .AcceptsJson().AcceptsXml().AcceptsFormUrlEncoded();
```

MVC:

```csharp
[HttpPost("/mvc/items")]
[AcceptsJson][AcceptsXml][AcceptsFormUrlEncoded]
public Task<IActionResult> Post(CancellationToken ct)
  => this.ParsedAsync(itemParser, x => Ok(x), ct);
```

Swagger helpers: add `ParseySharp.Swashbuckle` and `RequestModel` annotations (Minimal API: `SetRequestModel<T>()`, MVC: `[RequestModel<T>]`).

## Carrier adapters

- JSON (System.Text.Json): Core
- Newtonsoft.Json: `ParseySharp.NewtonsoftJson`
- XML: Core
- YAML: `ParseySharp.YamlDotNet`
- Protobuf: `ParseySharp.Protobuf`
- MessagePack: `ParseySharp.MessagePack`
- Avro: `ParseySharp.Avro`
- DataTables: Core
- In‑memory objects (Map/List/etc): Core
- Reflection: Core

## Samples and tests

- Samples: `Samples/ParseySharp.SampleWeb/` (Minimal API + MVC, multi‑format endpoints)
- Tests: `ParseySharp.Tests/` (broad carrier coverage)

## License

MIT. See `LICENSE`.

## Extending to new formats (3 functions)

To add a new carrier, you only need to supply three functions to a `ParsePathNav<TCarrier>`:

- `Prop` (drill into a carrier by name)
- `Index` (drill into a carrier by array index)
- `Unbox` (extract a primitive value from a carrier)

Here’s a concrete illustration using a familiar type, `System.Text.Json.JsonElement`.

```csharp
using System.Text.Json;

public static class ParsePathNavJson
{
  public static readonly ParsePathNav<JsonElement> Json = new(
    Prop: (node, name) =>
      node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var child)
        ? Right<JsonElement, Option<JsonElement>>(Optional(child))
        : node.ValueKind == JsonValueKind.Object
          ? Right<JsonElement, Option<JsonElement>>(None)
          : Left<JsonElement, Option<JsonElement>>(node),

    Index: (node, i) =>
      node.ValueKind == JsonValueKind.Array && i >= 0 && i < node.GetArrayLength()
        ? Right<JsonElement, Option<JsonElement>>(Optional(node[i]))
        : node.ValueKind == JsonValueKind.Array
          ? Right<JsonElement, Option<JsonElement>>(None)
          : Left<JsonElement, Option<JsonElement>>(node),

    Unbox: node => node.ValueKind switch
    {
      JsonValueKind.Null   => Right<JsonElement, Unknown<JsonElement>>(new Unknown<JsonElement>.None()),
      JsonValueKind.String => Right<JsonElement, Unknown<JsonElement>>(Unknown.New<JsonElement>(node.GetString()!)),
      JsonValueKind.True   => Right<JsonElement, Unknown<JsonElement>>(Unknown.New<JsonElement>(true)),
      JsonValueKind.False  => Right<JsonElement, Unknown<JsonElement>>(Unknown.New<JsonElement>(false)),
      // handle other cases
      _ => Left<JsonElement, Unknown<JsonElement>>(node)
    }
  );
}

// Using your custom navigator with any parser:
//   var result = parser.RunWithNav(ParsePathNavJson.Json)(jsonElement);
```

See a production-grade example in `ParseySharp.Avro/Navigate.Avro.cs`, which defines a navigator over Avro generic values by implementing these three functions.
