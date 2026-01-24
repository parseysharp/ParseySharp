using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;

namespace ParseySharp.Swashbuckle;

public sealed class EnumMemberNamesSchemaFilter : ISchemaFilter
{
  public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
  {
    if (schema is not OpenApiSchema openApiSchema) return;

    var t = Nullable.GetUnderlyingType(context.Type) ?? context.Type;
    if (!t.IsEnum) return;

    openApiSchema.AllOf = null;
    openApiSchema.OneOf = null;
    openApiSchema.AnyOf = null;
    openApiSchema.Type = JsonSchemaType.String;
    openApiSchema.Format = null;

    var names = Enum.GetNames(t);
    openApiSchema.Enum = names.Select(n => (JsonNode)JsonValue.Create(n)!).ToList();

    if (openApiSchema.Default is JsonValue defVal && (defVal.TryGetValue<int>(out _) || defVal.TryGetValue<long>(out _) || defVal.TryGetValue<double>(out _)))
      openApiSchema.Default = JsonValue.Create(names.First());
    if (openApiSchema.Example is null || (openApiSchema.Example is JsonValue exVal && (exVal.TryGetValue<int>(out _) || exVal.TryGetValue<long>(out _) || exVal.TryGetValue<double>(out _))))
      openApiSchema.Example = JsonValue.Create(names.First());
  }
}
