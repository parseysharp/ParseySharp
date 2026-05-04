using System.Collections;
using System.Reflection;
using OneOf;

namespace ParseySharp.HotChocolate;

internal static class Projector
{
    static readonly MethodInfo EitherFoldOpen =
        typeof(Projector).GetMethod(nameof(EitherFold), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Builds a fold from a value of the given CLR type down to its underlying leaf
    /// instance, by folding <see cref="TypeShapeClassifier.Classify"/> over the
    /// <see cref="TypeShape"/> structure. Shares its shape vocabulary with the
    /// schema-time analyzer; no separate traversal logic.
    /// </summary>
    public static Func<object?, object?> BuildShapeWalker(Type t) =>
        TypeShapeClassifier.Classify(t).Match<Func<object?, object?>>(
            e  => BindEither(e),
            o  => BindOneOf(o),
            n  => BuildShapeWalker(n.Underlying),
            a  => MapWalker(BuildShapeWalker(a.Element)),
            en => MapWalker(BuildShapeWalker(en.Element)),
            _  => static x => x);

    static Func<object?, object?> BindEither(EitherShape e) =>
        (Func<object?, object?>)EitherFoldOpen
            .MakeGenericMethod(e.Left, e.Right)
            .Invoke(null, [BuildShapeWalker(e.Left), BuildShapeWalker(e.Right)])!;

    static Func<object?, object?> BindOneOf(OneOfShape o)
    {
        var arms = o.Arms.Select(BuildShapeWalker).ToArray();
        return raw => raw is IOneOf v && v.Value is { } x ? arms[v.Index](x) : null;
    }

    static Func<object?, object?> MapWalker(Func<object?, object?> elementWalker) =>
        raw => raw is IEnumerable e ? e.Cast<object?>().Select(elementWalker).ToList() : null;

    static Func<object?, object?> EitherFold<L, R>(
        Func<object?, object?> walkL, Func<object?, object?> walkR) =>
        raw => raw is null
            ? null
            : ((Either<L, R>)raw).Match<object?>(
                Right: r => walkR(r),
                Left:  l => walkL(l));
}
