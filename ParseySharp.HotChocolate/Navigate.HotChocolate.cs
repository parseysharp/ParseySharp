namespace ParseySharp;

public static class ParsePathNavHotChocolate
{
    static bool IsHcNull(IValueNode? n) => n is null or NullValueNode;

    public static readonly ParsePathNav<IValueNode> HotChocolate =
        ParsePathNav<IValueNode>.Create(
            Prop: (node, name) =>
                node is ObjectValueNode obj
                    ? Optional(obj.Fields.FirstOrDefault(f => f.Name.Value == name))
                        .Match(
                            Some: f => Optional(f.Value).Filter(v => !IsHcNull(v)).Match(
                                Some: v => NavStep.Value<IValueNode>(v),
                                None: () => NavStep.Null<IValueNode>()),
                            None: () => NavStep.Absent<IValueNode>())
                    : NavStep.NotApplicable(node),

            Index: (node, i) =>
                node is ListValueNode list && i >= 0
                    ? (i < list.Items.Count
                        ? Optional(list.Items[i]).Filter(v => !IsHcNull(v)).Match(
                            Some: v => NavStep.Value<IValueNode>(v),
                            None: () => NavStep.Null<IValueNode>())
                        : NavStep.Absent<IValueNode>())
                    : NavStep.NotApplicable(node),

            Unbox: node => node switch
            {
                null or NullValueNode
                    => UnboxStep.Null(),

                ListValueNode list
                    => UnboxStep.Value(list.Items),

                ObjectValueNode
                    => UnboxStep.Value(node),

                StringValueNode s
                    => UnboxStep.Value(s.Value),

                BooleanValueNode b
                    => UnboxStep.Value(b.Value),

                EnumValueNode e
                    => UnboxStep.Value(e.Value),

                IntValueNode n => UnboxInt(n),

                FloatValueNode f => UnboxFloat(f),

                _ => UnboxStep.NotApplicable(node)
            },

            CloneNode: x => x
        );

    static UnboxStep UnboxInt(IntValueNode n) =>
        ((Func<UnboxStep>)(() =>
        {
            var l = n.ToInt64();
            return (l <= int.MaxValue && l >= int.MinValue)
                ? UnboxStep.Value((int)l)
                : UnboxStep.Value(l);
        }))
        .Try<UnboxStep, UnboxStep>(_ => UnboxStep.Value(n.ToString()))
        .Match(
            Left: fallback => fallback,
            Right: r => r
        );

    static UnboxStep UnboxFloat(FloatValueNode f) =>
        ((Func<UnboxStep>)(() => UnboxStep.Value(f.ToDouble())))
        .Try<UnboxStep, UnboxStep>(_ => UnboxStep.Value(f.ToString()))
        .Match(
            Left: fallback => fallback,
            Right: r => r
        );
}

public static class ParseHotChocolateExtensions
{
    public static Func<IValueNode, Validation<Seq<ParsePathErr>, A>> ParseHotChocolate<A>(this Parse<A> parser) =>
        ParseExtensions.RunWithNav(parser, ParsePathNavHotChocolate.HotChocolate);
}
