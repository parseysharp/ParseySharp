namespace ParseySharp.AspNetCore;

public static class AcceptFileFormatYaml
{
  // YAML -> Seq<T> (expects top-level sequence)
  public static FormatDef<Unit, T, LanguageExt.Seq<T>> Yaml<T>()
    => FormatDef.New<T, Seq<T>>(
      Name: "yaml",
      ContentType: "application/x-yaml",
      Build: (fileField, shape) => ParseMultipartYaml.YamlAt(fileField, shape.Seq())
    );

  // YAML (streaming-when) -> Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>> (always eager Left)
  public static FormatDef<Func<FilePart, bool>, T, Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>> YamlStreamingWhen<T>()
    => new(
      Name: "yaml",
      ContentType: "application/x-yaml",
      Build: Reader.pure<Func<FilePart, bool>, Func<FilePart, Parse<T>, Parse<Either<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>>>>(
        (file, shape) => ParseMultipartYaml.YamlAt(file, shape.Seq())
          .Map(seq => Left<Seq<T>, IAsyncEnumerable<Validation<Seq<ParsePathErr>, T>>>(seq))
          .As()
      )
    );
}
