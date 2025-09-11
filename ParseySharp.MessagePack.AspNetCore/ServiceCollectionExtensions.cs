using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions_MessagePack
{
  public static IServiceCollection AddParseySharpMessagePack(this IServiceCollection services)
  {
    services.TryAddEnumerable(ServiceDescriptor.Singleton<ParseySharp.AspNetCore.IContentHandler>(new ParseySharp.AspNetCore.MessagePackContentHandler()));
    return services;
  }
}
