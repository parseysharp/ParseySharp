using System.Text.Json.Serialization;
using ParseySharp;

namespace ParseySharp.SampleWeb;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "paymentMethod")]
[JsonDerivedType(typeof(Card),   "card")]
[JsonDerivedType(typeof(Account), "account")]
public abstract record PaymentMethod;
public sealed record Card(string number, int cvv) : PaymentMethod;
public sealed record Account(string routingNumber, string accountNumber) : PaymentMethod;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "transactionType")]
[JsonDerivedType(typeof(Debit), "debit")]
[JsonDerivedType(typeof(Credit), "credit")]
public abstract record Transaction;
public sealed record Debit(PaymentMethod paymentMethod, decimal amount, DateTimeOffset timestamp) : Transaction;
public sealed record Credit(PaymentMethod paymentMethod, decimal amount, DateTimeOffset timestamp) : Transaction;

public enum PaymentMethodType { Card, Account }

public static class PaymentMethodTypeExtensions
{
  public static A Match<A>(this PaymentMethodType paymentMethodType, Func<A> Card, Func<A> Account)
    => paymentMethodType switch
    {
      PaymentMethodType.Card => Card(),
      PaymentMethodType.Account => Account(),
      _ => throw new Exception("Impossible")
    };
}
public enum TransactionType  { Debit, Credit }

public static class TransactionTypeExtensions
{
  public static A Match<A>(this TransactionType transactionType, Func<A> Debit, Func<A> Credit)
    => transactionType switch
    {
      TransactionType.Debit => Debit(),
      TransactionType.Credit => Credit(),
      _ => throw new Exception("Impossible")
    };
}

public static class Transactions
{
  public static readonly Parse<PaymentMethod> PaymentMethodParser =
    (from paymentMethodType in Parse.Enum<PaymentMethodType>().At("paymentMethodType", [])
     from pm in paymentMethodType.Match(
       Card: () => (
           Parse.As<string>().At("number", []),
           Parse.As<int>().At("cvv", [])
         ).Apply((number, cvv) => (PaymentMethod)new Card(number, cvv)).As(),
       Account: () => (
           Parse.As<string>().At("routingNumber", []),
           Parse.As<string>().At("accountNumber", [])
         ).Apply((routingNumber, accountNumber) => (PaymentMethod)new Account(routingNumber, accountNumber)).As()
     )
     select pm)
    .As();

  public static readonly Parse<Transaction> TransactionParser =
    (PaymentMethodParser.At("paymentMethod", []),
     Parse.DecimalFlex().At("amount", []),
     Parse.DateTimeOffsetFlex().At("timestamp", []),
     Parse.Enum<TransactionType>().At("transactionType", [])
     ).Apply(
      (pm, amount, timestamp, transactionType) =>
      transactionType.Match<Transaction>(
        Credit: () => new Credit(pm, amount, timestamp),
        Debit: () => new Debit(pm, amount, timestamp)
      )
     ).As();
}