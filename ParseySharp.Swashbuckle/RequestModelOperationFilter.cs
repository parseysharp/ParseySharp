using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

// Uses the existing RequestModelMetadata added by SetRequestModel<T>()
// - If an operation has a requestBody, set its schema to the declared model
// - If there is no requestBody (e.g., GET), emit query parameters from the model's top-level properties
public sealed class RequestModelOperationFilter : IOperationFilter
{
  public void Apply(OpenApiOperation operation, OperationFilterContext context)
  {
    var normalized = IsMvc(context)
      ? NormalizeMvc(operation, context)
      : NormalizeMinimal(operation, context);

    if (normalized is null) return;

    if (normalized.Mode == Mode.Query)
    {
      ApplyQuery(operation, context, normalized);
      return;
    }

    ApplyBody(operation, normalized, context);
  }

  private static (IDictionary<string, OpenApiSchema> props, ISet<string>? required)
    ResolveProperties(OpenApiSchema schema, SchemaRepository repo)
  {
    // Unwrap $ref
    if (schema.Reference is not null && repo.Schemas.TryGetValue(schema.Reference.Id, out var target))
      return (target.Properties, target.Required is null ? null : new HashSet<string>(target.Required));

    // Unwrap allOf single inheritance pattern (common for records)
    if (schema.AllOf != null && schema.AllOf.Count > 0)
    {
      foreach (var s in schema.AllOf)
      {
        var (props, req) = ResolveProperties(s, repo);
        if (props.Count > 0)
          return (props, req);
      }
    }

    return (schema.Properties, schema.Required is null ? null : new HashSet<string>(schema.Required));
  }

  private enum Mode { Query, Body }

  private sealed record Normalized(Mode Mode, Type RequestType, OpenApiSchema Schema, IReadOnlyList<string> MediaTypes);

  private static bool IsMvc(OperationFilterContext ctx)
    => ctx.ApiDescription.ActionDescriptor is ControllerActionDescriptor;

  private static Normalized? NormalizeMinimal(OpenApiOperation op, OperationFilterContext ctx)
  {
    var reqType = GetRequestTypeFromMetadata(ctx);
    if (reqType is null) return null;
    var schema = ctx.SchemaGenerator.GenerateSchema(reqType, ctx.SchemaRepository);
    var isGet = string.Equals(ctx.ApiDescription.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
    if (isGet) return new Normalized(Mode.Query, reqType, schema, Array.Empty<string>());

    // Minimal API: best-effort media discovery
    var medias = ctx.ApiDescription.SupportedRequestFormats
      .Select(f => f.MediaType)
      .Where(mt => !string.IsNullOrWhiteSpace(mt))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .DefaultIfEmpty("application/json")
      .ToArray();
    return new Normalized(Mode.Body, reqType, schema, medias);
  }

  private static Normalized? NormalizeMvc(OpenApiOperation op, OperationFilterContext ctx)
  {
    var reqType = GetRequestTypeFromMetadata(ctx);
    if (reqType is null) return null;
    var schema = ctx.SchemaGenerator.GenerateSchema(reqType, ctx.SchemaRepository);
    var isGet = string.Equals(ctx.ApiDescription.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
    if (isGet) return new Normalized(Mode.Query, reqType, schema, Array.Empty<string>());

    var medias = ctx.ApiDescription.SupportedRequestFormats
      .Select(f => f.MediaType)
      .Where(mt => !string.IsNullOrWhiteSpace(mt))
      .ToList();

    if (medias.Count == 0)
    {
      var consumes = ctx.ApiDescription.ActionDescriptor.FilterDescriptors
        ?.Select(fd => fd.Filter)
        ?.OfType<Microsoft.AspNetCore.Mvc.ConsumesAttribute>()
        ?.SelectMany(ca => ca.ContentTypes)
        ?.Where(mt => !string.IsNullOrWhiteSpace(mt))
        ?.ToList() ?? new List<string>();
      medias.AddRange(consumes);
    }

    var mediaArr = medias.Distinct(StringComparer.OrdinalIgnoreCase)
                         .DefaultIfEmpty("application/json").ToArray();
    return new Normalized(Mode.Body, reqType, schema, mediaArr);
  }

  private static Type? GetRequestTypeFromMetadata(OperationFilterContext ctx)
  {
    var endpointMetadata = ctx.ApiDescription.ActionDescriptor.EndpointMetadata;
    var reqMeta = endpointMetadata?
      .OfType<ParseySharp.AspNetCore.AcceptsExtensions.RequestModelMetadata>()
      .LastOrDefault();
    return reqMeta?.RequestType;
  }

  private static void ApplyQuery(OpenApiOperation operation, OperationFilterContext ctx, Normalized n)
  {
    operation.RequestBody = null;
    operation.Parameters ??= new List<OpenApiParameter>();

    var (props, requiredSet) = ResolveProperties(n.Schema, ctx.SchemaRepository);
    if (props.Count == 0)
    {
      foreach (var p in n.RequestType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
      {
        if (!p.CanRead) continue;
        var propSchema = ctx.SchemaGenerator.GenerateSchema(p.PropertyType, ctx.SchemaRepository);
        operation.Parameters.Add(new OpenApiParameter
        {
          Name = p.Name,
          In = ParameterLocation.Query,
          Required = false,
          Schema = propSchema
        });
      }
      return;
    }

    foreach (var kv in props)
    {
      var name = kv.Key;
      var propSchema = kv.Value;
      var required = requiredSet != null && requiredSet.Contains(name);
      operation.Parameters.Add(new OpenApiParameter
      {
        Name = name,
        In = ParameterLocation.Query,
        Required = required,
        Schema = propSchema
      });
    }
  }

  private static void ApplyBody(OpenApiOperation operation, Normalized n, OperationFilterContext context)
  {
    operation.RequestBody ??= new OpenApiRequestBody { Required = true, Content = new Dictionary<string, OpenApiMediaType>() };
    foreach (var mt in n.MediaTypes)
    {
      if (!operation.RequestBody.Content.TryGetValue(mt, out var content))
      {
        content = new OpenApiMediaType();
        operation.RequestBody.Content[mt] = content;
      }
      if (string.Equals(mt, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
      {
        // Build a multipart-aware schema that renders file chooser(s) and collect doc info
        var (mpSchema, encodings, fileParts, otherParts) = BuildMultipartSchema(n.RequestType, n.Schema);
        content.Schema = mpSchema;
        if (encodings.Count > 0)
          content.Encoding = encodings;

        // Append a concise description of file parts for human readers
        if (fileParts.Count > 0)
        {
          var sb = new System.Text.StringBuilder();
          sb.AppendLine();
          sb.AppendLine("Multipart parts:");
          foreach (var p in fileParts)
          {
            var ct = string.IsNullOrWhiteSpace(p.ContentType) ? "application/octet-stream" : p.ContentType;
            var arr = p.IsArray ? "[]" : string.Empty;
            if (p.ModelType is null)
            {
              sb.AppendLine($"- {p.Name}{arr} ({ct})");
            }
            else
            {
              var shape = RenderTypeShape(p.ModelType, context);
              sb.AppendLine($"- {p.Name}{arr} ({ct}), rows shape: {shape}");
            }
          }
          operation.Description = string.IsNullOrWhiteSpace(operation.Description)
            ? sb.ToString()
            : operation.Description + "\n\n" + sb.ToString();
        }
      }
      else
      {
        // Other media types: use the normalized schema directly
        content.Schema = n.Schema;
      }
    }
  }

  private static bool IsIFormFileType(Type t)
    => typeof(IFormFile).IsAssignableFrom(t);

  private static bool IsEnumerableOfIFormFile(Type t)
  {
    if (t.IsArray) return IsIFormFileType(t.GetElementType()!);
    if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return false;
    var ienumT = t.IsGenericType ? t.GetGenericArguments().FirstOrDefault() : null;
    return ienumT is not null && IsIFormFileType(ienumT);
  }

  private static bool IsFileUploadMarker(Type t)
    => t == typeof(ParseySharp.AspNetCore.FileUpload)
       || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ParseySharp.AspNetCore.FileUpload<>))
       || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ParseySharp.AspNetCore.FileUpload<,>));

  private sealed record FilePartDoc(string Name, string ContentType, Type? ModelType, bool IsArray);

  private static (OpenApiSchema mpSchema, IDictionary<string, OpenApiEncoding> encodings, List<FilePartDoc> parts, List<string> otherParts)
    BuildMultipartSchema(Type requestType, OpenApiSchema fallback)
  {
    var schema = new OpenApiSchema { Type = "object", Properties = new Dictionary<string, OpenApiSchema>() };
    var enc = new Dictionary<string, OpenApiEncoding>(StringComparer.OrdinalIgnoreCase);
    var parts = new List<FilePartDoc>();
    var others = new List<string>();

    var props = requestType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
      .Where(p => p.CanRead);

    foreach (var p in props)
    {
      var name = p.Name;
      var t = p.PropertyType;

      if (IsIFormFileType(t))
      {
        schema.Properties[name] = new OpenApiSchema { Type = "string", Format = "binary" };
        enc[name] = new OpenApiEncoding { ContentType = "application/octet-stream" };
        parts.Add(new FilePartDoc(name, "application/octet-stream", null, false));
        continue;
      }

      if (IsEnumerableOfIFormFile(t))
      {
        schema.Properties[name] = new OpenApiSchema
        {
          Type = "array",
          Items = new OpenApiSchema { Type = "string", Format = "binary" }
        };
        enc[name] = new OpenApiEncoding { ContentType = "application/octet-stream" };
        parts.Add(new FilePartDoc(name, "application/octet-stream", null, true));
        continue;
      }

      if (IsFileUploadMarker(t))
      {
        // Default file schema
        var fileSchema = new OpenApiSchema { Type = "string", Format = "binary" };

        // If format is provided via FileUpload<TFormat,...>, prefer that for encoding content type
        string? contentType = null;
        Type? modelType = null;

        if (t.IsGenericType)
        {
          var args = t.GetGenericArguments();
          if (t.GetGenericTypeDefinition() == typeof(ParseySharp.AspNetCore.FileUpload<>))
          {
            var fmt = args[0];
            try { contentType = ((ParseySharp.AspNetCore.IFileFormat)Activator.CreateInstance(fmt)!).ContentType; } catch { }
          }
          else if (t.GetGenericTypeDefinition() == typeof(ParseySharp.AspNetCore.FileUpload<,>))
          {
            var fmt = args[0];
            modelType = args[1];
            try { contentType = ((ParseySharp.AspNetCore.IFileFormat)Activator.CreateInstance(fmt)!).ContentType; } catch { }
          }
        }

        schema.Properties[name] = fileSchema;
        enc[name] = new OpenApiEncoding { ContentType = contentType ?? "application/octet-stream" };

        // Vendor extension to hint inner model (if present)
        if (modelType is not null)
        {
          fileSchema.Extensions = fileSchema.Extensions ?? new Dictionary<string, IOpenApiExtension>();
          fileSchema.Extensions["x-fileModel"] = new OpenApiString(modelType.Name);
        }
        parts.Add(new FilePartDoc(name, contentType ?? "application/octet-stream", modelType, false));
        continue;
      }

      // Fallback: leave as simple string for multipart; detailed schema is available in other content types
      schema.Properties[name] = new OpenApiSchema { Type = "string" };
      others.Add(name);
    }

    // If we didn't detect any properties, fall back to provided schema
    if (schema.Properties.Count == 0)
      return (fallback, enc, parts, others);

    return (schema, enc, parts, others);
  }

  // Renders a compact field list for a record/class using the generated OpenAPI schema
  private static string RenderTypeShape(Type t, OperationFilterContext ctx)
  {
    try
    {
      var s = ctx.SchemaGenerator.GenerateSchema(t, ctx.SchemaRepository);
      var (props, required) = ResolveProperties(s, ctx.SchemaRepository);
      if (props.Count == 0)
        return t.Name;

      string Map(OpenApiSchema ps)
      {
        if (ps.Type == "array" && ps.Items is not null)
        {
          var inner = Map(ps.Items);
          return inner + "[]";
        }
        if (!string.IsNullOrWhiteSpace(ps.Type))
        {
          return ps.Format switch
          {
            "int32" => "int",
            "int64" => "long",
            _ => ps.Type
          };
        }
        // Fallback when type is unset (refs/objects)
        return "object";
      }

      var parts = props.Select(kv =>
      {
        var name = kv.Key;
        var schema = kv.Value;
        var req = required != null && required.Contains(name);
        var ty = Map(schema);
        var nullable = req ? string.Empty : "?";
        return $"{name}: {ty}{nullable}";
      });
      return string.Join(", ", parts);
    }
    catch
    {
      return t.Name;
    }
  }
}
