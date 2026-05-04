namespace ParseySharp.HotChocolate;

/// <summary>
/// Bare-arm naming convention for a discriminated-union position with no
/// parent context. When a DU is discovered at a field position (object field,
/// input-object field, or argument), the schema name is derived from the
/// parent's type name + the field/argument name regardless of side; only the
/// no-parent fallback is side-specific. Implementations consume the
/// alphabetically ordered arm types and emit a schema name.
/// </summary>
internal interface IDuSchemaNaming<TSelf>
    where TSelf : IDuSchemaNaming<TSelf>
{
    static abstract string Bare(IReadOnlyList<Type> arms);
}

/// <summary>Output unions: alphabetical join with <c>Or</c> (<c>AOrBOrC</c>).</summary>
internal readonly struct OutputNaming : IDuSchemaNaming<OutputNaming>
{
    public static string Bare(IReadOnlyList<Type> arms) =>
        string.Join("Or", arms.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
}

/// <summary>Input <c>@oneOf</c> objects: <c>OneOf</c> prefix + alphabetical concat (<c>OneOfABC</c>).</summary>
internal readonly struct InputNaming : IDuSchemaNaming<InputNaming>
{
    public static string Bare(IReadOnlyList<Type> arms) =>
        "OneOf" + string.Concat(arms.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
}

internal static class DuTypeNaming
{
    /// <summary>
    /// Schema name for a discovered DU position. When parent context is provided
    /// — owning type name + field/argument name — the name is derived from it
    /// (<c>&lt;Parent&gt;&lt;PascalField&gt;</c>) regardless of side. Without parent
    /// context, falls back to the side-specific bare convention from
    /// <typeparamref name="TNaming"/>.
    /// </summary>
    public static string NameFor<TNaming>(
        string? parentTypeName,
        string? fieldName,
        IReadOnlyList<Type> arms)
        where TNaming : IDuSchemaNaming<TNaming> =>
        parentTypeName is { } p && fieldName is { } f
            ? Capitalize(p) + Capitalize(f)
            : TNaming.Bare(arms);

    static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) || char.IsUpper(s[0])
            ? s
            : char.ToUpperInvariant(s[0]) + s[1..];
}
