namespace ParseySharp.SampleWeb;

using System.ComponentModel.DataAnnotations;
using ParseySharp.AspNetCore;
using ParseySharp.Refine;

public readonly record struct Email(Refine.Refined<string, Email> Inner): Refine.IRefine<string, Email>
{
    public static Seq<string> Errors(string x) => 
      "Email must contain @".ErrUnless(() => x.Contains("@")) +
      "Email must be non-empty".ErrUnless(() => x.Trim().Length > 0) +
      "Email must contain a domain".ErrUnless(() => x.Contains("@") && x.Split("@").Last().Length > 0);

    public static Either<Seq<string>, Email> Refined(string x) => Refine.Create<string, Email>(x).Map<Email>(x => new(x));
}

public abstract record PaymentMethod
{
  public T Match<T>(Func<Card, T> Card, Func<Ach, T> Ach) =>
    this switch
    {
      Card card => Card(card),
      Ach ach => Ach(ach),
      _ => throw new Exception("Impossible")
    };
}
public sealed record Card(string Number, int Cvv) : PaymentMethod;
public sealed record Ach(string RoutingNumber, string AccountNumber) : PaymentMethod;

public readonly record struct ValidPayment(Refine.Refined<PaymentMethod, ValidPayment> Inner): Refine.IRefine<PaymentMethod, ValidPayment>
{
  public static CreditCardAttribute CheckCC = new();

  public static Seq<string> Errors(PaymentMethod x) =>
    x.Match(
      card =>
        "Credit card number is invalid".ErrUnless(() => CheckCC.IsValid(card.Number)) +
        "CVV is invalid".ErrUnless(() => 
          (card.Number.StartsWith("34") || card.Number.StartsWith("37"))
            ? card.Cvv.ToString().Length == 4
            : card.Cvv.ToString().Length == 3),
      ach =>
        "ACH routing number is invalid".ErrUnless(() => IsValidAbaRouting(ach.RoutingNumber)) +
        "ACH account number is invalid".ErrUnless(() => ach.AccountNumber.Length > 0)
    );
  
  static bool IsValidAbaRouting(string? rn)
  {
    if (string.IsNullOrWhiteSpace(rn) || rn.Length != 9 || !rn.All(char.IsDigit)) return false;
    int d1 = rn[0]-'0', d2 = rn[1]-'0', d3 = rn[2]-'0', d4 = rn[3]-'0', d5 = rn[4]-'0',
      d6 = rn[5]-'0', d7 = rn[6]-'0', d8 = rn[7]-'0', d9 = rn[8]-'0';
    int sum = 3*(d1+d4+d7) + 7*(d2+d5+d8) + (d3+d6+d9);
    return sum % 10 == 0;
  }

  public static Either<Seq<string>, ValidPayment> Refined(PaymentMethod x) =>
    Refine.Create<PaymentMethod, ValidPayment>(x).Map<ValidPayment>(x => new(x));
}

public enum PaymentMethodType { Card, Ach }

public static class PaymentMethodTypeExtensions
{
  public static A Match<A>(this PaymentMethodType paymentMethodType, Func<A> Card, Func<A> Ach)
    => paymentMethodType switch
    {
      PaymentMethodType.Card => Card(),
      PaymentMethodType.Ach => Ach(),
      _ => throw new Exception("Impossible")
    };
}

public sealed record Item(string Sku, int Quantity, decimal UnitPrice);

public readonly record struct ValidItem(Refine.Refined<Item, ValidItem> Inner): Refine.IRefine<Item, ValidItem>
{
  public static Seq<string> Errors(Item x) =>
    "SKU is invalid".ErrUnless(() => x.Sku.Length > 0) +
    "Quantity is invalid".ErrUnless(() => x.Quantity > 0) +
    "Unit price is invalid".ErrUnless(() => x.UnitPrice > 0);

  public static Either<Seq<string>, ValidItem> Refined(Item x) =>
    Refine.Create<Item, ValidItem>(x).Map<ValidItem>(x => new(x));
}

public sealed record PostalAddress(string Line1, string City, string Country, string Postal);

public sealed record CheckoutRequest(
  Email CustomerEmail,
  ValidPayment PaymentMethod,
  decimal Total,
  Seq<ValidItem> Items,
  Option<PostalAddress> ShippingAddress
);

public readonly record struct ValidCheckout(Refine.Refined<CheckoutRequest, ValidCheckout> Inner): Refine.IRefine<CheckoutRequest, ValidCheckout>
{
  public static Seq<string> Errors(CheckoutRequest x) => 
    "Shipping address required on orders over $100".ErrWhen(() => 
      x.Total > 100m && x.ShippingAddress.IsNone) +
    "ACH orders must be under $25,000".ErrWhen(() => 
      x.PaymentMethod.Value().Match(
        Card: _ => false,
        Ach: _ => x.Total > 25000m
      )).ToSeq();

  public static Either<Seq<string>, ValidCheckout> Refined(CheckoutRequest x) =>
    Refine.Create<CheckoutRequest, ValidCheckout>(x).Map<ValidCheckout>(x => new(x));
}

public static class Checkout
{
  public static readonly Parse<ValidPayment> PaymentMethodParser =
    (from paymentMethodType in Parse.Enum<PaymentMethodType>().At("type")
     from pm in paymentMethodType.Match(
       Card: () => (
           Parse.As<string>().At("number"),
           Parse.Int32Flex().At("cvv")
         ).Apply((number, cvv) => (PaymentMethod)new Card(number, cvv)).As(),
       Ach: () => (
           Parse.As<string>().At("routingNumber"),
           Parse.As<string>().At("accountNumber")
         ).Apply((routingNumber, accountNumber) => (PaymentMethod)new Ach(routingNumber, accountNumber)).As()
     )
     select pm)
    .As().Filter(x =>
      Refine.Create<PaymentMethod, ValidPayment>(x).Map(x => new ValidPayment(x)));

  public static readonly Parse<ValidItem> ItemParser =
    (
      Parse.As<string>().At("sku"),
      Parse.Int32Flex().At("quantity"),
      Parse.DecimalFlex().At("unitPrice")
    ).Apply((sku, quantity, unitPrice) => new Item(sku, quantity, unitPrice))
    .As().Filter(ValidItem.Refined);

  public static readonly Parse<Option<PostalAddress>> PostalAddressParser =
    (
      Parse.As<string>().At("line1"),
      Parse.As<string>().At("city"),
      Parse.As<string>().At("country"),
      Parse.As<string>().At("postal")
    ).Apply((line1, city, country, postal) => new PostalAddress(line1, city, country, postal)).As().Option();

  public static readonly Parse<ValidCheckout> CheckoutParser =
    (
      Parse.As<string>()
        .Filter(Email.Refined)
        .At("customerEmail"),
      PaymentMethodParser.At("paymentMethod"),
      ItemParser.Seq().At("items"),
      PostalAddressParser.At("shippingAddress")
    ).Apply((customerEmail, paymentMethod, items, shippingAddress) =>
      new CheckoutRequest(
        customerEmail,
        paymentMethod,
        items.Sum(x => x.Value().Quantity * x.Value().UnitPrice),
        items,
        shippingAddress))
    .As().Filter(ValidCheckout.Refined);

  public static readonly FileWithFormatSpec<Seq<ValidCheckout>> CheckoutHistoryParser = FileWithFormat.BuildEager<ValidCheckout>(
    prefix: "history",
    formats: Seq(
      AcceptFileFormat.Ndjson<ValidCheckout>(),
      AcceptFileFormat.XmlRows<ValidCheckout>(),
      AcceptFileFormatYaml.YamlRows<ValidCheckout>(),
      AcceptFileFormatAvro.Avro<ValidCheckout>()
    ),
    shape: Checkout.CheckoutParser)
  .RegisterDocFormats(typeof(CheckoutHistoryDocFormats));
}

// Doc-only type to describe the payment request shape (type-discriminated)
public sealed record PaymentMethodDoc(PaymentMethodType type, string? number, int? cvv, string? routingNumber, string? accountNumber);
public sealed record CheckoutDoc(string customerEmail, PaymentMethodDoc paymentMethod, Seq<Item> items, PostalAddress? shippingAddress);
public sealed record CheckoutHistoryDocFormats { };
public sealed record CheckoutHistoryDoc(
  FileUpload<AnyFormat, CheckoutDoc> historyFile,
  FormatName<CheckoutHistoryDocFormats> historyFormat
);