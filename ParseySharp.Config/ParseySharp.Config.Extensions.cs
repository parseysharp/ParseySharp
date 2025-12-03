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

public record ParseOptionsError(string SectionName, Seq<ParsePathErr> Errors);

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
      Fail: errs => throw errs.ToConfigurationBindingException<T>(sectionName),
      Succ: value => builder.Services.AddSingleton<IOptions<T>>(Options.Create(value))
    );

    return builder;
  }

  public static Either<ParseOptionsError, T> ParseOptions<T>(
    this IConfiguration configuration,
    string sectionName,
    Parse<T> parser
  ) =>
    Optional(sectionName)
      .Filter(n => !string.IsNullOrWhiteSpace(n))
      .Match(
        None: () => parser.ParseConfiguration()(configuration),
        Some: n => parser.ParseConfigSection()(configuration.GetSection(n))
      )
      .ToEither()
      .MapLeft(errs => new ParseOptionsError(sectionName, errs));

  public static T GetOrThrow<T>(this Either<ParseOptionsError, T> result) =>
    result.Match(
      Right: v => v,
      Left: e => throw e.Errors.ToConfigurationBindingException<T>(e.SectionName)
    );

  public static InvalidOperationException ToConfigurationBindingException<T>(
    this Seq<ParsePathErr> errors,
    string? sectionName
  ) =>
    new InvalidOperationException(
      $"\n\nFailed to bind {typeof(T).FullName} from configuration {sectionName ?? "root"}:\n\n{string.Join("\n", errors.Map(x => x.ToString()))}\n\n"
    );
}
