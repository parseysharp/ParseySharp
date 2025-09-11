using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace ParseySharp.AspNetCore;

// Base semantic attribute that links to a concrete content handler type.
// Satellite packages define their own attributes by overriding HandlerType.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class AcceptsContentAttribute : Attribute
{
  protected abstract Type HandlerType { get; }

  internal IReadOnlyList<string> ResolveContentTypes()
  {
    if (Activator.CreateInstance(HandlerType) is IContentHandler h)
      return h.SupportedContentTypes.ToArray();
    return [];
  }
}

// QueryString is semantic but does not add Consumes; it only signals non-body semantics.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AcceptsQueryStringAttribute : Attribute {}

public sealed class AcceptsJsonAttribute : AcceptsContentAttribute
{ protected override Type HandlerType => typeof(JsonContentHandler); }

public sealed class AcceptsXmlAttribute : AcceptsContentAttribute
{ protected override Type HandlerType => typeof(XmlContentHandler); }

public sealed class AcceptsFormUrlEncodedAttribute : AcceptsContentAttribute
{ protected override Type HandlerType => typeof(FormUrlEncodedContentHandler); }

public sealed class AcceptsMultipartAttribute : AcceptsContentAttribute
{ protected override Type HandlerType => typeof(MultipartFormDataContentHandler); }

// MVC convention: translate semantic Accepts* attributes into ConsumesAttribute entries.
public sealed class AcceptsSemanticConvention : IActionModelConvention
{
  public void Apply(ActionModel action)
  {
    var attrs = action.Attributes.OfType<AcceptsContentAttribute>().ToArray();
    if (attrs.Length == 0) return;

    // Merge all content-types from attributes
    var types = attrs.SelectMany(a => a.ResolveContentTypes())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .ToArray();
    if (types.Length == 0) return;

    // Remove existing Consumes to merge cleanly, then add a single merged Consumes
    var existing = action.Filters.OfType<ConsumesAttribute>().ToList();
    foreach (var c in existing) action.Filters.Remove(c);

    if (types.Length == 1)
      action.Filters.Add(new ConsumesAttribute(types[0]));
    else
      action.Filters.Add(new ConsumesAttribute(types[0], types.Skip(1).ToArray()));
  }
}

public static class MvcAcceptsExtensions
{
  // Register the convention to honor Accepts* semantic attributes in MVC controllers
  public static IMvcBuilder AddParseySharpMvc(this IMvcBuilder mvc)
  {
    mvc.Services.Configure<MvcOptions>(o =>
    {
      o.Conventions.Add(new AcceptsSemanticConvention());
      o.Conventions.Add(new RequestModelConvention());
    });
    return mvc;
  }

  // Back-compat alias if needed
  public static IMvcBuilder AddParseySharpMvcSemantics(this IMvcBuilder mvc)
    => AddParseySharpMvc(mvc);
}
