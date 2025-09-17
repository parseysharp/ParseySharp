using ParseySharp.AspNetCore;
using ParseySharp.SampleWeb;
using ParseySharp.Swashbuckle;
using ParseySharp.Refine;

var builder = WebApplication.CreateBuilder(args);

// Register ParseySharp ASP.NET core with JSON handler
builder.Services
  .AddEndpointsApiExplorer()
  .AddSwaggerGen(c =>
  {
    c.OperationFilter<RequestModelOperationFilter>();
  })
  .AddParseySharpCore()
  .AddParseySharpMessagePack()
  .AddParseySharpProtobuf()
  .AddParseySharpMultipart();

// MVC controllers for /mvc routes (mirrors the Minimal API endpoints)
builder.Services
  .AddControllers()
  .AddParseySharpMvc();

builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });

var app = builder.Build();

// Swagger UI for trying the endpoints and inspecting RequestType
app.UseSwagger();
app.UseSwaggerUI();

// Map MVC controllers
app.MapControllers();

app.MapParsedPost("/checkout",
  Checkout.CheckoutParser, (ValidCheckout checkout) => 
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
  Checkout.CheckoutHistoryParser.Parser, (Seq<ValidCheckout> checkouts) =>
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

app.Run();