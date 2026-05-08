namespace ParseySharp;

public abstract record FieldUpdate<A>
{
  public sealed record Set(A Value)   : FieldUpdate<A>;
  public sealed record Clear          : FieldUpdate<A>;
  public sealed record Leave          : FieldUpdate<A>;

  public FieldUpdate<U> Map<U>(Func<A, U> f) => this switch
  {
    Set s    => new FieldUpdate<U>.Set(f(s.Value)),
    Clear    => new FieldUpdate<U>.Clear(),
    Leave    => new FieldUpdate<U>.Leave(),
    _        => throw new InvalidOperationException()
  };
}

public static class FieldUpdate
{
  public static FieldUpdate<A> Set<A>(A value) => new FieldUpdate<A>.Set(value);
  public static FieldUpdate<A> Clear<A>()      => new FieldUpdate<A>.Clear();
  public static FieldUpdate<A> Leave<A>()      => new FieldUpdate<A>.Leave();
}
