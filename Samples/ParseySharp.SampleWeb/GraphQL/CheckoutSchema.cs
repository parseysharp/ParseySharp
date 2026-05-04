namespace ParseySharp.SampleWeb.GraphQL;

using global::HotChocolate;
using global::OneOf;
using global::HotChocolate.Types;
using global::ParseySharp.HotChocolate;
using global::ParseySharp.Refine;

public sealed record PaymentMethodInput(
    PaymentMethodType Type,
    string? Number,
    int? Cvv,
    string? RoutingNumber,
    string? AccountNumber
);

public sealed record PostalAddressInput(string Line1, string City, string Country, string Postal);

public sealed record ItemInput(string Sku, int Quantity, decimal UnitPrice);

public sealed record CheckoutInput(
    string CustomerEmail,
    PaymentMethodInput PaymentMethod,
    IReadOnlyList<ItemInput> Items,
    PostalAddressInput? ShippingAddress
);

public sealed record CheckoutResult(
    bool Accepted,
    string PaymentMethodKind,
    int ItemsCount,
    decimal Amount
);

public sealed record CheckoutSuccess(string PaymentMethodKind, int ItemsCount, decimal Amount);

public sealed record CheckoutIssue(
    IReadOnlyList<string> Path,
    string Message,
    string Expected,
    string? Actual
);

public sealed record ValidationError(IReadOnlyList<CheckoutIssue> Issues);

public sealed record NotFoundError(string Resource, string Id);

public sealed record ConflictError(string Reason);

public sealed record CheckoutOutcome(
    Either<OneOf<NotFoundError, ConflictError, ValidationError>, CheckoutSuccess> Value
);

public sealed record PercentDiscount(decimal Percent);

public sealed record FixedDiscount(decimal Amount);

public sealed record SeasonalDiscount(OneOf<PercentDiscount, FixedDiscount> Value);

public sealed record DiscountInput(OneOf<PercentDiscount, FixedDiscount, SeasonalDiscount> Value);

/// <summary>
/// Parsers for the <c>@oneOf</c> input demo. Each level of the schema mirrors
/// the CLR shape exactly: <c>DiscountInput.value</c> selects one arm; that arm
/// (when itself a record around a DU, such as <see cref="SeasonalDiscount"/>)
/// surfaces its own <c>value</c> field which the parser drills through with
/// <c>.At("value")</c>. No flattening, no central dispatcher.
/// </summary>
public static class Discount
{
    public static readonly Parse<PercentDiscount> PercentDiscountParser = Parse
        .DecimalFlex()
        .At("percent")
        .Map(p => new PercentDiscount(p))
        .As();

    public static readonly Parse<FixedDiscount> FixedDiscountParser = Parse
        .DecimalFlex()
        .At("amount")
        .Map(a => new FixedDiscount(a))
        .As();

    public static readonly Parse<OneOf<PercentDiscount, FixedDiscount>> SeasonalInnerParser =
        PercentDiscountParser
            .At("percentDiscount")
            .Map(p => OneOf<PercentDiscount, FixedDiscount>.FromT0(p))
            .As()
            .OrElse(
                FixedDiscountParser
                    .At("fixedDiscount")
                    .Map(f => OneOf<PercentDiscount, FixedDiscount>.FromT1(f))
                    .As()
            );

    public static readonly Parse<SeasonalDiscount> SeasonalDiscountParser = SeasonalInnerParser
        .At("value")
        .Map(o => new SeasonalDiscount(o))
        .As();

    public static readonly Parse<DiscountInput> DiscountInputParser = PercentDiscountParser
        .At("percentDiscount")
        .Map(p => OneOf<PercentDiscount, FixedDiscount, SeasonalDiscount>.FromT0(p))
        .As()
        .OrElse(
            FixedDiscountParser
                .At("fixedDiscount")
                .Map(f => OneOf<PercentDiscount, FixedDiscount, SeasonalDiscount>.FromT1(f))
                .As()
        )
        .OrElse(
            SeasonalDiscountParser
                .At("seasonalDiscount")
                .Map(s => OneOf<PercentDiscount, FixedDiscount, SeasonalDiscount>.FromT2(s))
                .As()
        )
        .At("value")
        .Map(o => new DiscountInput(o))
        .As();
}

public sealed class GraphQLQuery
{
    public string Ping() => "pong";

    public IReadOnlyList<CheckoutOutcome> RecentOutcomes() =>
        new CheckoutOutcome[]
        {
            new(
                Right<OneOf<NotFoundError, ConflictError, ValidationError>, CheckoutSuccess>(
                    new CheckoutSuccess("card", 3, 199.95m)
                )
            ),
            new(
                Left<OneOf<NotFoundError, ConflictError, ValidationError>, CheckoutSuccess>(
                    OneOf<NotFoundError, ConflictError, ValidationError>.FromT0(
                        new NotFoundError("Item", "sku-404")
                    )
                )
            ),
            new(
                Left<OneOf<NotFoundError, ConflictError, ValidationError>, CheckoutSuccess>(
                    OneOf<NotFoundError, ConflictError, ValidationError>.FromT1(
                        new ConflictError("inventory locked")
                    )
                )
            ),
        };
}

public sealed class QueryType : ObjectType<GraphQLQuery>
{
    protected override void Configure(IObjectTypeDescriptor<GraphQLQuery> descriptor)
    {
        descriptor
            .Field("quoteDiscount")
            .ResolveParsedArgument<DiscountInput, string>(
                "input",
                Discount.DiscountInputParser,
                onSuccess: input => new ValueTask<string>(Describe(input)),
                onFailure: errs => new ValueTask<string>(
                    "errors: " + string.Join("; ", errs.Map(e => e.Message))
                )
            );
    }

    static string Describe(DiscountInput input) =>
        input.Value.Match(
            p => $"percent {p.Percent}",
            f => $"fixed {f.Amount}",
            s => "seasonal " + s.Value.Match(p => $"percent {p.Percent}", f => $"fixed {f.Amount}")
        );
}

public sealed class Mutation { }

public sealed class MutationType : ObjectType<Mutation>
{
    protected override void Configure(IObjectTypeDescriptor<Mutation> descriptor)
    {
        descriptor
            .Field("checkout")
            .ResolveParsedArgument(
                "input",
                a => a.Type<NonNullType<InputObjectType<CheckoutInput>>>(),
                Checkout.CheckoutParser,
                checkout => new ValueTask<CheckoutOutcome>(
                    new CheckoutOutcome(
                        Right<
                            OneOf<NotFoundError, ConflictError, ValidationError>,
                            CheckoutSuccess
                        >(
                            new CheckoutSuccess(
                                PaymentMethodKind: checkout
                                    .Value()
                                    .PaymentMethod.Value()
                                    .Match(Card: _ => "card", Ach: _ => "ach"),
                                ItemsCount: checkout.Value().Items.Sum(x => x.Value().Quantity),
                                Amount: checkout.Value().Total
                            )
                        )
                    )
                )
            );

        descriptor
            .Field("checkoutOutcome")
            .ResolveParsedArgument<ValidCheckout, CheckoutOutcome>(
                "input",
                a => a.Type<NonNullType<InputObjectType<CheckoutInput>>>(),
                Checkout.CheckoutParser,
                onSuccess: checkout => new ValueTask<CheckoutOutcome>(
                    new CheckoutOutcome(
                        Right<
                            OneOf<NotFoundError, ConflictError, ValidationError>,
                            CheckoutSuccess
                        >(
                            new CheckoutSuccess(
                                PaymentMethodKind: checkout
                                    .Value()
                                    .PaymentMethod.Value()
                                    .Match(Card: _ => "card", Ach: _ => "ach"),
                                ItemsCount: checkout.Value().Items.Sum(x => x.Value().Quantity),
                                Amount: checkout.Value().Total
                            )
                        )
                    )
                ),
                onFailure: errs => new ValueTask<CheckoutOutcome>(
                    new CheckoutOutcome(
                        Left<OneOf<NotFoundError, ConflictError, ValidationError>, CheckoutSuccess>(
                            OneOf<NotFoundError, ConflictError, ValidationError>.FromT2(
                                new ValidationError(
                                    Issues: errs.Map(e => new CheckoutIssue(
                                            Path: e.Path.ToArray(),
                                            Message: e.Message,
                                            Expected: e.Expected,
                                            Actual: e.Actual?.ToString()
                                        ))
                                        .ToArray()
                                )
                            )
                        )
                    )
                )
            );
    }
}
