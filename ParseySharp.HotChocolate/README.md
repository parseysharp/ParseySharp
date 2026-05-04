# ParseySharp.HotChocolate

Parse Hot Chocolate GraphQL field arguments with ParseySharp. Structural failures and domain refinements accumulate into `ParsePathErr`s and surface through Hot Chocolate's native error channel, with GraphQL-native paths (index segments become integers, name segments stay as names).

## Install

- NuGet packages to install:
  - ParseySharp.HotChocolate
  - HotChocolate.AspNetCore

Commands:

```bash
dotnet add package ParseySharp.HotChocolate
dotnet add package HotChocolate.AspNetCore
```

## Wire up in `Program.cs`

```csharp
using ParseySharp.HotChocolate;

var builder = WebApplication.CreateBuilder(args);

builder.Services
  .AddGraphQLServer()
  .AddParseySharpHotChocolate()
  .AddQueryType<GraphQLQuery>()
  .AddMutationType<CheckoutMutationType>();

var app = builder.Build();
app.MapGraphQL("/graphql");
app.Run();
```

`AddParseySharpHotChocolate()` does three things:

1. Registers a default `IGraphQLErrorMapper` → `DefaultGraphQLErrorMapper` singleton (replaceable; see "Customize error mapping" below).
2. Captures a `ParseySharpHotChocolateOptions` singleton (used for uninhabited-type registration; see "Discriminated unions" below).
3. Installs two Hot Chocolate `TypeInterceptor`s — one for output unions, one for `@oneOf` inputs — that structurally detect discriminated unions in field/argument types and project them into the GraphQL type system. No marker types, no inheritance, no attribute on your domain.

The optional configure delegate lets you tune options:

```csharp
.AddParseySharpHotChocolate(o => o.RegisterUninhabited<MyVoid>())
```

`MyVoid` is whatever marker type you've defined to mean "no inhabitant"; register it once at startup and the decomposer prunes it from every DU it walks. See "Pruning uninhabited types" below.

## Resolve a field with a `Parse<T>`

```csharp
using ParseySharp;
using ParseySharp.HotChocolate;
using HotChocolate.Types;

public sealed class QueryType : ObjectType<GraphQLQuery>
{
  protected override void Configure(IObjectTypeDescriptor<GraphQLQuery> d) =>
    d.Field("quoteDiscount")
      .ResolveParsedArgument<DiscountInput, string>(
        "input",
        Discount.DiscountInputParser,
        input => new ValueTask<string>(Describe(input)));
}
```

`ResolveParsedArgument` is one call that declares the argument's GraphQL schema *and* wires the parser-driven resolver. The argument name appears once. Two flavors:

- **Auto-binding** (above): when the parser's output type **is** the wire format, drop the descriptor lambda. The bridge sends `typeof(T)` through the same CLR-type inference Hot Chocolate uses for method-bound parameters — modifiers (lists, nullables), nested records, and DU positions at any depth all come out right via the per-configuration field walk. This is the common case for self-owned APIs where you control the schema and the parsing target.

- **Descriptor-bound**: when the wire format diverges from the parser output (you parse into a refined domain type, the schema accepts a DTO), pass an argument descriptor that pins the wire shape and let the parser project the AST onto your domain type:

  ```csharp
  d.Field("checkoutOutcome")
    .ResolveParsedArgument<ValidCheckout, CheckoutOutcome>(
      "input",
      a => a.Type<NonNullType<InputObjectType<CheckoutInput>>>(),
      Checkout.CheckoutParser,
      onSuccess: c => new ValueTask<CheckoutOutcome>(...),
      onFailure: errs => new ValueTask<CheckoutOutcome>(...));
  ```

  Here the schema is `CheckoutInput`, but the parser produces `ValidCheckout`.

The auto-binding path's only blind spot is **scalar `T`** (`string`, `int`, custom scalar) — Hot Chocolate's CLR inference will try to build an input object from a scalar CLR type and produce nonsense. For scalar arguments, use the descriptor-bound overload with `a.Type<NonNullType<StringType>>()` (or whichever scalar applies).

If you'd rather declare the argument and the resolver at separate sites, the `.Argument(...).ResolveParsed(...)` chain is still available; `ResolveParsedArgument` is just a convenience.

`ResolveParsed`/`ResolveParsedArgument` are catamorphisms over `Validation<Seq<ParsePathErr>, T>` — a fold with one arm per outcome. Each variant (auto-bound, descriptor-bound, no-binding) has four overloads letting you vary whether you want the resolver context and whether you want to own the failure branch:

```csharp
// Auto-binding — wire format inferred from typeof(T).
ResolveParsedArgument<T, TResult>(string name, Parse<T> parser, /* success + optional failure, with or without ctx */);

// Descriptor-bound — wire format declared explicitly (diverges from T).
ResolveParsedArgument<T, TResult>(string name, Action<IArgumentDescriptor> argument, Parse<T> parser, /* same shape */);

// No binding — argument declared elsewhere (e.g. method-bound parameter).
ResolveParsed<T, TResult>(string name, Parse<T> parser, /* same shape */);
```

When the failure handler is omitted, the convenience overload reports parse errors via `IResolverContext.ReportParseErrors(Seq<ParsePathErr>)` and returns `default(TResult)` — pair it with a nullable field type.

Use a context-bearing overload when the handler needs DI (`ctx.Service<T>()`, `ctx.RequestServices`), cancellation (`ctx.RequestAborted`), or any HC context.

### Errors → native error channel

The no-failure-arm overloads delegate failure to `IResolverContext.ReportParseErrors(Seq<ParsePathErr>)` — an extension that asks the registered `IGraphQLErrorMapper` for `IError`s and calls `context.ReportError(...)` for each. The resolver then returns `default(TResult)`.

This is the right choice when you want parse errors to appear in the top-level `errors[]` array and `data` to stay `null` for that field. **Declare the field as nullable** to avoid HC's `HC0018 "Cannot return null for non-nullable field"` masking your errors.

A failing request surfaces each error with its GraphQL-native path:

```json
{
  "errors": [
    {
      "message": "Email must contain @",
      "path": ["checkout", "customerEmail"],
      "extensions": { "expected": "String", "actual": "Some(not-an-email)" }
    },
    {
      "message": "SKU is invalid",
      "path": ["checkout", "items", 0],
      "extensions": { "expected": "Item", "actual": "Some(Item { Sku = , Quantity = 0, UnitPrice = 0 })" }
    }
  ],
  "data": { "checkout": null }
}
```

Notes:

- `items.0` is an integer in the GraphQL response path, not the string `"[0]"`. The mapper converts ParseySharp's bracketed index segments (`[N]`) into GraphQL `Path.Append(int)` calls; non-numeric segments go through `Path.Append(string)`.
- `expected` and `actual` come straight from `ParsePathErr` and are exposed on `error.extensions`.

### Failure as data

When you want to keep a non-null field type, report failures as *data* instead of errors, or map parse errors into your own result/union type, use the overload that owns the failure arm:

```csharp
d.Field("checkoutOutcome")
  .ResolveParsedArgument<ValidCheckout, CheckoutOutcome>(
    "input",
    a => a.Type<NonNullType<InputObjectType<CheckoutInput>>>(),
    Checkout.CheckoutParser,
    onSuccess: checkout => new ValueTask<CheckoutOutcome>(
      new CheckoutOutcome(
        Right<OneOf<NotFoundError, ConflictError, ValidationError>, CheckoutSuccess>(
          new CheckoutSuccess(...)))),
    onFailure: errs => new ValueTask<CheckoutOutcome>(
      new CheckoutOutcome(
        Left<OneOf<NotFoundError, ConflictError, ValidationError>, CheckoutSuccess>(
          OneOf<NotFoundError, ConflictError, ValidationError>.FromT2(
            new ValidationError(errs.Map(e => new CheckoutIssue(
              Path:     e.Path.ToArray(),
              Message:  e.Message,
              Expected: e.Expected,
              Actual:   e.Actual?.ToString())).ToArray()))))));
```

No `errors[]` array, no `HC0018`, and the failure is part of the schema's response shape. If you want both — data-shaped failure *and* a GraphQL-channel error for observability — call `ctx.ReportParseErrors(errs)` inside `onFailure` before returning.

## Discriminated unions

A field whose result is one of several distinct shapes is a GraphQL union; an argument that accepts one of several distinct shapes is a GraphQL `@oneOf` input object. ParseySharp's interceptors discover both *structurally* — there is no marker type, no inheritance, no attribute on your domain. The shape vocabulary the interceptors recognize:

- `Either<L, R>` — two arms.
- `OneOf<T0..Tn>` (from the `OneOf` library) — `n` arms.

`Validation<L, R>` is **not** in the set: it's an effect type (a parser/validation outcome), not a domain sum, and it falls through to Hot Chocolate's default handling. Plain records around DUs are also **not** treated specially — the field walk one level deeper catches the inner DU on its own.

### Where projections happen

Anywhere a field's CLR result/argument-type tree has a recognized DU position — top-level, inside a list, behind a `Nullable<>`, etc. — the interceptor synthesizes a projected schema type and rewrites the field's reference to point at it. List/non-null structure is preserved.

- **Output**: a `UnionType` whose members are the (transitively) flattened arms. A result formatter on the same field unwraps `Either`/`OneOf` instances at runtime so HC can dispatch `__typename` and child fields against the leaf object.
- **Input**: an `InputObjectType` carrying the `@oneOf` directive (one field per immediate arm, mutually exclusive at request time per the GraphQL spec). Input projection does **not** flatten through nested DUs; each nested DU position is caught one level deeper as its own `@oneOf`.

### Naming

Names are derived from the *position* the projection appears in, not from the DU's CLR identity:

- **At a field of an object/input-object type** — `<OwningTypeName><PascalCaseFieldName>`.
  ```csharp
  public sealed record CheckoutOutcome(
    Either<OneOf<NotFoundError, ConflictError, ValidationError>, CheckoutSuccess> Value);

  // Output schema:
  // type CheckoutOutcome { value: CheckoutOutcomeValue! }
  // union CheckoutOutcomeValue = NotFoundError | ConflictError | ValidationError | CheckoutSuccess
  ```

- **At an argument** — `<FieldName><PascalCaseArgumentName>`.
  ```csharp
  public string Echo(OneOf<A, B> input) => ...;

  // Schema:
  // type Query { echo(input: EchoInput!): String! }
  // input EchoInput @oneOf { a: A, b: B }
  ```

- **Without parent context** (rare; nested DU arm inside a synthesized `@oneOf`, or top-level fallback) — output uses an alphabetical `Or` join (`AOrBOrC`); input uses a `OneOf` prefix + alphabetical concat (`OneOfABC`). The fallback only fires when no field/argument name is available — in practice you'll only see it for nested bare-DU arms.

To override a derived name, declare an explicit `InputObjectType<X>` / `ObjectType<X>` / `UnionType` subclass at that position with `descriptor.Name("…")` — the bridge respects whatever name you set.

### Schema/CLR symmetry

Schemas mirror the CLR shape one-for-one. A record around a DU keeps its property in the schema — the parser drills through it with `.At("propertyName")` exactly as the schema reads:

```csharp
public sealed record DiscountInput(OneOf<PercentDiscount, FixedDiscount, SeasonalDiscount> Value);
public sealed record SeasonalDiscount(OneOf<PercentDiscount, FixedDiscount> Value);

// Schema:
// input DiscountInput { value: DiscountInputValue! }
// input DiscountInputValue @oneOf {
//   percentDiscount: PercentDiscountInput
//   fixedDiscount:   FixedDiscountInput
//   seasonalDiscount: SeasonalDiscountInput
// }
// input SeasonalDiscountInput { value: SeasonalDiscountInputValue! }
// input SeasonalDiscountInputValue @oneOf {
//   percentDiscount: PercentDiscountInput
//   fixedDiscount:   FixedDiscountInput
// }
```

Parser composition mirrors the schema one-for-one — `.At("value")` at every record/wrapper level, then per-arm `.At(...)` to discriminate within a `@oneOf`:

```csharp
public static readonly Parse<DiscountInput> DiscountInputParser =
  PercentDiscountParser.At("percentDiscount").Map(p => OneOf<PercentDiscount, FixedDiscount, SeasonalDiscount>.FromT0(p)).As()
    .OrElse(FixedDiscountParser.At("fixedDiscount").Map(f => OneOf<PercentDiscount, FixedDiscount, SeasonalDiscount>.FromT1(f)).As())
    .OrElse(SeasonalDiscountParser.At("seasonalDiscount").Map(s => OneOf<PercentDiscount, FixedDiscount, SeasonalDiscount>.FromT2(s)).As())
    .At("value")
    .Map(o => new DiscountInput(o)).As();
```

### Querying a union

```graphql
mutation CheckoutOutcome($input: CheckoutInput!) {
  checkoutOutcome(input: $input) {
    value {
      __typename
      ... on CheckoutSuccess { paymentMethodKind itemsCount amount }
      ... on ValidationError { issues { path message expected actual } }
      ... on NotFoundError   { resource id }
      ... on ConflictError   { reason }
    }
  }
}
```

### Pruning uninhabited types

Register types that, by construction, never inhabit a sum position. The decomposer drops them from both unions and `@oneOf` inputs. The bridge ships with no defaults — define your own marker (a closed type with no public constructors is conventional) and register it once at startup:

```csharp
public sealed record MyVoid { private MyVoid() { } }

builder.Services
  .AddGraphQLServer()
  .AddParseySharpHotChocolate(o => o.RegisterUninhabited<MyVoid>());
```

After that, every `Either<MyVoid, R>` / `OneOf<…, MyVoid, …>` arm is treated as if `MyVoid` weren't there.

Three pruning outcomes (output side):

- **N ≥ 2 inhabited arms** — register a union with those members.
- **1 inhabited arm** — collapse: the field's schema type becomes that single leaf's `ObjectType<TLeaf>` directly, no wrapper union. `Either<MyVoid, R>` becomes `R` in the schema.
- **0 inhabited arms** — `SchemaException` at schema build.

### Duplicate arms fail at schema build

GraphQL union members must be distinct object types; `@oneOf` input fields must be distinct names. If decomposition yields the same CLR type twice (for example, `Either<NotFound, NotFound>`), schema construction raises `HotChocolate.SchemaException` at startup, naming the duplicate. The remedy is to introduce distinct domain types that carry the disambiguation at the type level.

### How the projection works

Schema-time and runtime share one type-classification vocabulary (`TypeShape` — a closed sum of `Either`, `OneOf`, `Nullable`, `Array`, `Enumerable`, `Leaf`). Both the decomposer (schema) and the projector (runtime value walker) are folds over `TypeShape` parameterized on a strategy: `Deep` for output unions, `Shallow` for `@oneOf` inputs. Naming is parameterized on a parallel `IDuSchemaNaming` typeclass with `OutputNaming` and `InputNaming` instances. Adding a shape or a side requires adding it in exactly one place — there is no parallel walk to keep aligned.

## Customize error mapping

Replace the default registration with your own `IGraphQLErrorMapper`:

```csharp
public sealed class MyMapper : IGraphQLErrorMapper
{
  public IReadOnlyList<IError> ToErrors(IResolverContext ctx, Seq<ParsePathErr> errors) =>
    errors.Map(e =>
      ErrorBuilder.New()
        .SetMessage(e.Message)
        .SetCode("VALIDATION")
        .SetPath(ctx.Path) // or compose with e.Path as you wish
        .Build())
    .ToArray();
}

builder.Services.AddSingleton<IGraphQLErrorMapper, MyMapper>();
builder.Services.AddGraphQLServer().AddParseySharpHotChocolate()...;
```

`AddParseySharpHotChocolate()` uses `TryAddSingleton`, so any registration you add before or after wins.

## Sample project

`Samples/ParseySharp.SampleWeb` runs the server on `http://localhost:5099/graphql` (Nitro UI at the same URL). Ready-to-paste queries live alongside the existing REST samples:

- `graphql.ping.graphql`
- `graphql.checkout.graphql` (convenience overload, nullable result) + `graphql.checkout.valid.variables.json` / `graphql.checkout.invalid.variables.json`
- `graphql.checkoutOutcome.graphql` (full fold, auto-discovered DU result) — reuses the same variables files
- `graphql.quoteDiscount.graphql` — a multi-aliased query against `quoteDiscount(input: DiscountInput!)` exercising every arm of the nested `@oneOf` input (`percentDiscount`, `fixedDiscount`, and the recursive `seasonalDiscount.value.{percentDiscount|fixedDiscount}`)

Paste the query into the operation pane and (where applicable) the variables JSON into the variables pane. The `checkout` invalid variant exercises email, SKU, quantity, unit-price, and CVV refinements simultaneously and surfaces every error in the GraphQL `errors[]` array. The `checkoutOutcome` variant surfaces the same failure as data — `__typename: "ValidationError"` plus the `issues` collection — through the auto-discovered `CheckoutOutcomeValue` union under the `value` field.

## Notes

- Variables are substituted by Hot Chocolate before your resolver runs; parsing operates on a concrete `IValueNode`.
- Input scalar coercion and schema validation are Hot Chocolate's responsibility. ParseySharp only runs once HC hands you a schema-valid AST literal.
- The navigator lives at `ParsePathNavHotChocolate.HotChocolate` and the extension `parser.ParseHotChocolate()` returns `Func<IValueNode, Validation<Seq<ParsePathErr>, A>>` if you want to parse an `IValueNode` outside a resolver.
