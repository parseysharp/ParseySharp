using System.Text.Json;
using System.Xml.Linq;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using Google.Protobuf.WellKnownTypes;
using System.Data;
using YamlDotNet.RepresentationModel;
using MessagePack;
using MessagePack.Resolvers;
using Avro;
using Avro.Generic;

namespace ParseySharp.Tests;

public static class Obj {
  public static Seq<object?> Seq(Seq<object?> xs) => xs;

  public static object? Record(Seq<(string, object?)> xs) => toMap(xs);
}

#pragma warning disable IDE1006 // Naming Styles (test schema uses lower-case names)
public abstract record MyEither<L, R>
{
  public sealed record Left(L left) : MyEither<L, R>
  {
    public string kind => "left";
  }

  public sealed record Right(R right) : MyEither<L, R>
  {
    public string kind => "right";
  }
}

public sealed class Row
{
  public string kind { get; set; } = "";
  public string? left { get; set; }
  public int? right { get; set; }
};
#pragma warning restore IDE1006

public static class MyEither
{
  public static MyEither<L, R> Left<L, R>(L left) => new MyEither<L, R>.Left(left);
  public static MyEither<L, R> Right<L, R>(R right) => new MyEither<L, R>.Right(right);
}

public class ParserTests
{

  static Parse<Either<L, R>> EitherParser<L, R>(Parse<L> left, Parse<R> right) =>
    (from f in Parse.As<string>().At("kind", [])
      .Filter(x => x switch
      {
        "left" => Right<string, Either<Unit, Unit>>(Left<Unit, Unit>(Unit.Default)),
        "right" => Right<string, Either<Unit, Unit>>(Right<Unit, Unit>(Unit.Default)),
        _ => Left<string, Either<Unit, Unit>>($"Invalid kind: {x}")
      })
    from v in f.Match(
      Left: _ => left.At("left", []).Map(Left<L, R>),
      Right: _ => right.At("right", []).Map(Right<L, R>)
    )
    select v).As();

  [Fact]
    public void Builds_A_Parser()
    {
      var singleParser = EitherParser(
        left: Parse.As<string>()
          .Filter(s => Some("Too short!").Filter(_ => s.Length < 5)),
        right: Parse.Int32Flex().Option()
          .Filter(x => x.Filter(i => i < 21).Map(_ => "Too low!"))
      );
      var parser = singleParser.Seq();

      var objInput = Obj.Seq([
            Obj.Record(
              [("kind", "left"), ("left", "hello")]
            ),
            Obj.Record(
              [("kind", "left"), ("left", "clarice")]
            ),
            Obj.Record(
              [("kind", "right"), ("right", null)]
            ),
            Obj.Record(
              [("kind", "left"), ("left", "fortytwo")]
            ),
            Obj.Record(
              [("kind", "right"), ("right", 42)]
            )]);
      var result = parser.ParseObject()(objInput);
      Console.WriteLine(result);

      var reflectResult = parser.ParseReflect()(
        Seq(MyEither.Left<string, Option<int>>("hello"),
          MyEither.Left<string, Option<int>>("clarice"),
          MyEither.Right<string, Option<int>>(None),
          MyEither.Left<string, Option<int>>("fortytwo"),
          MyEither.Right<string, Option<int>>(Some(42))));

      Console.WriteLine(reflectResult);

      var json = """
        [
          { "kind": "left",  "left": "hello" },
          { "kind": "left",  "left": "clarice" },
          { "kind": "right", "right": null },
          { "kind": "left",  "left": "fortytwo" },
          { "kind": "right", "right": 42 }
        ]
        """;

      var root = JsonDocument.Parse(json).RootElement;

      var jsonResult = parser.ParseJson()(root);

      Console.WriteLine(jsonResult);

      var inputXml =
        XElement.Parse(@"
          <root>
            <item>
              <kind>left</kind>
              <left>hello</left>
            </item>
            <item>
              <kind>left</kind>
              <left>clarice</left>
            </item>
            <item>
              <kind>right</kind>
              <right></right>
            </item>
            <item>
              <kind>left</kind>
              <left>fortytwo</left>
            </item>
            <item>
              <kind>right</kind>
              <right>42</right>
            </item>
          </root>
      ");

      var xmlResult = parser.ParseXml()(inputXml);

      Console.WriteLine(xmlResult);

      JsonNode node = JsonNode.Parse("""
        [
          { "kind": "left",  "left": "hello" },
          { "kind": "left",  "left": "clarice" },
          { "kind": "right", "right": null },
          { "kind": "left",  "left": "fortytwo" },
          { "kind": "right", "right": 42 }
        ]
        """)!;

      var jsonNodeResult = parser.ParseJsonNode()(node);

      Console.WriteLine(jsonNodeResult);

      var jarr = JArray.Parse("""
        [
          { "kind": "left",  "left": "hello" },
          { "kind": "left",  "left": "clarice" },
          { "kind": "right", "right": null },
          { "kind": "left",  "left": "fortytwo" },
          { "kind": "right", "right": 42 }
        ]
        """);

      var jsonNetResult = parser.ParseNewtonsoftJson()(jarr);

      Console.WriteLine(jsonNetResult);

      var pb = new ListValue
      {
        Values =
        {
          Value.ForStruct(new Struct { Fields = { ["kind"] = Value.ForString("left"),  ["left"]  = Value.ForString("hello")    } }),
          Value.ForStruct(new Struct { Fields = { ["kind"] = Value.ForString("left"),  ["left"]  = Value.ForString("clarice")  } }),
          Value.ForStruct(new Struct { Fields = { ["kind"] = Value.ForString("right"), ["right"] = Value.ForNull()             } }),
          Value.ForStruct(new Struct { Fields = { ["kind"] = Value.ForString("left"),  ["left"]  = Value.ForString("fortytwo") } }),
          Value.ForStruct(new Struct { Fields = { ["kind"] = Value.ForString("right"), ["right"] = Value.ForNumber(42)         } }),
        }
      };

      var protobufResult = parser.ParseProtobuf()(pb);

      Console.WriteLine(protobufResult);

      // Protobuf (schema-backed) example: Timestamp is a generated IMessage with fields
      var ts = new Timestamp { Seconds = 123, Nanos = 456 };
      var tsParser = (
        Parse.As<long>().At("seconds", []),
        Parse.As<int>().At("nanos", [])
      ).Apply((s, n) => (seconds: s, nanos: n)).As();
      var tsResult = tsParser.ParseProtobuf()(ts);
      Console.WriteLine(tsResult);
      Assert.Equal(Success<Seq<ParsePathErr>, (long seconds, int nanos)>((123L, 456)), tsResult);

      var table = new DataTable();
      table.Columns.Add("kind", typeof(string));
      table.Columns.Add("left", typeof(string));
      table.Columns.Add("right", typeof(int));

      table.Rows.Add("left",  "hello",    DBNull.Value);
      table.Rows.Add("left",  "clarice",  DBNull.Value);
      table.Rows.Add("right", DBNull.Value, DBNull.Value);
      table.Rows.Add("left",  "fortytwo", DBNull.Value);
      table.Rows.Add("right", DBNull.Value, 42);

      var rowsEnum = table.Rows.Cast<DataRow>();

      // 1) DataTable → Seq<A> (eager, aggregated)
      var dataTableSeq = singleParser.ParseDataTableSeq()(table);
      Console.WriteLine(dataTableSeq);

      // 2) IEnumerable<DataRow> → Seq<A> (eager, aggregated)
      var dataRowsSeq = singleParser.ParseDataRowsSeq()(rowsEnum);
      Console.WriteLine(dataRowsSeq);

      // 3) DataTable stream → array of per-row Validation
      var dataTableStreamed = toSeq(singleParser.ParseDataTableStream(table).ToArray()).Traverse(x => x);
      Console.WriteLine(dataTableStreamed);

      // 4) IEnumerable<DataRow> stream → array of per-row Validation
      var dataRowsStreamed = toSeq(singleParser.ParseDataRowsStream(rowsEnum).ToArray()).Traverse(x => x);
      Console.WriteLine(dataRowsStreamed);

      // 5) IDataReader stream → array of per-row Validation
      var dataReaderStreamed = toSeq(singleParser.ParseDataReaderStream(table.CreateDataReader()).ToArray()).Traverse(x => x);
      Console.WriteLine(dataReaderStreamed);

      var yaml = """
        - kind: left
          left: hello
        - kind: left
          left: clarice
        - kind: right
          right: ~
        - kind: left
          left: fortytwo
        - kind: right
          right: 42
        """;
      
      var ystream = new YamlStream();
      using (var yreader = new StringReader(yaml))
      {
        ystream.Load(yreader);
      }
      var yroot = ystream.Documents[0].RootNode;

      var yamlResult = parser.ParseYamlNode()(yroot);
      Console.WriteLine(yamlResult);

      var mp1 = MessagePackSerializer.Serialize(objInput);

      var msgPackResult1 = parser.ParseMessagePackBytes()(mp1);
      Console.WriteLine(msgPackResult1);

      var rows = new[]
      {
        new Row { kind = "left",  left  = "hello"    },
        new Row { kind = "left",  left  = "clarice"  },
        new Row { kind = "right", right = null       },
        new Row { kind = "left",  left  = "fortytwo" },
        new Row { kind = "right", right = 42         },
      };

      var mp2 = MessagePackSerializer.Serialize(
        rows,
        MessagePackSerializerOptions.Standard
          .WithResolver(ContractlessStandardResolver.Instance)
        );

      var msgPackResult2 = parser.ParseMessagePackBytes()(mp2);
      Console.WriteLine(msgPackResult2);

      var rowSchema = (RecordSchema)Schema.Parse(@"
      {
        ""type"": ""record"",
        ""name"": ""row"",
        ""fields"": [
          { ""name"": ""kind"",  ""type"": ""string"" },
          { ""name"": ""left"",  ""type"": [""null"", ""string""], ""default"": null },
          { ""name"": ""right"", ""type"": [""null"", ""int""   ], ""default"": null }
        ]
      }");

      static GenericRecord Rec(RecordSchema s, params (string name, object? value)[] fields)
      {
        var r = new GenericRecord(s);
        foreach (var (name, value) in fields) r.Add(name, value);
        return r;
      }

      var avroRows = new GenericRecord[]
      {
        Rec(rowSchema, ("kind", "left"),  ("left", "hello"),   ("right", null)),
        Rec(rowSchema, ("kind", "left"),  ("left", "clarice"), ("right", null)),
        Rec(rowSchema, ("kind", "right"), ("left", null),      ("right", null)),
        Rec(rowSchema, ("kind", "left"),  ("left", "fortytwo"), ("right", null)),
        Rec(rowSchema, ("kind", "right"), ("left", null),      ("right", 42   )),
      };

      var avroResult = parser.ParseAvro()(avroRows);
      Console.WriteLine(avroResult);

      Assert.Equal(result, avroResult);
      Assert.Equal(result, msgPackResult1);
      Assert.Equal(result, msgPackResult2);
      Assert.Equal(result, yamlResult);
      Assert.Equal(result, dataTableSeq);
      Assert.Equal(result, dataRowsSeq);
      Assert.Equal(result, dataTableStreamed);
      Assert.Equal(result, dataRowsStreamed);
      Assert.Equal(result, dataReaderStreamed);
      Assert.Equal(result, protobufResult);
      Assert.Equal(result, xmlResult);
      Assert.Equal(result, jsonNodeResult);
      Assert.Equal(result, jsonNetResult);
      Assert.Equal(result, jsonResult);
      Assert.Equal(result, reflectResult);

      Assert.Equal(
        result,
        Success<Seq<ParsePathErr>, Seq<Either<string, Option<int>>>>(
          [Left<string, Option<int>>("hello"),
          Left<string, Option<int>>("clarice"),
          Right<string, Option<int>>(None),
          Left<string, Option<int>>("fortytwo"),
          Right<string, Option<int>>(Some(42))]
        ));
    }
}
