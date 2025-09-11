using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions_Protobuf
{
  public static IServiceCollection AddParseySharpProtobuf(this IServiceCollection services)
  {
    services.TryAddEnumerable(ServiceDescriptor.Singleton<ParseySharp.AspNetCore.IContentHandler>(new ParseySharp.AspNetCore.ProtobufContentHandler()));
    return services;
  }
}
