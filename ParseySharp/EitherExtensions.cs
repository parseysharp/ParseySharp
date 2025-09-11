public static class EitherExtensions
{
  public static Either<L, R> Try<L, R>(this Func<R> f, Func<Exception, L> left)
  {
    try
    {
      return Right<L, R>(f());
    }
    catch (Exception e)
    {
      return Left<L, R>(left(e));
    }
  }
}