using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using ParseySharp.AspNetCore;
using System.Text.Json.Nodes;

namespace ParseySharp.Swashbuckle;

// Uses the existing RequestModelMetadata added by SetRequestModel<T>()
// - If an operation has a requestBody, set its schema to the declared model
// - If there is no requestBody (e.g., GET), emit query parameters from the model's top-level properties
public sealed class RequestModelOperationFilter : IOperationFilter
{
  public void Apply(OpenApiOperation operation, OperationFilterContext context)
  {
    var normalized = IsMvc(context)
      ? NormalizeMvc(context)
      : NormalizeMinimal(context);

    if (normalized is null) return;

    if (normalized.Mode == Mode.Query)
    {
      ApplyQuery(operation, context, normalized);
      return;
    }

    ApplyBody(operation, normalized, context);
  }

  private static (IDictionary<string, IOpenApiSchema>? props, ISet<string>? required)
    ResolveProperties(IOpenApiSchema schema, SchemaRepository repo)
  {
    // Unwrap $ref
    if (schema is OpenApiSchemaReference schemaRef && schemaRef.Id != null && repo.Schemas.TryGetValue(schemaRef.Id, out var target))
    {
      if (target is OpenApiSchema targetSchema)
        return (targetSchema.Properties, targetSchema.Required is null ? null : new HashSet<string>(targetSchema.Required));
    }

    if (schema is OpenApiSchema openApiSchema)
    {
      // Unwrap allOf single inheritance pattern (common for records)
      if (openApiSchema.AllOf != null && openApiSchema.AllOf.Count > 0)
      {
        foreach (var s in openApiSchema.AllOf)
        {
          var (props, req) = ResolveProperties(s, repo);
          if (props != null && props.Count > 0)
            return (props, req);
        }
      }

      return (openApiSchema.Properties, openApiSchema.Required is null ? null : new HashSet<string>(openApiSchema.Required));
    }

    return (null, null);
  }

  private enum Mode { Query, Body }

  private sealed record Normalized(Mode Mode, Type RequestType, IOpenApiSchema Schema, IReadOnlyList<string> MediaTypes);

  private static bool IsMvc(OperationFilterContext ctx)
    => ctx.ApiDescription.ActionDescriptor is ControllerActionDescriptor;

  private static Normalized? NormalizeMinimal(OperationFilterContext ctx)
  {
    var reqType = GetRequestTypeFromMetadata(ctx);
    if (reqType is null) return null;
    var schema = ctx.SchemaGenerator.GenerateSchema(reqType, ctx.SchemaRepository);
    var isGet = string.Equals(ctx.ApiDescription.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
    if (isGet) return new Normalized(Mode.Query, reqType, schema, []);

    // Minimal API: best-effort media discovery
    var medias = ctx.ApiDescription.SupportedRequestFormats
      .Select(f => f.MediaType)
      .Where(mt => !string.IsNullOrWhiteSpace(mt))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .DefaultIfEmpty("application/json")
      .ToArray();
    return new Normalized(Mode.Body, reqType, schema, medias);
  }

  private static Normalized? NormalizeMvc(OperationFilterContext ctx)
  {
    var reqType = GetRequestTypeFromMetadata(ctx);
    if (reqType is null) return null;
    var schema = ctx.SchemaGenerator.GenerateSchema(reqType, ctx.SchemaRepository);
    var isGet = string.Equals(ctx.ApiDescription.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
    if (isGet) return new Normalized(Mode.Query, reqType, schema, []);

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
        ?.ToList() ?? [];
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
    operation.Parameters ??= [];

    var (props, requiredSet) = ResolveProperties(n.Schema, ctx.SchemaRepository);
    if (props is null || props.Count == 0)
    {
      foreach (var p in n.RequestType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
      {
        if (!p.CanRead) continue;
        var propSchema = ctx.SchemaGenerator.GenerateSchema(p.PropertyType, ctx.SchemaRepository);
        propSchema = CoerceEnumSchemaIfNeeded(propSchema, p.PropertyType);
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
      // If we can locate the CLR property and it's an enum (or array/nullable of enum), coerce to string enum
      var pi = n.RequestType.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
      if (pi is not null)
        propSchema = CoerceEnumSchemaIfNeeded(propSchema, pi.PropertyType);
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
      if (!operation.RequestBody.Content!.TryGetValue(mt, out var content))
      {
        content = new OpenApiMediaType();
        operation.RequestBody.Content[mt] = content;
      }
      if (string.Equals(mt, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
      {
        // Build a multipart-aware schema that renders file chooser(s) and collect doc info
        var (mpSchema, encodings, fileParts, _) = BuildMultipartSchema(n.RequestType, n.Schema);
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
              sb.AppendLine($"- {p.Name}{arr} ({ct}), payload shape: {shape}");
            }
          }
          operation.Description = string.IsNullOrWhiteSpace(operation.Description)
            ? sb.ToString()
            : operation.Description + "\n\n" + sb.ToString();
        }

        // Append format selector information if present (from x-formatSelectors)
        if (content.Schema is OpenApiSchema contentSchema && 
            contentSchema.Extensions != null && 
            contentSchema.Extensions.TryGetValue("x-formatSelectors", out var ext) && 
            ext is JsonNodeExtension jsonNodeExt && jsonNodeExt.Node is JsonObject fmtObj)
        {
          var sb = new System.Text.StringBuilder();
          sb.AppendLine();
          sb.AppendLine("Formats:");
          foreach (var kv in fmtObj)
          {
            var fieldName = kv.Key;
            if (kv.Value is JsonArray arr)
            {
              var pairs = new List<string>();
              foreach (var any in arr)
              {
                if (any is JsonObject o && o.TryGetPropertyValue("name", out var nameAny) && o.TryGetPropertyValue("contentType", out var ctAny))
                {
                  var fmtName = nameAny?.GetValue<string>() ?? "";
                  var ct = ctAny?.GetValue<string>() ?? "application/octet-stream";
                  pairs.Add($"{fmtName} ({ct})");
                }
              }
              if (pairs.Count > 0)
                sb.AppendLine($"- {fieldName}: one of {string.Join(", ", pairs)}");
            }
          }
          var txt = sb.ToString();
          if (!string.IsNullOrWhiteSpace(txt))
          {
            operation.Description = string.IsNullOrWhiteSpace(operation.Description)
              ? txt
              : operation.Description + "\n\n" + txt;
          }
        }
      }
      else
      {
        // Other media types: use the normalized schema directly
        content.Schema = n.Schema;
        // Coerce enum properties within the request model to string enums
        CoerceEnumPropertiesRecursive(content.Schema, n.RequestType, context.SchemaRepository);
      }
    }
  }

  private static IOpenApiSchema CoerceEnumSchemaIfNeeded(IOpenApiSchema schema, Type clrType)
  {
    if (schema is not OpenApiSchema openApiSchema) return schema;

    // Handle Nullable<T>
    var t = Nullable.GetUnderlyingType(clrType) ?? clrType;

    // Arrays / IEnumerable<T>
    if (t.IsArray)
    {
      var elem = t.GetElementType()!;
      if (openApiSchema.Items is not null)
        openApiSchema.Items = CoerceEnumSchemaIfNeeded(openApiSchema.Items, elem);
      return openApiSchema;
    }

    var ienum = GetIEnumerableElementType(t);
    if (ienum is not null && openApiSchema.Items is not null)
    {
      openApiSchema.Items = CoerceEnumSchemaIfNeeded(openApiSchema.Items, ienum);
      return openApiSchema;
    }

    if (t.IsEnum)
    {
      // Break component references for enum properties so we can inline string-enum
      openApiSchema.AllOf = null;
      openApiSchema.OneOf = null;
      openApiSchema.AnyOf = null;
      openApiSchema.Type = JsonSchemaType.String;
      openApiSchema.Format = null;
      var names = System.Enum.GetNames(t);
      openApiSchema.Enum = names.Select(n => (JsonNode)JsonValue.Create(n)!).ToList();
      // If previous Default/Example were numeric, replace with first name example
      if (openApiSchema.Default is JsonValue defVal && (defVal.TryGetValue<int>(out _) || defVal.TryGetValue<long>(out _) || defVal.TryGetValue<double>(out _)))
        openApiSchema.Default = JsonValue.Create(names.First());
      if (openApiSchema.Example is null || (openApiSchema.Example is JsonValue exVal && (exVal.TryGetValue<int>(out _) || exVal.TryGetValue<long>(out _) || exVal.TryGetValue<double>(out _))))
        openApiSchema.Example = JsonValue.Create(names.First());
      return openApiSchema;
    }

    return openApiSchema;
  }

  private static void CoerceEnumPropertiesRecursive(IOpenApiSchema schema, Type clrType, SchemaRepository repo)
  {
    var (props, _) = ResolveProperties(schema, repo);
    if (props is null || props.Count == 0) return;

    var bindingFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase;
    foreach (var kv in props)
    {
      var name = kv.Key;
      var propSchema = kv.Value;
      var pi = clrType.GetProperty(name, bindingFlags);
      if (pi is null) continue;

      props[name] = CoerceEnumSchemaIfNeeded(propSchema, pi.PropertyType);

      // Recurse into nested object graphs
      var innerType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
      if (!innerType.IsPrimitive && !innerType.IsEnum && innerType != typeof(string))
      {
        // Array / IEnumerable element recursion
        if (propSchema is OpenApiSchema openApiPropSchema)
        {
          if (innerType.IsArray && openApiPropSchema.Items is not null)
          {
            var elem = innerType.GetElementType()!;
            CoerceEnumPropertiesRecursive(openApiPropSchema.Items, elem, repo);
          }
          else
          {
            var elemI = GetIEnumerableElementType(innerType);
            if (elemI is not null && openApiPropSchema.Items is not null)
              CoerceEnumPropertiesRecursive(openApiPropSchema.Items, elemI, repo);
            else
              CoerceEnumPropertiesRecursive(propSchema, innerType, repo);
          }
        }
      }
    }
  }

  private static Type? GetIEnumerableElementType(Type t)
  {
    if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return null;
    if (t.IsGenericType)
    {
      var ga = t.GetGenericArguments();
      if (ga.Length == 1) return ga[0];
    }
    return null;
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

  private static (IOpenApiSchema mpSchema, IDictionary<string, OpenApiEncoding> encodings, List<FilePartDoc> parts, List<string> otherParts)
    BuildMultipartSchema(Type requestType, IOpenApiSchema fallback)
  {
    var schema = new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema>() };
    var enc = new Dictionary<string, OpenApiEncoding>(StringComparer.OrdinalIgnoreCase);
    var parts = new List<FilePartDoc>();
    var others = new List<string>();
    // Collect format selectors to later render in description; store as an OpenAPI extension on the schema
    var formatSelectors = new Dictionary<string, IReadOnlyList<FormatInfo>>(StringComparer.OrdinalIgnoreCase);

    var props = requestType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
      .Where(p => p.CanRead);

    foreach (var p in props)
    {
      var name = p.Name;
      var t = p.PropertyType;

      if (IsIFormFileType(t))
      {
        schema.Properties[name] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };
        enc[name] = new OpenApiEncoding { ContentType = "application/octet-stream" };
        parts.Add(new FilePartDoc(name, "application/octet-stream", null, false));
        continue;
      }

      if (IsEnumerableOfIFormFile(t))
      {
        schema.Properties[name] = new OpenApiSchema
        {
          Type = JsonSchemaType.Array,
          Items = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
        };
        enc[name] = new OpenApiEncoding { ContentType = "application/octet-stream" };
        parts.Add(new FilePartDoc(name, "application/octet-stream", null, true));
        continue;
      }

      if (IsFileUploadMarker(t))
      {
        // Default file schema
        var fileSchema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };

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
          fileSchema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
          // Human-friendly name
          fileSchema.Extensions["x-fileModel"] = new JsonNodeExtension(JsonValue.Create(modelType.Name));
        }
        parts.Add(new FilePartDoc(name, contentType ?? "application/octet-stream", modelType, false));
        continue;
      }

      // Recognize FormatName<TTag> and emit string-enum using registered infos
      if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(FormatName<>))
      {
        var tag = t.GetGenericArguments()[0];
        if (FileWithFormatDocRegistry.TryGetInfos(tag, out var infos))
        {
          var s = new OpenApiSchema
          {
            Type = JsonSchemaType.String,
            Enum = infos.Select(fi => (JsonNode)JsonValue.Create(fi.Name)!).ToList()
          };
          schema.Properties[name] = s;
          formatSelectors[name] = infos;
          continue;
        }
      }

      // Fallback: leave as simple string for multipart; detailed schema is available in other content types
      schema.Properties[name] = new OpenApiSchema { Type = JsonSchemaType.String };
      others.Add(name);
    }

    // If we didn't detect any properties, fall back to provided schema
    if (schema.Properties.Count == 0)
      return (fallback, enc, parts, others);

    // If we collected any format selectors, persist them in an extension so ApplyBody can render a concise description
    if (formatSelectors.Count > 0)
    {
      var obj = new JsonObject();
      foreach (var kv in formatSelectors)
      {
        var arr = new JsonArray();
        foreach (var fi in kv.Value)
        {
          var item = new JsonObject
          {
            ["name"] = JsonValue.Create(fi.Name),
            ["contentType"] = JsonValue.Create(fi.ContentType)
          };
          arr.Add(item);
        }
        obj[kv.Key] = arr;
      }
      schema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
      schema.Extensions["x-formatSelectors"] = new JsonNodeExtension(obj);
    }

    return (schema, enc, parts, others);
  }

  // Renders a compact field list for a record/class using the generated OpenAPI schema
  private static string RenderTypeShape(Type t, OperationFilterContext ctx)
  {
    try
    {
      var s = ctx.SchemaGenerator.GenerateSchema(t, ctx.SchemaRepository);
      var (props, required) = ResolveProperties(s, ctx.SchemaRepository);
      if (props is null || props.Count == 0)
        return t.Name;

      static string Map(IOpenApiSchema ps)
      {
        if (ps is OpenApiSchema openApiSchema)
        {
          if (openApiSchema.Type != null && (openApiSchema.Type.Value & JsonSchemaType.Array) == JsonSchemaType.Array && openApiSchema.Items is not null)
          {
            var inner = Map(openApiSchema.Items);
            return inner + "[]";
          }
          if (openApiSchema.Type != null && openApiSchema.Type.Value != 0)
          {
            return openApiSchema.Format switch
            {
              "int32" => "int",
              "int64" => "long",
              _ => openApiSchema.Type.Value.ToString().ToLowerInvariant()
            };
          }
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
