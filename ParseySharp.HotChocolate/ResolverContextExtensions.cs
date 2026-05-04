namespace ParseySharp.HotChocolate;

public static class ResolverContextExtensions
{
    public static void ReportParseErrors(
        this IResolverContext context,
        Seq<ParsePathErr> errors)
    {
        foreach (var err in context.Service<IGraphQLErrorMapper>().ToErrors(context, errors))
            context.ReportError(err);
    }

    /// <summary>
    /// Reports the parse errors via <see cref="ReportParseErrors"/> and returns a
    /// completed <see cref="ValueTask{TResult}"/> wrapping <c>default(TResult)</c>,
    /// in one expression. Used by the convenience <c>ResolveParsed</c> overloads
    /// whose failure arm wants errors in the GraphQL <c>errors[]</c> channel and
    /// a null data slot.
    /// </summary>
    public static ValueTask<TResult> ReportParseErrorsAndDefault<TResult>(
        this IResolverContext context,
        Seq<ParsePathErr> errors)
    {
        context.ReportParseErrors(errors);
        return new ValueTask<TResult>(default(TResult)!);
    }
}
