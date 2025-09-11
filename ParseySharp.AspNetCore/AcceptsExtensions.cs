using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http.Metadata;

namespace ParseySharp.AspNetCore;

public static class AcceptsExtensions
{
  // Docs-only metadata: single request model per endpoint (applies to all formats)
  public sealed record RequestModelMetadata(Type RequestType);

  public static RouteHandlerBuilder SetRequestModel<TPayload>(this RouteHandlerBuilder builder)
    => builder.SetRequestModel(typeof(TPayload));

  public static RouteHandlerBuilder SetRequestModel(this RouteHandlerBuilder builder, Type requestType)
  {
    builder.Add(eb => eb.Metadata.Add(new RequestModelMetadata(requestType)));
    return builder;
  }


  // Helper to add Accepts metadata from a registered content handler. It reads the
  // request model type from RequestModelMetadata if present, otherwise defaults to object.
  public static RouteHandlerBuilder AcceptsWith<THandler>(this RouteHandlerBuilder builder)
    where THandler : class, IContentHandler
  {
    builder.Add(eb =>
    {
      var h = eb.ApplicationServices
                  .GetServices<IContentHandler>()
                  .OfType<THandler>()
                  .FirstOrDefault()
        ?? throw new InvalidOperationException($"Content handler '{typeof(THandler).FullName}' is not registered as an IContentHandler.");
      var newCts = h.SupportedContentTypes.ToArray();

      var modelType = eb.Metadata.OfType<RequestModelMetadata>().LastOrDefault()?.RequestType
                       ?? typeof(object);

      // Merge if an Accepts already exists for this model type
      var existing = eb.Metadata
        .OfType<IAcceptsMetadata>()
        .FirstOrDefault(m => m.RequestType == modelType);

      if (existing is not null)
      {
        var merged = existing.ContentTypes
          .Concat(newCts)
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToArray();

        eb.Metadata.Remove(existing);
        eb.Metadata.Add(new AcceptsMetadataImpl(modelType, merged));
      }
      else
      {
        eb.Metadata.Add(new AcceptsMetadataImpl(modelType, newCts));
      }
    });
    return builder;
  }

  // Non-generic Accepts helpers (use RequestModelMetadata or object)
  public static RouteHandlerBuilder AcceptsJson(this RouteHandlerBuilder builder)
    => builder.AcceptsWith<JsonContentHandler>();

  public static RouteHandlerBuilder AcceptsXml(this RouteHandlerBuilder builder)
    => builder.AcceptsWith<XmlContentHandler>();

  public static RouteHandlerBuilder AcceptsFormUrlEncoded(this RouteHandlerBuilder builder)
    => builder.AcceptsWith<FormUrlEncodedContentHandler>();

  public static RouteHandlerBuilder AcceptsMultipart(this RouteHandlerBuilder builder)
    => builder.AcceptsWith<MultipartFormDataContentHandler>();

  public static RouteHandlerBuilder AcceptsQueryString(this RouteHandlerBuilder builder)
    => builder.AcceptsWith<QueryStringContentHandler>();
}

// Minimal public implementation to attach Accepts metadata directly during endpoint build
internal sealed class AcceptsMetadataImpl(Type requestType, IReadOnlyList<string> contentTypes) : IAcceptsMetadata
{
  public Type? RequestType { get; } = requestType;
  public bool IsOptional => false;
  public IReadOnlyList<string> ContentTypes { get; } = contentTypes;
}
