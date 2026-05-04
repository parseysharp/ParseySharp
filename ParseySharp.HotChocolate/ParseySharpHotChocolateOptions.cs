namespace ParseySharp.HotChocolate;

/// <summary>
/// Configures ParseySharp's Hot Chocolate integration. Used by the markerless
/// discriminated-union type interceptors to prune uninhabited arms from
/// projected union member sets and <c>@oneOf</c> input fields.
/// </summary>
public sealed class ParseySharpHotChocolateOptions
{
    readonly System.Collections.Generic.HashSet<Type> _uninhabited = new();

    /// <summary>
    /// Marks <typeparamref name="T"/> as uninhabited. Any occurrence of
    /// <typeparamref name="T"/> in a discriminated-union arm is pruned
    /// from the resulting GraphQL union members and <c>@oneOf</c> fields.
    /// </summary>
    public ParseySharpHotChocolateOptions RegisterUninhabited<T>() =>
        RegisterUninhabited(typeof(T));

    /// <summary>
    /// Marks <paramref name="t"/> as uninhabited. See <see cref="RegisterUninhabited{T}()"/>.
    /// </summary>
    public ParseySharpHotChocolateOptions RegisterUninhabited(Type t)
    {
        _uninhabited.Add(t);
        return this;
    }

    internal IReadOnlySet<Type> UninhabitedTypes => _uninhabited;
}
