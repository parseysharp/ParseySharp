namespace ParseySharp.AspNetCore;

// Marker type used in doc-only DTOs to indicate a format selector bound to a tag type
public sealed class FormatName<TTag> { }

// Public so the Swashbuckle project can read the registered infos
public static class FileWithFormatDocRegistry
{
  private static readonly System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Collections.Generic.IReadOnlyList<FormatInfo>> _infos
    = new();

  public static void Register(System.Type tag, System.Collections.Generic.IReadOnlyList<FormatInfo> infos)
    => _infos[tag] = infos;

  public static bool TryGetInfos(System.Type tag, out System.Collections.Generic.IReadOnlyList<FormatInfo> infos)
    => _infos.TryGetValue(tag, out infos!);
}

public static class FileWithFormatDocExtensions
{
  // Explicit, fluent registration for docs: binds the spec's format infos (name + content-type) to a tag type
  public static FileWithFormatSpec<TResult> RegisterDocFormats<TTag, TResult>(this FileWithFormatSpec<TResult> spec)
  {
    FileWithFormatDocRegistry.Register(typeof(TTag), spec.FormatInfos);
    return spec;
  }

  // Overload that avoids a generic TTag by accepting a runtime Type for the tag
  public static FileWithFormatSpec<TResult> RegisterDocFormats<TResult>(this FileWithFormatSpec<TResult> spec, System.Type tagType)
  {
    FileWithFormatDocRegistry.Register(tagType, spec.FormatInfos);
    return spec;
  }
}