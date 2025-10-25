using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using ParseySharp.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ParseySharp;

public static class ParseConfigExtensions
{
  public static Func<IConfigurationSection, Validation<Seq<ParsePathErr>, A>> ParseConfigSection<A>(this Parse<A> parser) =>
    ParseExtensions.RunWithNav(parser, ParsePathNavConfig.Config);

  public static Func<IConfiguration, Validation<Seq<ParsePathErr>, A>> ParseConfiguration<A>(this Parse<A> parser) =>
    config => parser.ParseConfigSection<A>()(new RootSection(config));

  // Adapter to expose IConfiguration root as a virtual section with children
  private sealed class RootSection(IConfiguration cfg) : IConfigurationSection
  {
    public string Key => string.Empty;
    public string Path => string.Empty;
    public string? Value { get => null; set => throw new NotSupportedException(); }

    public string? this[string key]
    {
      get => cfg[key];
      set => cfg[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => cfg.GetChildren();
    public IChangeToken GetReloadToken() => cfg.GetReloadToken();
    public IConfigurationSection GetSection(string key) => cfg.GetSection(key);
  }
}


// TODO - support ChangeToken
public static class ParseConfigOptionsExtensions
{
  public static OptionsBuilder<T> ParseWith<T>(
    this OptionsBuilder<T> builder,
    IConfiguration config,
    Parse<T> parser,
    string? sectionName = null)
    where T : class
  {
    var v =
      Optional(sectionName)
        .Filter(n => !string.IsNullOrWhiteSpace(n))
        .Match(
          None: () => parser.ParseConfiguration<T>()(config),
          Some: n => parser.ParseConfigSection<T>()(config.GetSection(n))
        );

    v.Match(
      Fail: errs => throw new InvalidOperationException(
        $"\n\nFailed to bind {
          typeof(T).FullName} from configuration {
          sectionName ?? "root"}:\n\n{
          string.Join("\n", errs.Map(x => x.ToString()))}\n\n"
      ),
      Succ: value => builder.Services.AddSingleton<IOptions<T>>(Options.Create(value))
    );

    return builder;
  }
}
