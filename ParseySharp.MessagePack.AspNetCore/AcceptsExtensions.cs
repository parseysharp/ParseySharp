using Microsoft.AspNetCore.Routing;

namespace ParseySharp.AspNetCore;

public static class AcceptsExtensionsMessagePack
{
  public static RouteHandlerBuilder AcceptsMessagePack(this RouteHandlerBuilder builder)
    => AcceptsExtensions.AcceptsWith<MessagePackContentHandler>(builder);
}
