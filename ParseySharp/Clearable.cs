namespace ParseySharp;

// Three-state type for PATCH semantics where "absent" and "explicit null"
// have different meanings:
//   Unchanged — field was not included in the request (don't touch)
//   Cleared   — field was explicitly set to null (erase current value)
//   Set(T)    — field was given a new value
//
// Use .Clearable() instead of .Option() on a parser when a nullable field
// must support all three states. Non-nullable or non-clearable fields can
// stay on .Option() (which collapses absent and null into None).
public abstract record Clearable<T>
{
    public sealed record Unchanged() : Clearable<T>;
    public sealed record Cleared() : Clearable<T>;
    public sealed record Set(T Value) : Clearable<T>;

    
}

public static class Clearable
{
    public static Clearable<T> Unchanged<T>() => new Clearable<T>.Unchanged();
    public static Clearable<T> Cleared<T>() => new Clearable<T>.Cleared();
    public static Clearable<T> Set<T>(T value) => new Clearable<T>.Set(value);

    public static U Match<T, U>(
      this Clearable<T> clearable,
      Func<U> Unchanged,
      Func<U> Cleared,
      Func<T, U> Set
    ) => clearable switch
    {
        Clearable<T>.Unchanged => Unchanged(),
        Clearable<T>.Cleared => Cleared(),
        Clearable<T>.Set s => Set(s.Value),
        _ => throw new InvalidOperationException("Unreachable Clearable branch")
    };

    public static Clearable<U> Map<T, U>(this Clearable<T> clearable, Func<T, U> f) =>
      clearable.Match(
        Unchanged: Clearable.Unchanged<U>,
        Cleared: Clearable.Cleared<U>,
        Set: v => Clearable.Set(f(v))
      );

    public static Option<T> ToOption<T>(this Clearable<T> clearable) =>
      clearable.Match(
        Unchanged: () => None,
        Cleared: () => None,
        Set: v => Some(v)
      );

    public static Clearable<T> FromOption<T>(Option<T> option) =>
      option.Match(
        Some: Clearable.Set,
        None: Clearable.Unchanged<T>
      );
}
