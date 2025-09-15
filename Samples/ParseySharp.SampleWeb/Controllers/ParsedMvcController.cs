using Microsoft.AspNetCore.Mvc;
using ParseySharp.AspNetCore;
using ParseySharp.Refine;

namespace ParseySharp.SampleWeb.Controllers;
[ApiController]
[Route("mvc")] // keep endpoints under /mvc to avoid colliding with Minimal API samples
public sealed class ParsedMvcController : ControllerBase
{
  public static readonly FileWithFormatSpec<Seq<ValidCheckout>> fileWithFormatSpec = Checkout.CheckoutHistoryParser;

  [HttpPost("checkout-history")]
    [AcceptsMultipart]
    [RequestModel<CheckoutHistoryDoc>]
    public Task<IActionResult> PostCheckoutHistory(CancellationToken ct)
      => this.ParsedAsync(
        fileWithFormatSpec.Parser,
        checkouts => Ok(new { 
          accepted = true,
          total = checkouts.Length,
          byMethod = checkouts.GroupBy(x => x.Value().PaymentMethod.Value().Match<object>(
            Card: _ => "card",
            Ach: _ => "ach"
          )).ToDictionary(g => g.Key, g => g.Count()),
          sumAmount = checkouts.Sum(x => x.Value().Total),
          }),
        ct);

    [HttpPost("checkout")]
    [AcceptsJson]
    [AcceptsXml]
    [AcceptsMessagePack]
    [AcceptsProtobuf]
    [RequestModel<CheckoutDoc>]
    public Task<IActionResult> PostPayment(CancellationToken ct)
      => this.ParsedAsync(
        Checkout.CheckoutParser,
        checkout => Ok(new { 
          accepted = true,
          paymentMethod = checkout.Value().PaymentMethod.Value().Match<object>(
            Card: card => new { type = "card", last4 = card.Number[^4..] },
            Ach: ach => new { type = "ach", routingLast4 = ach.RoutingNumber[^4..], accountLast4 = ach.AccountNumber[^4..] }),
          itemsCount = checkout.Value().Items.Sum(x => x.Value().Quantity),
          amount = checkout.Value().Total
        }), ct);



}