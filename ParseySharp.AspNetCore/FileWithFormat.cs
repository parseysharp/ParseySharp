using System;
using System.Linq;
using LanguageExt;

namespace ParseySharp.AspNetCore;

// Describes a format whose file parser is built from a user-provided shape parser
// Builder receives the uploaded FilePart and the shape parser
public sealed record FormatDef<R, TShape, TResult>(string Name, string ContentType, Reader<R, Func<FilePart, Parse<TShape>, Parse<TResult>>> Build)
{
  public FormatDef<R, TShape, B> Map<B>(Func<TResult, B> f) => new(Name, ContentType, Build.Map<Func<FilePart, Parse<TShape>, Parse<B>>>(run => (file, shape) => run(file, shape).Map(f).As()));
};

public static class FormatDef
{
  public static FormatDef<Unit, TShape, TResult> New<TShape, TResult>(
    string Name,
    string ContentType,
    Func<FilePart, Parse<TShape>, Parse<TResult>> Build)
    => new(Name, ContentType, Reader.pure<Unit, Func<FilePart, Parse<TShape>, Parse<TResult>>>(Build));
};

// Returned by FileWithFormat: composed parser + minimal doc hints via RequestModelExtras
public sealed record FileWithFormatSpec<TResult>(
  Parse<TResult> Parser,
  string FormatField,
  string FileField,
  string[] AllowedFormats,
  IReadOnlyList<FormatInfo> FormatInfos
);

public sealed record FormatInfo(string Name, string ContentType);

public static class FileWithFormat
{
  public static FileWithFormatSpec<TResult> Build<TResult>(
    string prefix,
    Seq<FormatDef<Unit, TResult, TResult>> formats,
    Parse<TResult> shape) =>
    Build<Unit, TResult, TResult>(prefix, formats, shape, unit);

  public static FileWithFormatSpec<TResult> Build<TResult, TShape>(
    string prefix,
    Seq<FormatDef<Unit, TShape, TResult>> formats,
    Parse<TShape> shape) =>
    Build<Unit, TResult, TShape>(prefix, formats, shape, unit);

  // Compose a parser that reads "<prefix>Format" then parses "<prefix>File" using the selected format builder
  public static FileWithFormatSpec<TResult> Build<Deps, TResult, TShape>(
    string prefix,
    Seq<FormatDef<Deps, TShape, TResult>> formats,
    Parse<TShape> shape,
    Deps deps)
  {
    var formatField = prefix + "Format";
    var fileField = prefix + "File";
    var names = formats.Map(f => f.Name).ToArray();
    var infos = formats.Map(f => new FormatInfo(f.Name, f.ContentType)).ToArray();

    // Inline string-enum parser (case-insensitive)
    var formatParser = Parse.As<string>().Filter(s =>
      names.Contains(s, StringComparer.OrdinalIgnoreCase)
        ? Right<string, string>(s)
        : Left<string, string>(
            "Invalid value '" + s + "'. Expected one of: " + string.Join(", ", names)))
      .At(formatField, []);

    var parser = new BindParse<string, TResult>(
      formatParser,
      name =>
        new BindParse<FilePart, TResult>(
          ParseMultipart.FileAt(fileField),
          fp => formats.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                  .Match(
                    None: Parse.Fail<TResult>("Unsupported format: " + name).At(formatField, []),
                    Some: f => f.Build.Run(deps)(fp, shape)
                  )
        )
    );

    return new FileWithFormatSpec<TResult>(parser.As(), formatField, fileField, names, infos);
  }

  public static FileWithFormatSpec<Seq<TShape>> BuildEager<TShape>(
    string prefix,
    Seq<FormatDef<Unit, TShape, Seq<TShape>>> formats,
    Parse<TShape> shape)
    => Build(prefix, formats, shape);

  public static FileWithFormatSpec<IAsyncEnumerable<Validation<Seq<ParsePathErr>, TShape>>> BuildStreaming<TShape>(
    string prefix,
    Seq<FormatDef<Unit, TShape, IAsyncEnumerable<Validation<Seq<ParsePathErr>, TShape>>>> formats,
    Parse<TShape> shape)
    => Build(prefix, formats, shape);

  public static FileWithFormatSpec<Either<Seq<TShape>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, TShape>>>> BuildStreamingWhen<TShape>(
    string prefix,
    Seq<FormatDef<Func<FilePart, bool>, TShape, Either<Seq<TShape>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, TShape>>>>> formats,
    Parse<TShape> shape,
    Func<FilePart, bool> when)
    => Build(prefix, formats, shape, when);
}
