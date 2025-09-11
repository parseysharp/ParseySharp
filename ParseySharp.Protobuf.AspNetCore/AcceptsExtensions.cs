using Google.Protobuf;

namespace ParseySharp.AspNetCore;

public static class AcceptsExtensionsProtobuf
{
  public static RouteHandlerBuilder AcceptsProtobuf(this RouteHandlerBuilder builder)
    => AcceptsExtensions.AcceptsWith<ProtobufContentHandler>(builder);

  public static RouteHandlerBuilder AcceptsProtobuf<TMessage>(this RouteHandlerBuilder builder, MessageParser<TMessage> parser)
    where TMessage : class, IMessage<TMessage>
  {
    builder = builder.AcceptsWith<ProtobufContentHandler>();
    builder.Add(eb => eb.Metadata.Add(new ProtobufDecoderMetadata(bytes => parser.ParseFrom(bytes.Span))));
    return builder;
  }
}
