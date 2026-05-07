using Microsoft.Extensions.Configuration;

namespace ParseySharp.Config;

public static class ParsePathNavConfig
{
  public static readonly ParsePathNav<IConfigurationSection> Config =
    ParsePathNav<IConfigurationSection>.Create(
      Prop: (section, name) =>
        Try.lift(() =>
          Optional(section)
            .Bind(x => Optional(x.GetSection(name)))
            .Filter(SectionExists)
            .Match(
              Some: s => NavStep.Value<IConfigurationSection>(s),
              None: () => NavStep.Absent<IConfigurationSection>())
        )
        .ToEither()
        .Match(
          Left: _ => NavStep.NotApplicable(section),
          Right: r => r
        ),

      Index: (section, i) =>
        Try.lift(() =>
          Optional(section)
            .Filter(_ => i >= 0)
            .Bind(x => Optional(x.GetSection(i.ToString())))
            .Filter(SectionExists)
            .Match(
              Some: s => NavStep.Value<IConfigurationSection>(s),
              None: () => NavStep.Absent<IConfigurationSection>())
        )
        .ToEither()
        .Match(
          Left: _ => NavStep.NotApplicable(section),
          Right: r => r
        ),

      Unbox: section =>
        Try.lift(() =>
          (Optional(section)
            .Filter(_ => _.GetChildren().Any())
            .Map<object>(x =>
              IsArraySection(x)
                ? OrderByIndex(x.GetChildren())
                : x) |
          Optional(section.Value).Filter(v =>
            !string.IsNullOrWhiteSpace(v))
            .Map<object>(v => v))
            .Match(
              Some: v => UnboxStep.Value(v),
              None: () => UnboxStep.Null()))
        .ToEither()
        .Match(
          Left: _ => UnboxStep.NotApplicable(section),
          Right: r => r
        ),

      CloneNode: s => s
    );

  static bool SectionExists(IConfigurationSection s) => s is not null && (s.Value is not null || s.GetChildren().Any());

  static bool IsArraySection(IConfigurationSection s) => s.GetChildren().Any() && s.GetChildren().All(c => int.TryParse(c.Key, out _));

  static IEnumerable<IConfigurationSection> OrderByIndex(IEnumerable<IConfigurationSection> kids) => kids.OrderBy(c => int.Parse(c.Key));
}
