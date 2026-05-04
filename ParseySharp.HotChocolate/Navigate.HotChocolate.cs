namespace ParseySharp;

public static class ParsePathNavHotChocolate
{
    public static readonly ParsePathNav<IValueNode> HotChocolate =
        ParsePathNav<IValueNode>.Create(
            Prop: (node, name) =>
                node is ObjectValueNode obj
                    ? Right<Unknown<IValueNode>, Option<IValueNode>>(
                        Optional(obj.Fields.FirstOrDefault(f => f.Name.Value == name)).Map(f => f.Value))
                    : Left<Unknown<IValueNode>, Option<IValueNode>>(Unknown.New(node)),

            Index: (node, i) =>
                node is ListValueNode list && i >= 0
                    ? (i < list.Items.Count
                        ? Right<Unknown<IValueNode>, Option<IValueNode>>(Optional(list.Items[i]))
                        : Right<Unknown<IValueNode>, Option<IValueNode>>(None))
                    : Left<Unknown<IValueNode>, Option<IValueNode>>(Unknown.New(node)),

            Unbox: node => node switch
            {
                null or NullValueNode
                    => Right<Unknown<IValueNode>, Unknown<object>>(Unknown.UnsafeFromOption<object>(None)),

                ListValueNode list
                    => Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>(list.Items)),

                ObjectValueNode
                    => Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>(node)),

                StringValueNode s
                    => Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>(s.Value)),

                BooleanValueNode b
                    => Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>(b.Value)),

                EnumValueNode e
                    => Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>(e.Value)),

                IntValueNode n => UnboxInt(n),

                FloatValueNode f => UnboxFloat(f),

                _ => Left<Unknown<IValueNode>, Unknown<object>>(Unknown.New(node))
            },

            CloneNode: x => x
        );

    static Either<Unknown<IValueNode>, Unknown<object>> UnboxInt(IntValueNode n) =>
        ((Func<Either<Unknown<IValueNode>, Unknown<object>>>)(() =>
        {
            var l = n.ToInt64();
            return (l <= int.MaxValue && l >= int.MinValue)
                ? Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>((int)l))
                : Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>(l));
        }))
        .Try<Unknown<IValueNode>, Either<Unknown<IValueNode>, Unknown<object>>>(_ => Unknown.New<IValueNode>(n))
        .Match(
            Left: _ => Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>(n.ToString())),
            Right: r => r
        );

    static Either<Unknown<IValueNode>, Unknown<object>> UnboxFloat(FloatValueNode f) =>
        ((Func<Either<Unknown<IValueNode>, Unknown<object>>>)(() =>
            Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>(f.ToDouble()))))
        .Try<Unknown<IValueNode>, Either<Unknown<IValueNode>, Unknown<object>>>(_ => Unknown.New<IValueNode>(f))
        .Match(
            Left: _ => Right<Unknown<IValueNode>, Unknown<object>>(Unknown.New<object>(f.ToString())),
            Right: r => r
        );
}

public static class ParseHotChocolateExtensions
{
    public static Func<IValueNode, Validation<Seq<ParsePathErr>, A>> ParseHotChocolate<A>(this Parse<A> parser) =>
        ParseExtensions.RunWithNav(parser, ParsePathNavHotChocolate.HotChocolate);
}
