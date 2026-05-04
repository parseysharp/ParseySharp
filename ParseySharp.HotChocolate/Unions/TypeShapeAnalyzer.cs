using HotChocolate.Types.Descriptors.Configurations;

namespace ParseySharp.HotChocolate;

/// <summary>
/// A discriminated-union position discovered in a Hot Chocolate type reference.
/// <see cref="DuRoot"/> is the underlying <c>Either&lt;,&gt;</c> or <c>OneOf&lt;,..&gt;</c>
/// CLR type. <see cref="ClrShape"/> is the field's full host CLR type with
/// list/array/<see cref="Nullable{T}"/> wrappers preserved for the runtime walker.
/// </summary>
internal sealed record DuShape(Type DuRoot, Type ClrShape);

internal static class TypeShapeAnalyzer
{
    /// <summary>
    /// Walks <paramref name="typeRef"/>'s <see cref="IExtendedType.ElementType"/> chain
    /// to the innermost CLR leaf and returns <see cref="Some{A}(A)"/> a <see cref="DuShape"/>
    /// when that leaf classifies as a bare DU position (directly or under a
    /// <see cref="Nullable{T}"/>); otherwise <see cref="None"/>. Records around DUs
    /// are no longer treated as DU positions — the field walk that catches their
    /// containing scope catches the inner DU on the next level.
    /// </summary>
    public static Option<DuShape> Analyze(TypeReference? typeRef) =>
        typeRef is ExtendedTypeReference ext
            ? ScanLeaf(Innermost(ext.Type).Type) is { } du
                ? Some(du with { ClrShape = ext.Type.Source })
                : None
            : None;

    static IExtendedType Innermost(IExtendedType ext) =>
        ext.ElementType is { } inner ? Innermost(inner) : ext;

    static DuShape? ScanLeaf(Type t) =>
        TypeShapeClassifier.Classify(t).Match<DuShape?>(
            _ => new DuShape(DuRoot: t, ClrShape: t),
            _ => new DuShape(DuRoot: t, ClrShape: t),
            n => ScanLeaf(n.Underlying),
            _ => null,
            _ => null,
            _ => null);
}
