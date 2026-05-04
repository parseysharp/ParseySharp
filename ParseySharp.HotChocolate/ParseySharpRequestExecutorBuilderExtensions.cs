using HotChocolate.Execution.Configuration;
using ParseySharp.HotChocolate;

namespace Microsoft.Extensions.DependencyInjection;

public static class ParseySharpRequestExecutorBuilderExtensions
{
    /// <summary>
    /// Wires ParseySharp into a Hot Chocolate request executor builder:
    /// registers the default <see cref="IGraphQLErrorMapper"/>, captures
    /// <see cref="ParseySharpHotChocolateOptions"/>, and installs the
    /// markerless discriminated-union type interceptors that auto-project
    /// <c>Either&lt;,&gt;</c>/<c>OneOf&lt;,..&gt;</c> (and single-field wrapper
    /// records around them) to GraphQL union output types and <c>@oneOf</c>
    /// input object types.
    /// </summary>
    public static IRequestExecutorBuilder AddParseySharpHotChocolate(
        this IRequestExecutorBuilder builder,
        Action<ParseySharpHotChocolateOptions>? configure = null)
    {
        var options = new ParseySharpHotChocolateOptions();
        configure?.Invoke(options);

        builder.Services.TryAddSingleton<IGraphQLErrorMapper, DefaultGraphQLErrorMapper>();
        builder.Services.TryAddSingleton(options);

        return builder
            .TryAddTypeInterceptor<DuOutputTypeInterceptor>(_ => new DuOutputTypeInterceptor(options))
            .TryAddTypeInterceptor<DuInputTypeInterceptor>(_ => new DuInputTypeInterceptor(options));
    }
}
