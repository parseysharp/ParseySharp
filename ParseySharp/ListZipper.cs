namespace ParseySharp;

public record ListZipper<A>(Seq<A> Prevs, A Focus, Seq<A> Nexts)
{
  public Option<ListZipper<A>> Next() =>
    Nexts.HeadAndTailSafe().Map(
      r => new ListZipper<A>([Focus] + Prevs, r.Head, Seq(r.Tail))
    );

  public Option<ListZipper<A>> Prev() =>
    Prevs.HeadAndTailSafe().Map(
      l => new ListZipper<A>(Seq(l.Tail), l.Head, [Focus] + Nexts)
    );
  ListZipper<A> Rewind(ListZipper<A> z) =>
    z.Prev().Match(Some: Rewind, None: () => z);


  public Seq<A> ToSeq() =>
    toSeq(Seq(Prevs.Reverse()) + [Focus] + Nexts);


  // --- fold over all positions (left -> right), passing the current zipper each step
  public S Fold<S>(S seed, Func<S, ListZipper<A>, S> f)
  {
    // rewind to the leftmost position via recursion
    // fold forward via recursion
    S Go(S acc, ListZipper<A> z) =>
      z.Next().Match(
          Some: nz => Go(f(acc, z), nz),
          None: () => f(acc, z)
      );

    return Go(seed, Rewind(this));
  }
}

public static class ListZipper
{
  public static Option<ListZipper<A>> FromSeq<A>(Seq<A> xs) =>
    xs.HeadAndTailSafe().Map(
      (x) => new ListZipper<A>([], x.Head, Seq(x.Tail))
    );

  public static ListZipper<A> FromCons<A>(A head, Seq<A> xs) =>
      new([], head, xs);
}