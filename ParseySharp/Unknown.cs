public abstract record Unknown<T>
{
  public sealed record Value(T Get) : Unknown<T>;
  public sealed record None() : Unknown<T>;

  public Unknown<U> Map<U>(Func<T, U> f) => Unknown.Match(
    this,
    Some: x => Unknown.New(f(x)),
    None: () => new Unknown<U>.None()
  );
}

public static class Unknown
{
  public static Unknown<T> New<T>(T value) => 
    value is null ? new Unknown<T>.None() : new Unknown<T>.Value(value);

  public static Unknown<T> SequenceOption<T>(this Option<Unknown<T>> option) =>
    option.IfNone(() => new Unknown<T>.None());

  public static Option<T> ToOption<T>(this Unknown<T> unknown) =>
    unknown.Match(
      Some: x => Optional(x),
      None: () => None
    );

  public static Unknown<T> UnsafeFromOption<T>(this Option<T> option) =>
    option.Match(
      Some: x => New(x),
      None: () => new Unknown<T>.None()
    );

  public static U Match<T, U>(this Unknown<T> unknown, Func<T, U> Some, Func<U> None) => unknown switch
  {
    Unknown<T>.Value v => Some(v.Get),
    Unknown<T>.None _ => None(),
    _ => throw new Exception("Impossible")
  };
}