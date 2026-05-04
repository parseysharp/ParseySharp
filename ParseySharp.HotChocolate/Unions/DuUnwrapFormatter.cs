namespace ParseySharp.HotChocolate;

/// <summary>
/// Builds a Hot Chocolate <see cref="ResultFormatterDelegate"/> that walks a
/// resolver result down to its leaf instance via <see cref="Projector.BuildShapeWalker"/>,
/// preserving list/array/<see cref="Nullable{T}"/> structure and unwrapping
/// single-field records and nested <c>Either</c>/<c>OneOf</c> layers.
/// </summary>
internal static class DuUnwrapFormatter
{
    public static ResultFormatterDelegate For(Type clrShape)
    {
        var walker = Projector.BuildShapeWalker(clrShape);
        return (_, result) => walker(result);
    }
}
