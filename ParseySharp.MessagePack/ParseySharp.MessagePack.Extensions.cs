using ParseySharp.MessagePack;

namespace ParseySharp;

public static class ParseMessagePackExtensions
{
  public static Func<MsgNode, Validation<Seq<ParsePathErr>, A>> ParseMessagePackNode<A>(this Parse<A> parser) =>
    ParseExtensions.RunWithNav(parser, ParsePathNavMessagePack.MsgPack);

  public static Func<byte[], Validation<Seq<ParsePathErr>, A>> ParseMessagePackBytes<A>(this Parse<A> parser) =>
    bytes => MessagePackBuilder.FromBytes(bytes).Match(
      Left: err => Fail<Seq<ParsePathErr>, A>([new ParsePathErr(err, typeof(A).Name, Optional((object?)null), [])]),
      Right: node => parser.ParseMessagePackNode<A>()(node)
    );
}