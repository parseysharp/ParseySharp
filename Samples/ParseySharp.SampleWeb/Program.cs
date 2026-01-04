using Microsoft.Extensions.Options;
using ParseySharp.AspNetCore;
using ParseySharp.SampleWeb;
using ParseySharp.Swashbuckle;
using ParseySharp.Refine;
using ParseySharp;

var builder = WebApplication.CreateBuilder(args);

// Register ParseySharp ASP.NET core with JSON handler
builder.Services
  .AddEndpointsApiExplorer()
  .AddSwaggerGen(c =>
  {
    c.AddParseySharpDefaults();
  })
  .AddParseySharpCore()
  .AddParseySharpMessagePack()
  .AddParseySharpProtobuf()
  .AddParseySharpMultipart();

// MVC controllers for /mvc routes (mirrors the Minimal API endpoints)
builder.Services
  .AddControllers()
  .AddParseySharpMvc();

// Parse Seq<ValidCheckout> from configuration section "checkoutSeed" into options
builder.Services
  .AddOptions<ParsedOpts<Seq<ValidCheckout>>>()
  .ParseWith(builder.Configuration,
    Checkout.CheckoutParser.Seq().Map(xs => new ParsedOpts<Seq<ValidCheckout>>(xs)).As(),
    "checkoutSeed");

builder.Services
  .AddOptions<MySettings>()
  .ParseWith(builder.Configuration,
    (
      Parse.BoolFlex().At("enabled"),
      Parse.As<string>().At("endpoint"),
      Parse.Int32Flex().At("retries")
    ).Apply((enabled, endpoint, retries) => new MySettings(enabled, endpoint, retries)).As(),
    "mySettings");

builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });

var app = builder.Build();

// Swagger UI for trying the endpoints and inspecting RequestType
app.UseSwagger();
app.UseSwaggerUI();

// Map MVC controllers
app.MapControllers();

var seed = app.Services.GetRequiredService<IOptions<ParsedOpts<Seq<ValidCheckout>>>>();

app.MapGet("/checkout-seed", () =>
  Results.Ok(seed.Value.Value.Map(x => x.Serialize()).ToList()));

app.MapParsedPost("/checkout",
  Checkout.CheckoutParser, checkout => 
Task.FromResult<IResult>(
  Results.Ok(new { 
    accepted = true,
    paymentMethod = checkout.Value().PaymentMethod.Value().Match<object>(
      Card: card => new { type = "card", last4 = card.Number[^4..] },
      Ach: ach => new { type = "ach", routingLast4 = ach.RoutingNumber[^4..], accountLast4 = ach.AccountNumber[^4..] }),
    itemsCount = checkout.Value().Items.Sum(x => x.Value().Quantity),
    amount = checkout.Value().Total
  })))
.AcceptsJson()
.AcceptsXml()
.AcceptsMessagePack()
.AcceptsProtobuf()
.SetRequestModel<CheckoutDoc>();

app.MapParsedPost("/checkout-history",
  Checkout.CheckoutHistoryParser.Parser, checkouts =>
Task.FromResult<IResult>(
  Results.Ok(new
  {
    accepted = true,
    total = checkouts.Length,
    byMethod = checkouts.GroupBy(x => x.Value().PaymentMethod.Value().Match<object>(
      Card: _ => "card",
      Ach: _ => "ach"
    )).ToDictionary(g => g.Key, g => g.Count()),
    sumAmount = checkouts.Sum(x => x.Value().Total),
  })))
.AcceptsMultipart()
.SetRequestModel<CheckoutHistoryDoc>();

app.MapParsedGet("/echo-card-type",
  Parse.Enum<PaymentMethodType>().At("method"),
  method => Task.FromResult(Results.Ok(new { method = method.ToString() }))
).AcceptsQueryString().SetRequestModel<EchoCardTypeRequest>();

app.Run();

public record EchoCardTypeRequest(PaymentMethodType Method);