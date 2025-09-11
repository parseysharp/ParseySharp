using System.Text.Json;
using System.Runtime.CompilerServices;

namespace ParseySharp.AspNetCore;

public static partial class ParseMultipart
{
  // Get a single file by field name
  public static Parse<FilePart> FileAt(string name)
    => Parse.As<FilePart>().At(name, []);

  // Get all files under a field name as a sequence (when multiple files are uploaded for the same key)
  public static Parse<Seq<FilePart>> FilesAt(string name)
    => Parse.As<FilePart>().Seq().At(name, []);

  // Read all bytes from a single file
  public static Parse<byte[]> BytesAt(string name)
    => FileAt(name).Filter(fp =>
    {
      try
      {
        using var s = fp.OpenRead();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Success<Seq<ParsePathErr>, byte[]>(ms.ToArray());
      }
      catch (Exception ex)
      {
        return Fail<Seq<ParsePathErr>, byte[]>([ new ParsePathErr(ex.Message, "Byte[]", Some((object)fp.FileName), []) ]);
      }
    });

  // Read text from a file using UTF-8 by default
  public static Parse<string> TextAt(string name)
    => FileAt(name).Filter(fp =>
    {
      try
      {
        using var s = fp.OpenRead();
        using var sr = new StreamReader(s);
        var text = sr.ReadToEnd();
        return Success<Seq<ParsePathErr>, string>(text);
      }
      catch (Exception ex)
      {
        return Fail<Seq<ParsePathErr>, string>([ new ParsePathErr(ex.Message, nameof(String), Some((object)fp.FileName), []) ]);
      }
    });

  // Parse JSON file contents using the core JSON navigator with a supplied parser
  public static Parse<T> JsonAt<T>(string name, Parse<T> parser)
    => FileAt(name).Filter(fp =>
    {
      try
      {
        using var s = fp.OpenRead();
        using var doc = JsonDocument.Parse(s);
        var elem = doc.RootElement;
        if (elem.ValueKind == JsonValueKind.Undefined)
          return Fail<Seq<ParsePathErr>, T>([ new ParsePathErr("Empty JSON", typeof(T).Name, Some((object)fp.FileName), []) ]);
        return parser.ParseJson()(elem);
      }
      catch (Exception ex)
      {
        return Fail<Seq<ParsePathErr>, T>([ new ParsePathErr($"Invalid JSON: {ex.Message}", typeof(T).Name, Some((object)fp.FileName), []) ]);
      }
    });

  public static Parse<Seq<T>> CsvAt<T>(
    string name,
    bool hasHeader,
    Parse<T> lineParser,
    Func<TextReader, IEnumerable<string[]>> rows)
    => FileAt(name).Filter(fp =>
    {
      try
      {
        using var s = fp.OpenRead();
        using var sr = new StreamReader(s);
        var all = rows(sr).ToArray();
        if (all.Length == 0)
          return Success<Seq<ParsePathErr>, Seq<T>>(Empty);

        return lineParser.Seq().ParseObject()(
          hasHeader
            ? all.Skip(1)
                 .Select(row => (object)ToDict(all[0], row))
                 .ToArray()
            : all.Select(row => (object)ToIndexedDict(row)).ToArray()
        );
      }
      catch (Exception ex)
      {
        return Fail<Seq<ParsePathErr>, Seq<T>>([ new ParsePathErr($"Invalid CSV: {ex.Message}", typeof(T).Name, Some((object)name), []) ]);
      }
    });

  // Parse CSV file contents with a minimal, opinionated behavior:
  // - hasHeader = true: first row defines column names; each subsequent row becomes a Dictionary<string, object?>
  // - hasHeader = false: each row becomes an object[] (sequence), addressed by numeric index
  // The supplied lineParser is applied to each per-row carrier via ParseObject() or ParseArray().
  public static Parse<Seq<T>> CsvAt<T>(string name, bool hasHeader, Parse<T> lineParser)
    => CsvAt(name, hasHeader, lineParser, r => ParseCsv(r.ReadToEnd()));

  // STREAMING CSV: yield per-line validations without materializing entire file
  public static Parse<IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>> CsvStream<T>(
    string name,
    bool hasHeader,
    Parse<T> lineParser)
    => CsvStream(name, hasHeader, lineParser, DefaultCsvRowsAsync);

  public static Parse<IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>> CsvStream<T>(
    string name,
    bool hasHeader,
    Parse<T> lineParser,
    Func<TextReader, IAsyncEnumerable<string[]>> rowsAsync)
    => FileAt(name).Filter(fp =>
      Success<Seq<ParsePathErr>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(
        CsvStreamIterator(fp, hasHeader, lineParser, rowsAsync)
      )
    );

  private static async IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>> CsvStreamIterator<T>(
    FilePart fp,
    bool hasHeader,
    Parse<T> lineParser,
    Func<TextReader, IAsyncEnumerable<string[]>> rowsAsync,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    string[]? header = null;
    await foreach (var item in OwnEach(
      open: ct => Task.FromResult(rowsAsync(new StreamReader(fp.OpenRead()))),
      clone: (string[] row) => row.ToArray()
    ).WithCancellation(cancellationToken))
    {
      var idx = item.Index;
      var row = item.Value;
      if (hasHeader && header is null)
      {
        header = row;
        continue;
      }
      var carrier = hasHeader ? (object)ToDict(header!, row) : ToIndexedDict(row);
      var v = lineParser.ParseObject()(carrier);
      yield return v.MapFail(es => es.Map(e => e.WithPrefix([idx.ToString()])));
    }
  }

  // Default async row splitter: minimal CSV handling with quotes and multiline fields
  private static async IAsyncEnumerable<string[]> DefaultCsvRowsAsync(TextReader reader)
  {
    var cur = new List<string>();
    var sb = new System.Text.StringBuilder();
    bool inQuotes = false;
    while (true)
    {
      var line = await reader.ReadLineAsync();
      if (line is null) break;
      for (int i = 0; i < line.Length; i++)
      {
        var ch = line[i];
        if (inQuotes)
        {
          if (ch == '"')
          {
            if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
            else { inQuotes = false; }
          }
          else sb.Append(ch);
        }
        else
        {
          if (ch == '"') { inQuotes = true; }
          else if (ch == ',') { cur.Add(sb.ToString()); sb.Clear(); }
          else sb.Append(ch);
        }
      }
      // At end of physical line
      if (!inQuotes)
      {
        cur.Add(sb.ToString()); sb.Clear();
        yield return cur.ToArray();
        cur.Clear();
      }
      else
      {
        // Preserve newline inside a quoted field
        sb.Append('\n');
      }
    }
    if (sb.Length > 0 || cur.Count > 0)
    {
      cur.Add(sb.ToString());
      yield return cur.ToArray();
    }
  }

  // STREAMING NDJSON (one JSON object per line)
  public static Parse<IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>> NdjsonStream<T>(
    string name,
    Parse<T> lineParser)
    => FileAt(name).Filter(fp =>
      Success<Seq<ParsePathErr>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(
        NdjsonIterator(fp, lineParser)
      )
    );

  // EAGER NDJSON: read all lines, build a JSON array, and traverse with lineParser.Seq()
  public static Parse<Seq<T>> NdjsonAt<T>(
    string name,
    Parse<T> lineParser)
    => FileAt(name).Filter(fp =>
    {
      try
      {
        using var stream = fp.OpenRead();
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (true)
        {
          var line = reader.ReadLine();
          if (line is null) break;
          if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
        }
        if (lines.Count == 0) return Success<Seq<ParsePathErr>, Seq<T>>(Empty);
        var jsonArray = "[" + string.Join(",", lines) + "]";
        var node = System.Text.Json.Nodes.JsonNode.Parse(jsonArray);
        if (node is null) return Success<Seq<ParsePathErr>, Seq<T>>(Empty);
        return lineParser.Seq().ParseJsonNode()(node);
      }
      catch (Exception ex)
      {
        return Fail<Seq<ParsePathErr>, Seq<T>>([ new ParsePathErr($"Invalid NDJSON: {ex.Message}", typeof(T).Name, Some((object)name), []) ]);
      }
    });

  private static async IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>> NdjsonIterator<T>(
    FilePart fp,
    Parse<T> lineParser,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await foreach (var item in OwnEach(
      open: ct => Task.FromResult(ReadLines(new StreamReader(fp.OpenRead()), fp.OpenRead(), ct)),
      clone: (string s) => s
    ).WithCancellation(cancellationToken))
    {
      var idx = item.Index;
      var json = item.Value; // owned string per element
      Validation<Seq<ParsePathErr>, T> result;
      try
      {
        using var doc = JsonDocument.Parse(json);
        var elem = doc.RootElement;
        result = lineParser
          .ParseJson()(elem)
          .MapFail(es => es.Map(e => e.WithPrefix([idx.ToString()])));
      }
      catch (Exception ex)
      {
        result = Fail<Seq<ParsePathErr>, T>([ new ParsePathErr($"Invalid JSON: {ex.Message}", typeof(T).Name, None, []) ]);
      }
      yield return result;
    }
  }

  // General, reusable clone-on-yield combinator for streamed resources
  private static async IAsyncEnumerable<Indexed<TOwned>> OwnEach<TBorrowed, TOwned>(
    Func<CancellationToken, Task<IAsyncEnumerable<TBorrowed>>> open,
    Func<TBorrowed, TOwned> clone,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var idx = 0;
    var source = await open(cancellationToken);
    await foreach (var borrowed in source.WithCancellation(cancellationToken))
    {
      yield return new Indexed<TOwned>(idx++, clone(borrowed));
    }
  }

  // Read lines as an async sequence; ensures reader/stream lifetime ends with the sequence
  private static async IAsyncEnumerable<string> ReadLines(StreamReader reader, Stream stream, [EnumeratorCancellation] CancellationToken ct)
  {
    using (stream)
    using (reader)
    {
      while (!reader.EndOfStream)
      {
        var line = await reader.ReadLineAsync();
        if (line is null) yield break;
        if (string.IsNullOrWhiteSpace(line)) continue;
        yield return line;
      }
    }
  }

  private readonly record struct Indexed<T>(int Index, T Value);

  // Minimal CSV parser: handles commas, CRLF/LF rows, and quoted fields with doubled quotes
  private static List<string[]> ParseCsv(string text)
  {
    var rows = new List<string[]>();
    var cur = new List<string>();
    var sb = new System.Text.StringBuilder();
    bool inQuotes = false;

    for (int i = 0; i < text.Length; i++)
    {
      var ch = text[i];
      if (inQuotes)
      {
        if (ch == '"')
        {
          // doubled quote inside quoted field
          if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
          else { inQuotes = false; }
        }
        else sb.Append(ch);
      }
      else
      {
        if (ch == '"') { inQuotes = true; }
        else if (ch == ',') { cur.Add(sb.ToString()); sb.Clear(); }
        else if (ch == '\r') { /* ignore, handle on \n */ }
        else if (ch == '\n') { cur.Add(sb.ToString()); sb.Clear(); rows.Add(cur.ToArray()); cur.Clear(); }
        else sb.Append(ch);
      }
    }
    // add final row only if there is content buffered (avoid spurious empty row)
    if (sb.Length > 0 || cur.Count > 0)
    {
      cur.Add(sb.ToString());
      rows.Add(cur.ToArray());
    }
    return rows;
  }

  private static Dictionary<string, object?> ToDict(IReadOnlyList<string> header, string[] row)
  {
    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    var n = Math.Min(header.Count, row.Length);
    for (int i = 0; i < n; i++) dict[header[i]] = row[i];
    // Extra row cells without header names are ignored; missing cells remain absent
    return dict;
  }

  private static Dictionary<string, object?> ToIndexedDict(string[] row)
  {
    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < row.Length; i++) dict[i.ToString()] = row[i];
    return dict;
  }
}
