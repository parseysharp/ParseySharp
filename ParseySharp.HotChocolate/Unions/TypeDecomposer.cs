namespace ParseySharp.HotChocolate;

/// <summary>
/// Per-shape unfolding of the sum-bearing nodes (<see cref="EitherShape"/>,
/// <see cref="OneOfShape"/>) into the types the caller wants to surface.
/// Non-sum shapes (Nullable / Array / Enumerable / Leaf) are universal and
/// owned by the framework's fold; only the sum-bearing nodes carry strategy-specific
/// behavior. The <paramref name="uninhabited"/> set is threaded so strategies
/// that recurse can prune at every level and strategies that don't recurse can
/// still drop dead arms at the root.
/// </summary>
internal interface IDuTraversal<TSelf>
    where TSelf : IDuTraversal<TSelf>
{
    static abstract IEnumerable<Type> Either(Type host, EitherShape e, IReadOnlySet<Type> uninhabited);
    static abstract IEnumerable<Type> OneOf(Type host, OneOfShape o, IReadOnlySet<Type> uninhabited);
}

/// <summary>
/// Transitive unfold: keep applying the strategy at each sum-bearing arm until
/// every reachable position is a non-sum leaf. Used for output unions, where
/// the schema flattens nested DUs into a single member list.
/// </summary>
internal readonly struct Deep : IDuTraversal<Deep>
{
    public static IEnumerable<Type> Either(Type host, EitherShape e, IReadOnlySet<Type> uninhabited) =>
        TypeDecomposer.Walk<Deep>(e.Left, uninhabited).Concat(TypeDecomposer.Walk<Deep>(e.Right, uninhabited));
    public static IEnumerable<Type> OneOf(Type host, OneOfShape o, IReadOnlySet<Type> uninhabited) =>
        o.Arms.SelectMany(a => TypeDecomposer.Walk<Deep>(a, uninhabited));
}

/// <summary>
/// One-step split: yield the immediate arms at the root, leaving each arm intact
/// as a CLR type so the caller can recursively project nested input objects in
/// their own right. Used for <c>@oneOf</c> input objects.
/// </summary>
internal readonly struct Shallow : IDuTraversal<Shallow>
{
    public static IEnumerable<Type> Either(Type host, EitherShape e, IReadOnlySet<Type> uninhabited) =>
        new[] { e.Left, e.Right }.Where(a => !uninhabited.Contains(a));
    public static IEnumerable<Type> OneOf(Type host, OneOfShape o, IReadOnlySet<Type> uninhabited) =>
        o.Arms.Where(a => !uninhabited.Contains(a));
}

internal static class TypeDecomposer
{
    static readonly IReadOnlySet<Type> Empty = new System.Collections.Generic.HashSet<Type>();

    public static IReadOnlyList<Type> Decompose<TStrategy>(Type root)
        where TStrategy : IDuTraversal<TStrategy> =>
        Decompose<TStrategy>(root, Empty);

    public static IReadOnlyList<Type> Decompose<TStrategy>(Type root, IReadOnlySet<Type> uninhabited)
        where TStrategy : IDuTraversal<TStrategy> =>
        Walk<TStrategy>(root, uninhabited).ToArray();

    public static IReadOnlyList<Type> Duplicates(IReadOnlyList<Type> leaves) =>
        leaves.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();

    internal static IEnumerable<Type> Walk<TStrategy>(Type t, IReadOnlySet<Type> uninhabited)
        where TStrategy : IDuTraversal<TStrategy> =>
        uninhabited.Contains(t)
            ? []
            : TypeShapeClassifier.Classify(t).Match<IEnumerable<Type>>(
                e  => TStrategy.Either(t, e, uninhabited),
                o  => TStrategy.OneOf(t, o, uninhabited),
                n  => Walk<TStrategy>(n.Underlying, uninhabited),
                _  => [t],
                _  => [t],
                l  => [l.Type]);
}
