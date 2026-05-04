using System.Collections.Concurrent;
using OneOf;

namespace ParseySharp.HotChocolate;

internal sealed record EitherShape(Type Left, Type Right);
internal sealed record OneOfShape(IReadOnlyList<Type> Arms);
internal sealed record NullableShape(Type Underlying);
internal sealed record ArrayShape(Type Element);
internal sealed record EnumerableShape(Type Element);
internal sealed record LeafShape(Type Type);

/// <summary>
/// Closed-sum classification of a CLR <see cref="System.Type"/>. The single source
/// of truth for the shape vocabulary the bridge recognizes; both the schema-time
/// analyzer/decomposer and the runtime walker fold over <see cref="TypeShape"/>
/// via <c>Match</c>, with exhaustive coverage enforced by the type system.
/// Records around DUs are <em>not</em> recognized as a special wrapper shape —
/// they classify as <see cref="LeafShape"/>, and any nested DU position is
/// caught one level deeper at the field walk.
/// </summary>
internal sealed class TypeShape : OneOfBase<
    EitherShape, OneOfShape, NullableShape, ArrayShape, EnumerableShape, LeafShape>
{
    TypeShape(OneOf<EitherShape, OneOfShape, NullableShape, ArrayShape, EnumerableShape, LeafShape> input) : base(input) { }
    public static implicit operator TypeShape(EitherShape s) => new(s);
    public static implicit operator TypeShape(OneOfShape s) => new(s);
    public static implicit operator TypeShape(NullableShape s) => new(s);
    public static implicit operator TypeShape(ArrayShape s) => new(s);
    public static implicit operator TypeShape(EnumerableShape s) => new(s);
    public static implicit operator TypeShape(LeafShape s) => new(s);
}

internal static class TypeShapeClassifier
{
    static readonly ConcurrentDictionary<Type, TypeShape> Cache = new();

    public static TypeShape Classify(Type t) => Cache.GetOrAdd(t, ClassifyImpl);

    static TypeShape ClassifyImpl(Type t) =>
        Nullable.GetUnderlyingType(t) is { } underlying
            ? (TypeShape)new NullableShape(underlying)
        : t.IsArray
            ? new ArrayShape(t.GetElementType()!)
        : IsEither(t)
            ? new EitherShape(t.GetGenericArguments()[0], t.GetGenericArguments()[1])
        : IsOneOf(t)
            ? new OneOfShape(t.GetGenericArguments())
        : EnumerableElementOrNull(t) is { } element
            ? new EnumerableShape(element)
        : new LeafShape(t);

    public static bool IsRecognizedDu(Type t) => IsEither(t) || IsOneOf(t);

    static bool IsEither(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Either<,>);

    static bool IsOneOf(Type t) =>
        t.IsGenericType && typeof(IOneOf).IsAssignableFrom(t);

    static Type? EnumerableElementOrNull(Type t) =>
        t == typeof(string)
            ? null
            : t.GetInterfaces().Append(t)
                .FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];
}
