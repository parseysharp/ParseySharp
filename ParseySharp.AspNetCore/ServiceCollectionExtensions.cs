using ParseySharp.AspNetCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddParseySharpCore(this IServiceCollection services, Action<ParseySharpOptions>? configure = null)
  {
    // Build options eagerly to avoid runtime activation surprises
    var opts = new ParseySharpOptions();
    configure?.Invoke(opts);
    services.AddSingleton(opts);

    services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentHandler>(new QueryStringContentHandler()));
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentHandler>(new JsonContentHandler()));
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentHandler>(new XmlContentHandler()));
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentHandler>(new FormUrlEncodedContentHandler()));
    services.TryAddSingleton<IParseBinder, ParseBinder>();
    services.TryAddSingleton<IProblemMapper, DefaultProblemMapper>();
    return services;
  }

  // Opt-in registration for multipart/form-data (file-aware) handler
  public static IServiceCollection AddParseySharpMultipart(this IServiceCollection services)
  {
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentHandler>(new MultipartFormDataContentHandler()));
    return services;
  }
}
