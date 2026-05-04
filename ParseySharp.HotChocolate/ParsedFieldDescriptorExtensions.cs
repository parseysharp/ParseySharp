namespace ParseySharp.HotChocolate;

public static class ParsedFieldDescriptorExtensions
{
    public static IObjectFieldDescriptor ResolveParsed<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Parse<T> parser,
        Func<IResolverContext, T, ValueTask<TResult>> onSuccess,
        Func<IResolverContext, Seq<ParsePathErr>, ValueTask<TResult>> onFailure
    ) =>
        field.Resolve(
            new FieldResolverDelegate(async ctx =>
                await parser
                    .ParseHotChocolate()
                    .Invoke(ctx.ArgumentLiteral<IValueNode>(argumentName))
                    .Match<ValueTask<TResult>>(
                        Fail: errs => onFailure(ctx, errs),
                        Succ: val => onSuccess(ctx, val)
                    )
            ),
            typeof(NamedRuntimeType<TResult>)
        );

    public static IObjectFieldDescriptor ResolveParsed<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Parse<T> parser,
        Func<T, ValueTask<TResult>> onSuccess,
        Func<Seq<ParsePathErr>, ValueTask<TResult>> onFailure
    ) =>
        field.ResolveParsed(
            argumentName,
            parser,
            (_, v) => onSuccess(v),
            (_, errs) => onFailure(errs)
        );

    public static IObjectFieldDescriptor ResolveParsed<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Parse<T> parser,
        Func<IResolverContext, T, ValueTask<TResult>> onSuccess
    ) =>
        field.ResolveParsed(
            argumentName,
            parser,
            onSuccess,
            (ctx, errs) => ctx.ReportParseErrorsAndDefault<TResult>(errs)
        );

    public static IObjectFieldDescriptor ResolveParsed<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Parse<T> parser,
        Func<T, ValueTask<TResult>> onSuccess
    ) => field.ResolveParsed(argumentName, parser, (_, v) => onSuccess(v));

    /// <summary>
    /// Combo of <see cref="IObjectFieldDescriptor.Argument(string, Action{IArgumentDescriptor})"/>
    /// and <see cref="ResolveParsed{T, TResult}(IObjectFieldDescriptor, string, Parse{T}, Func{IResolverContext, T, ValueTask{TResult}}, Func{IResolverContext, Seq{ParsePathErr}, ValueTask{TResult}})"/>:
    /// declares the argument's GraphQL schema and wires the parser-driven resolver in
    /// one call so the argument name appears once. Use this overload when the
    /// schema's wire format diverges from the parser's output type — pass an
    /// argument descriptor that pins the wire shape; the parser handles the
    /// projection.
    /// </summary>
    public static IObjectFieldDescriptor ResolveParsedArgument<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Action<IArgumentDescriptor> argumentDescriptor,
        Parse<T> parser,
        Func<IResolverContext, T, ValueTask<TResult>> onSuccess,
        Func<IResolverContext, Seq<ParsePathErr>, ValueTask<TResult>> onFailure
    ) =>
        field
            .Argument(argumentName, argumentDescriptor)
            .ResolveParsed(argumentName, parser, onSuccess, onFailure);

    public static IObjectFieldDescriptor ResolveParsedArgument<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Action<IArgumentDescriptor> argumentDescriptor,
        Parse<T> parser,
        Func<T, ValueTask<TResult>> onSuccess,
        Func<Seq<ParsePathErr>, ValueTask<TResult>> onFailure
    ) =>
        field.ResolveParsedArgument(
            argumentName,
            argumentDescriptor,
            parser,
            (_, v) => onSuccess(v),
            (_, errs) => onFailure(errs)
        );

    public static IObjectFieldDescriptor ResolveParsedArgument<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Action<IArgumentDescriptor> argumentDescriptor,
        Parse<T> parser,
        Func<IResolverContext, T, ValueTask<TResult>> onSuccess
    ) =>
        field
            .Argument(argumentName, argumentDescriptor)
            .ResolveParsed(argumentName, parser, onSuccess);

    public static IObjectFieldDescriptor ResolveParsedArgument<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Action<IArgumentDescriptor> argumentDescriptor,
        Parse<T> parser,
        Func<T, ValueTask<TResult>> onSuccess
    ) =>
        field.ResolveParsedArgument(argumentName, argumentDescriptor, parser, (_, v) => onSuccess(v));

    /// <summary>
    /// Like <see cref="ResolveParsedArgument{T, TResult}(IObjectFieldDescriptor, string, Action{IArgumentDescriptor}, Parse{T}, Func{IResolverContext, T, ValueTask{TResult}}, Func{IResolverContext, Seq{ParsePathErr}, ValueTask{TResult}})"/>
    /// but the argument's GraphQL schema is auto-derived from <typeparamref name="T"/> via
    /// <see cref="IArgumentDescriptor.Type(Type)"/> — the same CLR-type-inference
    /// path Hot Chocolate uses for method-bound parameters. Use this when the
    /// parser's output type <em>is</em> the wire format (the common case for
    /// self-owned APIs); the bridge's input interceptor catches DU positions at
    /// any depth and preserves list/nullable structure. The only thing it
    /// can't auto-bind is a scalar <typeparamref name="T"/> (e.g. <c>string</c>,
    /// <c>int</c>) — for those, use the descriptor-bearing overload with
    /// <c>a.Type&lt;StringType&gt;()</c> or similar.
    /// </summary>
    public static IObjectFieldDescriptor ResolveParsedArgument<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Parse<T> parser,
        Func<IResolverContext, T, ValueTask<TResult>> onSuccess,
        Func<IResolverContext, Seq<ParsePathErr>, ValueTask<TResult>> onFailure
    ) =>
        field.ResolveParsedArgument(
            argumentName,
            a => a.Type(typeof(T)),
            parser,
            onSuccess,
            onFailure
        );

    public static IObjectFieldDescriptor ResolveParsedArgument<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Parse<T> parser,
        Func<T, ValueTask<TResult>> onSuccess,
        Func<Seq<ParsePathErr>, ValueTask<TResult>> onFailure
    ) =>
        field.ResolveParsedArgument(
            argumentName,
            a => a.Type(typeof(T)),
            parser,
            onSuccess,
            onFailure
        );

    public static IObjectFieldDescriptor ResolveParsedArgument<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Parse<T> parser,
        Func<IResolverContext, T, ValueTask<TResult>> onSuccess
    ) =>
        field.ResolveParsedArgument(
            argumentName,
            a => a.Type(typeof(T)),
            parser,
            onSuccess
        );

    public static IObjectFieldDescriptor ResolveParsedArgument<T, TResult>(
        this IObjectFieldDescriptor field,
        string argumentName,
        Parse<T> parser,
        Func<T, ValueTask<TResult>> onSuccess
    ) =>
        field.ResolveParsedArgument(
            argumentName,
            a => a.Type(typeof(T)),
            parser,
            onSuccess
        );
}
