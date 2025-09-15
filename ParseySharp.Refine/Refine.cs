using System.Runtime.CompilerServices;
using LanguageExt;
using static LanguageExt.Prelude;

namespace ParseySharp.Refine;

public static class Refine
{
  public sealed class Valid
  {
    internal static readonly Valid Instance = new Valid();
    private Valid() { }
  }

  public interface IRefine<T, TBrand>
      where TBrand : struct, IRefine<T, TBrand>
  {
    Refined<T, TBrand> Inner { get; }
    static abstract LanguageExt.Seq<string> Errors(T x);
  }

  public readonly record struct Refined<T, TBrand>(T Value, Valid Proof);

  public static LanguageExt.Either<LanguageExt.Seq<string>, Refined<T, TBrand>>
  Create<T, TBrand>(T x)
      where TBrand : struct, IRefine<T, TBrand>
  {
    var errs = TBrand.Errors(x);
    return errs.IsEmpty
        ? Right(new Refined<T, TBrand>(x, Valid.Instance))
        : Left<LanguageExt.Seq<string>, Refined<T, TBrand>>(errs);
  }
}

public static class RefineAccess
{
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static T Value<T, TBrand>(this Refine.IRefine<T, TBrand> x)
    where TBrand : struct, Refine.IRefine<T, TBrand>
    => x.Inner.Value;
}

public static class StringExtensions
{
  public static Seq<string> ErrWhen(this string x, Func<bool> pred) => pred() ? [x] : [];
  public static Seq<string> ErrUnless(this string x, Func<bool> pred) => !pred() ? [x] : [];
}