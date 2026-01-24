using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ParseySharp.Swashbuckle;

public static class SwaggerGenOptionsExtensions
{
  public static SwaggerGenOptions AddParseySharpDefaults(this SwaggerGenOptions options)
  {
    options.OperationFilter<RequestModelOperationFilter>();
    options.SchemaFilter<EnumMemberNamesSchemaFilter>();
    return options;
  }
}
