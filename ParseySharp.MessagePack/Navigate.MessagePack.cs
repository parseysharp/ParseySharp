using System.Buffers;
using System.Globalization;
using MessagePack;

namespace ParseySharp.MessagePack;

// Minimal recursive carrier for MessagePack using Cysharp's MessagePack reader
public abstract record MsgNode
{
  public sealed record Map(IReadOnlyDictionary<string, MsgNode> Items) : MsgNode;
  public sealed record Array(IReadOnlyList<MsgNode> Items) : MsgNode;
  public sealed record Str(string Value) : MsgNode;
  public sealed record I64(long Value) : MsgNode;
  public sealed record U64(ulong Value) : MsgNode;
  public sealed record F64(double Value) : MsgNode;
  public sealed record Bool(bool Value) : MsgNode;
  public sealed record Bin(byte[] Value) : MsgNode;
  public sealed record Nil() : MsgNode;
}

public static class ParsePathNavMessagePack
{
  public static readonly ParsePathNav<MsgNode> MsgPack =
    new(
      Prop: (node, name) =>
        node switch
        {
          MsgNode.Map m =>
            (m.Items.TryGetValue(name, out var child)
              ? Right<object, Option<MsgNode>>(Optional(child))
              : Right<object, Option<MsgNode>>(None)),
          _ => Left<object, Option<MsgNode>>(node)
        },

      Index: (node, i) =>
        node switch
        {
          MsgNode.Array a when i >= 0 =>
            (i < a.Items.Count
              ? Right<object, Option<MsgNode>>(Optional(a.Items[i]))
              : Right<object, Option<MsgNode>>(None)),
          _ => Left<object, Option<MsgNode>>(node)
        },

      Unbox: node => node switch
      {
        MsgNode.Nil => Right<object, Unknown<object>>(new Unknown<object>.None()),
        MsgNode.Str x => Right<object, Unknown<object>>(Unknown.New<object>(x.Value)),
        // Prefer Int32 when in range, else Int64
        MsgNode.I64 x =>
          (x.Value <= int.MaxValue && x.Value >= int.MinValue)
            ? Right<object, Unknown<object>>(Unknown.New<object>((int)x.Value))
            : Right<object, Unknown<object>>(Unknown.New<object>(x.Value)),
        // U64: Int32 if in range, else Int64 if it fits, else string to preserve value
        MsgNode.U64 x =>
          (x.Value <= (ulong)int.MaxValue)
            ? Right<object, Unknown<object>>(Unknown.New<object>((int)x.Value))
            : (x.Value <= long.MaxValue)
              ? Right<object, Unknown<object>>(Unknown.New<object>((long)x.Value))
              : Right<object, Unknown<object>>(Unknown.New<object>(x.Value.ToString())),
        MsgNode.F64 x => Right<object, Unknown<object>>(Unknown.New<object>(x.Value)),
        MsgNode.Bool x => Right<object, Unknown<object>>(Unknown.New<object>(x.Value)),
        MsgNode.Bin x => Right<object, Unknown<object>>(Unknown.New<object>(x.Value)),
        // Arrays expose their items so Seq can iterate naturally
        MsgNode.Array a => Right<object, Unknown<object>>(Unknown.New<object>(a.Items)),
        // Maps remain as nodes for key-based traversal
        MsgNode.Map => Right<object, Unknown<object>>(Unknown.New<object>(node)),
        _ => Left<object, Unknown<object>>(node)
      }
    );
}

public static class MessagePackBuilder
{
  public static Either<string, MsgNode> FromBytes(ReadOnlyMemory<byte> bytes)
  {
    try
    {
      var reader = new MessagePackReader(bytes);
      var node = ReadNode(ref reader);
      return Right<string, MsgNode>(node);
    }
    catch (Exception ex)
    {
      return Left<string, MsgNode>($"Invalid MessagePack: {ex.Message}");
    }
  }

  static MsgNode ReadNode(ref MessagePackReader reader)
  {
    var code = reader.NextMessagePackType;
    switch (code)
    {
      case MessagePackType.Nil:
        reader.ReadNil();
        return new MsgNode.Nil();
      case MessagePackType.Boolean:
        return new MsgNode.Bool(reader.ReadBoolean());
      case MessagePackType.Integer:
      {
        if (reader.TryReadNil()) return new MsgNode.Nil();
        try
        {
          var i = reader.ReadInt64();
          return new MsgNode.I64(i);
        }
        catch
        {
          var u = reader.ReadUInt64();
          return new MsgNode.U64(u);
        }
      }
      case MessagePackType.Float:
        return new MsgNode.F64(reader.ReadDouble());
      case MessagePackType.String:
        return new MsgNode.Str(reader.ReadString() ?? string.Empty);
      case MessagePackType.Binary:
        return new MsgNode.Bin(reader.ReadBytes()?.ToArray() ?? []);
      case MessagePackType.Array:
      {
        var len = reader.ReadArrayHeader();
        var list = new List<MsgNode>(len);
        for (int i = 0; i < len; i++)
          list.Add(ReadNode(ref reader));
        // Normalize array-of-pairs (e.g., LanguageExt Map encodes as [[key,value], ...]) into Map
        static bool IsKV(MsgNode n, out string key, out MsgNode val)
        {
          key = string.Empty; val = new MsgNode.Nil();
          if (n is MsgNode.Array { Items: var items } && items.Count == 2 && items[0] is MsgNode.Str s)
          {
            key = s.Value; val = items[1];
            return true;
          }
          return false;
        }
        if (list.Count > 0 && list.All(n => IsKV(n, out _, out _)))
        {
          var dict = new Dictionary<string, MsgNode>(list.Count, StringComparer.Ordinal);
          foreach (var n in list)
          {
            IsKV(n, out var k, out var v);
            dict[k] = v;
          }
          return new MsgNode.Map(dict);
        }
        return new MsgNode.Array(list);
      }
      case MessagePackType.Map:
      {
        var len = reader.ReadMapHeader();
        var dict = new Dictionary<string, MsgNode>(len, StringComparer.Ordinal);
        for (int i = 0; i < len; i++)
        {
          // Only support string keys for Prop; for non-string keys, stringify
          var keyType = reader.NextMessagePackType;
          string key;
          if (keyType == MessagePackType.String)
            key = reader.ReadString() ?? string.Empty;
          else
          {
            var keyNode = ReadNode(ref reader);
            key = keyNode switch
            {
              MsgNode.Str s => s.Value,
              MsgNode.I64 s => s.Value.ToString(),
              MsgNode.U64 s => s.Value.ToString(),
              MsgNode.F64 s => s.Value.ToString(CultureInfo.InvariantCulture),
              MsgNode.Bool s => s.Value ? "true" : "false",
              MsgNode.Nil => "null",
              _ => "_"
            };
          }
          var value = ReadNode(ref reader);
          // Last write wins on duplicate keys
          dict[key] = value;
        }
        return new MsgNode.Map(dict);
      }
      default:
        // Treat unknown as nil
        reader.Skip();
        return new MsgNode.Nil();
    }
  }
}