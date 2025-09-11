using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace ParseySharp.AspNetCore;

// Reads [RequestModel<T>] (or non-generic) on MVC actions and writes
// a Minimal-API-compatible metadata entry so downstream (e.g., Swagger filter)
// can read a unified RequestModelMetadata without reflection.
public sealed class RequestModelConvention : IActionModelConvention
{
  public void Apply(ActionModel action)
  {
    // Prefer generic attribute if present
    var attrGenObj = action.Attributes
      .FirstOrDefault(a => a.GetType().IsGenericType && a.GetType().GetGenericTypeDefinition() == typeof(RequestModelAttribute<>));

    if (attrGenObj is not null)
    {
      var modelType = attrGenObj.GetType().GetProperty("ModelType")!.GetValue(attrGenObj) as Type;
      foreach (var selector in action.Selectors)
        selector.EndpointMetadata.Add(new AcceptsExtensions.RequestModelMetadata(modelType!));
      return;
    }

    // Fallback to non-generic
    var attr = action.Attributes
      .OfType<RequestModelAttribute>()
      .FirstOrDefault();

    if (attr is not null)
    {
      foreach (var selector in action.Selectors)
        selector.EndpointMetadata.Add(new AcceptsExtensions.RequestModelMetadata(attr.ModelType));
    }
  }
}
