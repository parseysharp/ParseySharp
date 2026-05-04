using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using ParseySharp.HotChocolate;

namespace ParseySharp.Tests;

public sealed record A(string Name);
public sealed record B(int Code);
public sealed record C(double Value);
public sealed record D(bool Flag);

/// <summary>Used as an "uninhabited" marker in pruning tests.</summary>
public sealed record TestNever;

/// <summary>Single-property record around a DU. With wrappers no longer
/// recognized as a special shape, this is just a plain record whose
/// <c>Value</c> field is a DU position caught by the field walk.</summary>
public sealed record OutcomeRecord(Either<OneOf<A, B>, C> Value);

public sealed record CollapseRecord(Either<TestNever, A> Value);

public sealed record EmptyRecord(Either<TestNever, TestNever> Value);

public sealed record DupRecord(Either<A, A> Value);

public sealed record InnerOneOfBC(OneOf<B, C> Value);

public sealed record OuterAOrInner(OneOf<A, InnerOneOfBC> Value);

public class TypeDecomposerDeepTests
{
    [Fact]
    public void Either_descends_into_both_sides() =>
        Assert.Equal(
            [typeof(A), typeof(B)],
            TypeDecomposer.Decompose<Deep>(typeof(Either<A, B>)));

    [Fact]
    public void OneOf_descends_into_all_arms() =>
        Assert.Equal(
            [typeof(A), typeof(B), typeof(C)],
            TypeDecomposer.Decompose<Deep>(typeof(OneOf<A, B, C>)));

    [Fact]
    public void Nested_either_oneof_flattens_in_source_order() =>
        Assert.Equal(
            [typeof(A), typeof(B), typeof(C)],
            TypeDecomposer.Decompose<Deep>(typeof(Either<OneOf<A, B>, C>)));

    [Fact]
    public void Single_field_record_is_a_leaf_not_a_pierced_wrapper() =>
        Assert.Equal(
            [typeof(OutcomeRecord)],
            TypeDecomposer.Decompose<Deep>(typeof(OutcomeRecord)));

    [Fact]
    public void Validation_is_treated_as_an_opaque_leaf() =>
        Assert.Equal(
            [typeof(Validation<A, C>)],
            TypeDecomposer.Decompose<Deep>(typeof(Validation<A, C>)));

    [Fact]
    public void Unrecognized_type_is_a_single_leaf() =>
        Assert.Equal([typeof(D)], TypeDecomposer.Decompose<Deep>(typeof(D)));

    [Fact]
    public void Duplicates_are_returned_not_thrown()
    {
        var leaves = TypeDecomposer.Decompose<Deep>(typeof(Either<A, A>));
        Assert.Equal([typeof(A), typeof(A)], leaves);
        Assert.Equal([typeof(A)], TypeDecomposer.Duplicates(leaves));
    }

    [Fact]
    public void Uninhabited_arms_are_pruned()
    {
        var uninhabited = new System.Collections.Generic.HashSet<Type> { typeof(TestNever) };
        Assert.Equal(
            [typeof(A)],
            TypeDecomposer.Decompose<Deep>(typeof(Either<TestNever, A>), uninhabited));
    }

    [Fact]
    public void All_uninhabited_yields_empty()
    {
        var uninhabited = new System.Collections.Generic.HashSet<Type> { typeof(TestNever) };
        Assert.Empty(TypeDecomposer.Decompose<Deep>(typeof(Either<TestNever, TestNever>), uninhabited));
    }
}

public class TypeDecomposerShallowTests
{
    [Fact]
    public void Either_yields_immediate_arms_only() =>
        Assert.Equal(
            [typeof(OneOf<A, B>), typeof(C)],
            TypeDecomposer.Decompose<Shallow>(typeof(Either<OneOf<A, B>, C>)));

    [Fact]
    public void OneOf_yields_immediate_arms_only() =>
        Assert.Equal(
            [typeof(A), typeof(InnerOneOfBC)],
            TypeDecomposer.Decompose<Shallow>(typeof(OneOf<A, InnerOneOfBC>)));

    [Fact]
    public void Single_field_record_is_a_leaf_not_decomposed() =>
        Assert.Equal(
            [typeof(OutcomeRecord)],
            TypeDecomposer.Decompose<Shallow>(typeof(OutcomeRecord)));

    [Fact]
    public void Uninhabited_immediate_arms_are_pruned()
    {
        var uninhabited = new System.Collections.Generic.HashSet<Type> { typeof(TestNever) };
        Assert.Equal(
            [typeof(A)],
            TypeDecomposer.Decompose<Shallow>(typeof(Either<TestNever, A>), uninhabited));
    }
}

public class DuTypeNamingTests
{
    [Fact]
    public void Output_with_parent_uses_parent_plus_pascal_field() =>
        Assert.Equal(
            "CheckoutOutcomeValue",
            DuTypeNaming.NameFor<OutputNaming>("CheckoutOutcome", "value", [typeof(A), typeof(B)]));

    [Fact]
    public void Input_with_parent_uses_parent_plus_pascal_field() =>
        Assert.Equal(
            "DiscountInputValue",
            DuTypeNaming.NameFor<InputNaming>("DiscountInput", "value", [typeof(A), typeof(B)]));

    [Fact]
    public void Argument_position_treats_field_as_parent_and_argument_as_field() =>
        Assert.Equal(
            "EchoInput",
            DuTypeNaming.NameFor<InputNaming>("echo", "input", [typeof(A), typeof(B)]));

    [Fact]
    public void Output_no_parent_falls_back_to_alphabetical_Or_join() =>
        Assert.Equal(
            "AOrBOrC",
            DuTypeNaming.NameFor<OutputNaming>(null, null, [typeof(C), typeof(A), typeof(B)]));

    [Fact]
    public void Input_no_parent_falls_back_to_OneOf_concat() =>
        Assert.Equal(
            "OneOfABC",
            DuTypeNaming.NameFor<InputNaming>(null, null, [typeof(C), typeof(A), typeof(B)]));

    [Fact]
    public void Bare_naming_is_stable_under_arm_reordering() =>
        Assert.Equal(
            DuTypeNaming.NameFor<OutputNaming>(null, null, [typeof(A), typeof(B), typeof(C)]),
            DuTypeNaming.NameFor<OutputNaming>(null, null, [typeof(C), typeof(B), typeof(A)]));
}

public class ProjectorTests
{
    [Fact]
    public void Bare_OneOf_walks_to_arm()
    {
        var b = new B(7);
        var walker = Projector.BuildShapeWalker(typeof(OneOf<A, B>));
        Assert.Same(b, walker(OneOf<A, B>.FromT1(b)));
    }

    [Fact]
    public void Bare_Either_walks_both_branches()
    {
        var a = new A("alpha");
        var c = new C(1.5);
        var walker = Projector.BuildShapeWalker(typeof(Either<A, C>));
        Assert.Same(a, walker(Left<A, C>(a)));
        Assert.Same(c, walker(Right<A, C>(c)));
    }

    [Fact]
    public void Single_field_record_walker_is_identity_because_records_are_leaves()
    {
        var walker = Projector.BuildShapeWalker(typeof(OutcomeRecord));
        var w = new OutcomeRecord(Right<OneOf<A, B>, C>(new C(1.0)));
        Assert.Same(w, walker(w));
    }

    [Fact]
    public void List_of_either_walks_each_element_to_its_leaf()
    {
        var walker = Projector.BuildShapeWalker(typeof(IReadOnlyList<Either<A, C>>));
        var a = new A("alpha");
        var c = new C(1.5);
        var input = new[] { Left<A, C>(a), Right<A, C>(c) };

        var result = (IReadOnlyList<object?>)walker(input)!;
        Assert.Same(a, result[0]);
        Assert.Same(c, result[1]);
    }

    [Fact]
    public void Null_either_value_is_passed_through()
    {
        var walker = Projector.BuildShapeWalker(typeof(Either<A, C>));
        Assert.Null(walker(null));
    }

    [Fact]
    public void Non_DU_leaf_is_identity()
    {
        var walker = Projector.BuildShapeWalker(typeof(A));
        var a = new A("hi");
        Assert.Same(a, walker(a));
    }

    [Fact]
    public void Validation_walker_is_identity_because_it_is_not_a_recognized_DU()
    {
        var walker = Projector.BuildShapeWalker(typeof(Validation<Seq<string>, C>));
        var v = Success<Seq<string>, C>(new C(1.0));
        Assert.Same(v, walker(v));
    }
}

public class DuOutputSchemaTests
{
    static ISchemaBuilder NewSchemaBuilder(Action<ParseySharpHotChocolateOptions>? configure = null)
    {
        var options = new ParseySharpHotChocolateOptions();
        configure?.Invoke(options);
        return SchemaBuilder.New()
            .TryAddTypeInterceptor(new DuOutputTypeInterceptor(options))
            .TryAddTypeInterceptor(new DuInputTypeInterceptor(options));
    }

    public sealed class BareUnionQuery
    {
        public OneOf<A, B> AorB() => OneOf<A, B>.FromT0(new A("x"));
    }

    [Fact]
    public void Bare_DU_at_top_level_field_uses_parent_derived_name()
    {
        var schema = NewSchemaBuilder().AddQueryType<BareUnionQuery>().Create();
        var union = (UnionType)schema.Types["BareUnionQueryAorB"];
        Assert.Contains(union.Types, t => t.Name == "A");
        Assert.Contains(union.Types, t => t.Name == "B");
    }

    public sealed class RecordReturningQuery
    {
        public OutcomeRecord Outcome() => new(Right<OneOf<A, B>, C>(new C(1.0)));
    }

    [Fact]
    public void Record_with_DU_field_yields_object_type_and_parent_named_union()
    {
        var schema = NewSchemaBuilder().AddQueryType<RecordReturningQuery>().Create();
        var obj = (ObjectType)schema.Types[nameof(OutcomeRecord)];
        Assert.Equal("OutcomeRecordValue!", obj.Fields["value"].Type.ToString());

        var union = (UnionType)schema.Types["OutcomeRecordValue"];
        Assert.Equal(3, union.Types.Count);
    }

    public sealed class ListOfRecordsQuery
    {
        public IReadOnlyList<OutcomeRecord> Outcomes() =>
        [
            new(Right<OneOf<A, B>, C>(new C(1.0))),
            new(Left<OneOf<A, B>, C>(OneOf<A, B>.FromT0(new A("x")))),
        ];
    }

    [Fact]
    public void List_of_records_resolves_to_list_of_object_type()
    {
        var schema = NewSchemaBuilder().AddQueryType<ListOfRecordsQuery>().Create();
        var field = schema.QueryType.Fields["outcomes"];
        Assert.Equal($"[{nameof(OutcomeRecord)}!]!", field.Type.ToString());
    }

    public sealed class CollapseQuery
    {
        public CollapseRecord Outcome() => new(Right<TestNever, A>(new A("x")));
    }

    [Fact]
    public void Single_inhabited_leaf_collapses_DU_field_to_leaf_object_type()
    {
        var schema = NewSchemaBuilder(o => o.RegisterUninhabited<TestNever>())
            .AddQueryType<CollapseQuery>()
            .Create();
        var obj = (ObjectType)schema.Types[nameof(CollapseRecord)];
        Assert.Equal("A!", obj.Fields["value"].Type.ToString());
    }

    public sealed class EmptyQuery
    {
        public EmptyRecord Outcome() => new(Left<TestNever, TestNever>(new TestNever()));
    }

    [Fact]
    public void Zero_inhabited_leaves_throws_schema_exception()
    {
        var ex = Assert.Throws<SchemaException>(() =>
            NewSchemaBuilder(o => o.RegisterUninhabited<TestNever>())
                .AddQueryType<EmptyQuery>()
                .Create());
        Assert.Contains("no inhabited", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class DuplicateQuery
    {
        public DupRecord Outcome() => new(Left<A, A>(new A("x")));
    }

    [Fact]
    public void Duplicate_leaves_throws_schema_exception()
    {
        var ex = Assert.Throws<SchemaException>(() =>
            NewSchemaBuilder().AddQueryType<DuplicateQuery>().Create());
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(A), ex.Message);
    }

    public sealed class SharedRecordQuery
    {
        public OutcomeRecord First() => new(Right<OneOf<A, B>, C>(new C(1.0)));
        public OutcomeRecord Second() => new(Right<OneOf<A, B>, C>(new C(2.0)));
    }

    [Fact]
    public void Two_fields_returning_the_same_record_share_one_object_type_and_one_union()
    {
        var schema = NewSchemaBuilder().AddQueryType<SharedRecordQuery>().Create();
        Assert.Single(schema.Types.OfType<ObjectType>(), o => o.Name == nameof(OutcomeRecord));
        Assert.Single(schema.Types.OfType<UnionType>(), u => u.Name == "OutcomeRecordValue");
    }
}

public class DuInputSchemaTests
{
    static ISchemaBuilder NewSchemaBuilder(Action<ParseySharpHotChocolateOptions>? configure = null)
    {
        var options = new ParseySharpHotChocolateOptions();
        configure?.Invoke(options);
        return SchemaBuilder.New()
            .TryAddTypeInterceptor(new DuOutputTypeInterceptor(options))
            .TryAddTypeInterceptor(new DuInputTypeInterceptor(options));
    }

    public sealed class BareInputQuery
    {
        public string Echo(OneOf<A, B> input) => input.Match(a => a.Name, b => b.Code.ToString());
    }

    [Fact]
    public void Bare_DU_argument_uses_field_plus_argument_name()
    {
        var schema = NewSchemaBuilder().AddQueryType<BareInputQuery>().Create();
        var input = (InputObjectType)schema.Types["EchoInput"];
        Assert.Contains(input.Directives, d => d.Type.Name == "oneOf");
        Assert.Equal(2, input.Fields.Count);
    }

    public sealed class RecordInputQuery
    {
        public string Echo(OutcomeRecord input) => "ok";
    }

    [Fact]
    public void Record_with_DU_field_yields_input_object_and_parent_named_oneOf()
    {
        var schema = NewSchemaBuilder().AddQueryType<RecordInputQuery>().Create();
        var outer = (InputObjectType)schema.Types["OutcomeRecordInput"];
        Assert.DoesNotContain(outer.Directives, d => d.Type.Name == "oneOf");
        Assert.Equal("OutcomeRecordInputValue!", outer.Fields["value"].Type.ToString());

        var inner = (InputObjectType)schema.Types["OutcomeRecordInputValue"];
        Assert.Contains(inner.Directives, d => d.Type.Name == "oneOf");
        Assert.Equal(2, inner.Fields.Count);
    }

    public sealed class NestedInputQuery
    {
        public string Echo(OuterAOrInner input) => "ok";
    }

    [Fact]
    public void Nested_DU_arms_produce_nested_oneOf_inputs_one_level_per_record()
    {
        var schema = NewSchemaBuilder().AddQueryType<NestedInputQuery>().Create();

        var outer = (InputObjectType)schema.Types["OuterAOrInnerInput"];
        Assert.DoesNotContain(outer.Directives, d => d.Type.Name == "oneOf");
        Assert.Equal("OuterAOrInnerInputValue!", outer.Fields["value"].Type.ToString());

        var outerOneOf = (InputObjectType)schema.Types["OuterAOrInnerInputValue"];
        Assert.Contains(outerOneOf.Directives, d => d.Type.Name == "oneOf");
        Assert.Equal(2, outerOneOf.Fields.Count);

        var inner = (InputObjectType)schema.Types["InnerOneOfBCInput"];
        Assert.DoesNotContain(inner.Directives, d => d.Type.Name == "oneOf");
        Assert.Equal("InnerOneOfBCInputValue!", inner.Fields["value"].Type.ToString());

        var innerOneOf = (InputObjectType)schema.Types["InnerOneOfBCInputValue"];
        Assert.Contains(innerOneOf.Directives, d => d.Type.Name == "oneOf");
        Assert.Equal(2, innerOneOf.Fields.Count);
    }
}

/// <summary>
/// Auto-binding via <c>ResolveParsedArgument(name, parser, ...)</c> sends
/// <c>typeof(T)</c> through Hot Chocolate's CLR-type inference — the same path
/// method-bound parameters take. The DU interceptor's per-configuration field
/// walk catches DU positions at any depth and the modifier-preserving rewrite
/// keeps list/nullable structure intact, so these schema-shape tests pin the
/// auto-bind path against regression for every common <c>T</c> shape.
/// </summary>
public class ResolveParsedArgumentAutoBindTests
{
    static ISchemaBuilder NewSchemaBuilder()
    {
        var options = new ParseySharpHotChocolateOptions();
        return SchemaBuilder.New()
            .TryAddTypeInterceptor(new DuOutputTypeInterceptor(options))
            .TryAddTypeInterceptor(new DuInputTypeInterceptor(options));
    }

    static readonly Parse<A> AParser = Parse.As<string>().At("name").Map(n => new A(n)).As();

    public sealed record AutoBindRecord(A First, A Second);

    [Fact]
    public void Record_T_auto_binds_via_clr_type_inference()
    {
        var parser = (
            AParser.At("first"),
            AParser.At("second")
        ).Apply((first, second) => new AutoBindRecord(first, second)).As();

        var schema = NewSchemaBuilder().AddQueryType(d => d
            .Name("Query")
            .Field("echo")
            .ResolveParsedArgument("input", parser, _ => new ValueTask<string>("ok"))
            .Type<StringType>()).Create();

        var input = (InputObjectType)schema.Types["AutoBindRecordInput"];
        Assert.Equal("AInput!", input.Fields["first"].Type.ToString());
        Assert.Equal("AInput!", input.Fields["second"].Type.ToString());
    }

    [Fact]
    public void Record_with_nested_DU_T_auto_binds_and_projects_oneOf()
    {
        var parser =
            AParser.At("a")
                .Map(a => OneOf<A, InnerOneOfBC>.FromT0(a))
                .As()
                .At("value")
                .Map(o => new OuterAOrInner(o))
                .As();

        var schema = NewSchemaBuilder().AddQueryType(d => d
            .Name("Query")
            .Field("echo")
            .ResolveParsedArgument("input", parser, _ => new ValueTask<string>("ok"))
            .Type<StringType>()).Create();

        var outer = (InputObjectType)schema.Types["OuterAOrInnerInput"];
        Assert.Equal("OuterAOrInnerInputValue!", outer.Fields["value"].Type.ToString());

        var oneOf = (InputObjectType)schema.Types["OuterAOrInnerInputValue"];
        Assert.Contains(oneOf.Directives, x => x.Type.Name == "oneOf");
    }

    [Fact]
    public void Bare_DU_T_auto_binds_via_argument_walk_just_like_method_binding()
    {
        var parser = AParser.At("a").Map(a => OneOf<A, B>.FromT0(a)).As();

        var schema = NewSchemaBuilder().AddQueryType(d => d
            .Name("Query")
            .Field("echo")
            .ResolveParsedArgument("input", parser, _ => new ValueTask<string>("ok"))
            .Type<StringType>()).Create();

        var input = (InputObjectType)schema.Types["EchoInput"];
        Assert.Contains(input.Directives, x => x.Type.Name == "oneOf");
        Assert.Equal(2, input.Fields.Count);
    }

    [Fact]
    public void List_of_record_T_auto_binds_with_list_modifier_preserved()
    {
        var parser = AParser.Seq().Map(xs => (IReadOnlyList<A>)xs.ToArray()).As();

        var schema = NewSchemaBuilder().AddQueryType(d => d
            .Name("Query")
            .Field("echo")
            .ResolveParsedArgument("input", parser, _ => new ValueTask<string>("ok"))
            .Type<StringType>()).Create();

        var arg = schema.QueryType.Fields["echo"].Arguments["input"];
        Assert.StartsWith("[AInput", arg.Type.ToString());
        Assert.EndsWith("]", arg.Type.ToString());
    }
}
