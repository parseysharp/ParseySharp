namespace ParseySharp.HotChocolate;

public interface IGraphQLErrorMapper
{
    IReadOnlyList<IError> ToErrors(IResolverContext ctx, Seq<ParsePathErr> errors);
}

public sealed class DefaultGraphQLErrorMapper : IGraphQLErrorMapper
{
    public IReadOnlyList<IError> ToErrors(IResolverContext ctx, Seq<ParsePathErr> errors) =>
        errors.Map(e =>
            ErrorBuilder.New()
                .SetMessage(e.Message)
                .SetPath(AppendSegments(ctx.Path, e.Path))
                .SetExtension("expected", e.Expected)
                .SetExtension("actual", e.Actual)
                .Build())
        .ToArray();

    static global::HotChocolate.Path AppendSegments(global::HotChocolate.Path basePath, Seq<string> path) =>
        path.Fold(basePath, (acc, seg) =>
            (seg.Length >= 2 && seg[0] == '[' && seg[^1] == ']'
                && int.TryParse(seg.AsSpan(1, seg.Length - 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                ? acc.Append(i)
                : acc.Append(seg));
}
