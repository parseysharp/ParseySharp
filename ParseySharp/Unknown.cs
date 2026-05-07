namespace ParseySharp;

public sealed class Unknown<T>(OneOf<Value<T>, Null, Absent> input)
  : OneOfBase<Value<T>, Null, Absent>(input)
{
  public Unknown<U> Map<U>(Func<T, U> f) => this.Match(
    Value:  v => Unknown.New(f(v.Get)),
    Null:   _ => Unknown.Null<U>(),
    Absent: _ => Unknown.Absent<U>());
}

public static class Unknown
{
  public static Unknown<T> Value<T>(T v) => new(new Value<T>(v));
  public static Unknown<T> Null<T>()     => new(new Null());
  public static Unknown<T> Absent<T>()   => new(new Absent());

  public static Unknown<T> New<T>(T value) =>
    value is null ? Null<T>() : Value(value);

  public static Option<T> ToOption<T>(this Unknown<T> u) =>
    u.Match(
      Value:  v => Optional(v.Get),
      Null:   _ => Option<T>.None,
      Absent: _ => Option<T>.None);
}

public static class UnknownExtensions
{
  public static U Match<T, U>(
    this Unknown<T> u,
    Func<Value<T>, U> Value,
    Func<Null, U>     Null,
    Func<Absent, U>   Absent) =>
    u.Match<U>(Value, Null, Absent);
}
