namespace ParseySharp;

public sealed class NullableOption<T>(OneOf<Value<T>, Null, Absent> input)
  : OneOfBase<Value<T>, Null, Absent>(input)
{
  public NullableOption<U> Map<U>(Func<T, U> f) => this.Match(
    Value:  v => NullableOption.New(f(v.Get)),
    Null:   _ => NullableOption.Null<U>(),
    Absent: _ => NullableOption.Absent<U>());
}

public static class NullableOption
{
  public static NullableOption<T> Value<T>(T v) => new(new Value<T>(v));
  public static NullableOption<T> Null<T>()     => new(new Null());
  public static NullableOption<T> Absent<T>()   => new(new Absent());

  public static NullableOption<T> New<T>(T value) =>
    value is null ? Null<T>() : Value(value);

  public static Option<T> ToOption<T>(this NullableOption<T> u) =>
    u.Match(
      Value:  v => Optional(v.Get),
      Null:   _ => Option<T>.None,
      Absent: _ => Option<T>.None);
}

public static class NullableOptionExtensions
{
  public static U Match<T, U>(
    this NullableOption<T> u,
    Func<Value<T>, U> Value,
    Func<Null, U>     Null,
    Func<Absent, U>   Absent) =>
    u.Match<U>(Value, Null, Absent);
}
