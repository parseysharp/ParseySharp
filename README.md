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
using ParseySharp.Refine;

public abstract record PaymentMethod;
public sealed record Card(string Number, int Cvv) : PaymentMethod;
public sealed record Ach(string RoutingNumber, string AccountNumber) : PaymentMethod;

public enum PaymentMethodType { Card, Ach }

public readonly record struct ValidPayment(Refine.Refined<PaymentMethod, ValidPayment> Inner)
  : Refine.IRefine<PaymentMethod, ValidPayment>
{
  public static LanguageExt.Seq<string> Errors(PaymentMethod x) =>
    x switch
    {
      Card c =>
        "CVV is invalid".ErrUnless(() => c.Cvv is >= 100 and <= 999 || c.Cvv is >= 1000 and <= 9999),
      Ach a =>
        "ACH account number is invalid".ErrUnless(() => !string.IsNullOrWhiteSpace(a.AccountNumber)),
      _ => LanguageExt.Seq<string>.Empty
    };

  public static LanguageExt.Either<LanguageExt.Seq<string>, ValidPayment> Refined(PaymentMethod x)
    => Refine.Create<PaymentMethod, ValidPayment>(x).Map(v => new ValidPayment(v));
}

var paymentParser =
  (from paymentMethodType in Parse.Enum<PaymentMethodType>().At("type", [])
   from pm in paymentMethodType switch
   {
     PaymentMethodType.Card => (
         Parse.As<string>().At("number", []),
         Parse.Int32Flex().At("cvv", [])
       ).Apply((number, cvv) => (PaymentMethod)new Card(number, cvv)).As(),
     PaymentMethodType.Ach => (
         Parse.As<string>().At("routingNumber", []),
         Parse.As<string>().At("accountNumber", [])
       ).Apply((routingNumber, accountNumber) => (PaymentMethod)new Ach(routingNumber, accountNumber)).As(),
     _ => Parse.Fail<PaymentMethod>("Unsupported type")
   }
   select pm)
  .As()
  .Filter(ValidPayment.Refined);

// JSON (works the same for XML/YAML/Protobuf/MessagePack/Avro)
var cardJson = """{ "type":"Card", "number":"4111111111111111", "cvv": 123 }""";
var okCard = paymentParser.ParseJson()(System.Text.Json.JsonDocument.Parse(cardJson).RootElement);

var achJson = """{ "type":"Ach", "routingNumber":"021000021", "accountNumber":"000123456789" }""";
var okAch = paymentParser.ParseJson()(System.Text.Json.JsonDocument.Parse(achJson).RootElement);
```

## Concepts in 60 seconds

- `Parse.As<T>()` — atomic parser for `T`
- `.At("field", [])` — navigate to a field/path
- `.Option()` — optional field (Option<T>)
- `.Apply((...) => new T(...))` — map tuple results into your type
- `.As()` — finalize the parser
- Return type: `Validation<Seq<ParsePathErr>, T>` with path-aware errors

## Practical example

A common pattern is a discriminated payload: a `type` field decides which fields are required. The parser branches on `type` and then applies a refinement step to ensure business rules are met.

```csharp
// See Samples/ParseySharp.SampleWeb/Checkout.cs for a complete version
var paymentParser =
  (from paymentMethodType in Parse.Enum<PaymentMethodType>().At("type", [])
   from pm in paymentMethodType switch
   {
     PaymentMethodType.Card => (
         Parse.As<string>().At("number", []),
         Parse.Int32Flex().At("cvv", [])
       ).Apply((number, cvv) => (PaymentMethod)new Card(number, cvv)).As(),
     PaymentMethodType.Ach => (
         Parse.As<string>().At("routingNumber", []),
         Parse.As<string>().At("accountNumber", [])
       ).Apply((routingNumber, accountNumber) => (PaymentMethod)new Ach(routingNumber, accountNumber)).As()
   }
   select pm)
  .As()
  .Filter(x => Refine.Create<PaymentMethod, ValidPayment>(x).Map(v => new ValidPayment(v)));
```

This uses C# LINQ query syntax over `Parse<T>` to express dependent parsing (the second `from` depends on the first’s value) and `Refine` to enforce domain invariants. The same parser runs over JSON, XML, YAML, Protobuf, MessagePack, Avro, etc.

## Recipe index

- Optional fields: `.Option()`
- Alternatives: `p1.OrElse(p2)`
- Validation: `.Filter(pred => Left("why"))`
- Arrays and streaming: `parser.Seq()`, streaming variants
- Combine fields into a record: tuple + `.Apply(...)`
- Normalize types: string→int via `.Filter` or `OrElse`

See `ParseySharp.Tests/` and `Samples/` for working recipes.

- Default primitives for flexible, carrier-friendly parsing: `ParseySharp/DefaultParsers.cs`
- End-to-end example used throughout this README: `Samples/ParseySharp.SampleWeb/Checkout.cs`

## ASP.NET Core in two lines

Minimal API:

```csharp
app.MapParsedPost("/payment", paymentParser, (ValidPayment x) => Results.Ok(x))
   .AcceptsJson().AcceptsXml().AcceptsFormUrlEncoded();
```

MVC:

```csharp
[HttpPost("/mvc/payment")]
[AcceptsJson][AcceptsXml][AcceptsFormUrlEncoded]
public Task<IActionResult> Post(CancellationToken ct)
  => this.ParsedAsync(paymentParser, x => Ok(x), ct);
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

## Extending to new formats (4 functions)

To add a new carrier, you only need to supply four functions to a `ParsePathNav<TCarrier>`:

- `Prop` (drill into a carrier by name)
- `Index` (drill into a carrier by array index)
- `Unbox` (extract a primitive value from a carrier)
- `Clone` (clone or otherwise produce an owned value for safe iteration/yielding)

Here’s a concrete illustration using a familiar type, `System.Text.Json.JsonElement`.

```csharp
using System.Text.Json;

public static class ParsePathNavJson
{
  public static readonly ParsePathNav<JsonElement> Json = new(
    Prop: (node, name) =>
      node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var child)
        ? Right<Unknown<JsonElement>, Option<JsonElement>>(Optional(child))
        : node.ValueKind == JsonValueKind.Object
          ? Right<Unknown<JsonElement>, Option<JsonElement>>(None)
          : Left<Unknown<JsonElement>, Option<JsonElement>>(Unknown.New(node)),

    Index: (node, i) =>
      node.ValueKind == JsonValueKind.Array && i >= 0 && i < node.GetArrayLength()
        ? Right<Unknown<JsonElement>, Option<JsonElement>>(Optional(node[i]))
        : node.ValueKind == JsonValueKind.Array
          ? Right<Unknown<JsonElement>, Option<JsonElement>>(None)
          : Left<Unknown<JsonElement>, Option<JsonElement>>(Unknown.New(node)),

    Unbox: node => node.ValueKind switch
    {
      JsonValueKind.Null   => Right<Unknown<JsonElement>, Unknown<object>>(new Unknown<object>.None()),
      JsonValueKind.String => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(node.GetString()!)),
      JsonValueKind.True   => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(true)),
      JsonValueKind.False  => Right<Unknown<JsonElement>, Unknown<object>>(Unknown.New<object>(false)),
      // handle other cases
      _ => Left<Unknown<JsonElement>, Unknown<object>>(Unknown.New(node))
    },

    Clone: node =>
      node.ValueKind == JsonValueKind.Undefined
        ? JsonDocument.Parse("null").RootElement.Clone()
        : node.Clone()
  );
}

// Using your custom navigator with any parser:
//   var result = parser.RunWithNav(ParsePathNavJson.Json)(jsonElement);
```

See a production-grade example in `ParseySharp.Avro/Navigate.Avro.cs`, which defines a navigator over Avro generic values by implementing these four functions.
