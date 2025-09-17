using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using ParseySharp.AspNetCore;

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
    if (props.Count == 0)
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
      if (!operation.RequestBody.Content.TryGetValue(mt, out var content))
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
        if (content.Schema.Extensions != null && content.Schema.Extensions.TryGetValue("x-formatSelectors", out var ext) && ext is OpenApiObject fmtObj && fmtObj.Count > 0)
        {
          var sb = new System.Text.StringBuilder();
          sb.AppendLine();
          sb.AppendLine("Formats:");
          foreach (var kv in fmtObj)
          {
            var fieldName = kv.Key;
            if (kv.Value is OpenApiArray arr)
            {
              var pairs = new List<string>();
              foreach (var any in arr)
              {
                if (any is OpenApiObject o && o.TryGetValue("name", out var nameAny) && o.TryGetValue("contentType", out var ctAny))
                {
                  var fmtName = (nameAny as OpenApiString)?.Value ?? "";
                  var ct = (ctAny as OpenApiString)?.Value ?? "application/octet-stream";
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

  private static OpenApiSchema CoerceEnumSchemaIfNeeded(OpenApiSchema schema, Type clrType)
  {
    // Handle Nullable<T>
    var t = Nullable.GetUnderlyingType(clrType) ?? clrType;

    // Arrays / IEnumerable<T>
    if (t.IsArray)
    {
      var elem = t.GetElementType()!;
      if (schema.Items is not null)
        schema.Items = CoerceEnumSchemaIfNeeded(schema.Items, elem);
      return schema;
    }

    var ienum = GetIEnumerableElementType(t);
    if (ienum is not null && schema.Items is not null)
    {
      schema.Items = CoerceEnumSchemaIfNeeded(schema.Items, ienum);
      return schema;
    }

    if (t.IsEnum)
    {
      // Break component references for enum properties so we can inline string-enum
      schema.Reference = null;
      schema.AllOf = null;
      schema.OneOf = null;
      schema.AnyOf = null;
      schema.Type = "string";
      schema.Format = null;
      var names = System.Enum.GetNames(t);
      schema.Enum = names.Select(n => (IOpenApiAny)new OpenApiString(n)).ToList();
      // If previous Default/Example were numeric, replace with first name example
      if (schema.Default is OpenApiInteger || schema.Default is OpenApiLong || schema.Default is OpenApiDouble)
        schema.Default = new OpenApiString(names.First());
      if (schema.Example is null || schema.Example is OpenApiInteger || schema.Example is OpenApiLong || schema.Example is OpenApiDouble)
        schema.Example = new OpenApiString(names.First());
      return schema;
    }

    return schema;
  }

  private static void CoerceEnumPropertiesRecursive(OpenApiSchema schema, Type clrType, SchemaRepository repo)
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
        if (innerType.IsArray && propSchema.Items is not null)
        {
          var elem = innerType.GetElementType()!;
          CoerceEnumPropertiesRecursive(propSchema.Items, elem, repo);
        }
        else
        {
          var elemI = GetIEnumerableElementType(innerType);
          if (elemI is not null && propSchema.Items is not null)
            CoerceEnumPropertiesRecursive(propSchema.Items, elemI, repo);
          else
            CoerceEnumPropertiesRecursive(propSchema, innerType, repo);
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

  private static (OpenApiSchema mpSchema, IDictionary<string, OpenApiEncoding> encodings, List<FilePartDoc> parts, List<string> otherParts)
    BuildMultipartSchema(Type requestType, OpenApiSchema fallback)
  {
    var schema = new OpenApiSchema { Type = "object", Properties = new Dictionary<string, OpenApiSchema>() };
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
          fileSchema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
          // Human-friendly name
          fileSchema.Extensions["x-fileModel"] = new OpenApiString(modelType.Name);
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
            Type = "string",
            Enum = infos.Select(fi => (IOpenApiAny)new OpenApiString(fi.Name)).ToList()
          };
          schema.Properties[name] = s;
          formatSelectors[name] = infos;
          continue;
        }
      }

      // Fallback: leave as simple string for multipart; detailed schema is available in other content types
      schema.Properties[name] = new OpenApiSchema { Type = "string" };
      others.Add(name);
    }

    // If we didn't detect any properties, fall back to provided schema
    if (schema.Properties.Count == 0)
      return (fallback, enc, parts, others);

    // If we collected any format selectors, persist them in an extension so ApplyBody can render a concise description
    if (formatSelectors.Count > 0)
    {
      var obj = new OpenApiObject();
      foreach (var kv in formatSelectors)
      {
        var arr = new OpenApiArray();
        foreach (var fi in kv.Value)
        {
          var item = new OpenApiObject
          {
            ["name"] = new OpenApiString(fi.Name),
            ["contentType"] = new OpenApiString(fi.ContentType)
          };
          arr.Add(item);
        }
        obj[kv.Key] = arr;
      }
      schema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
      schema.Extensions["x-formatSelectors"] = obj;
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
      if (props.Count == 0)
        return t.Name;

      static string Map(OpenApiSchema ps)
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
