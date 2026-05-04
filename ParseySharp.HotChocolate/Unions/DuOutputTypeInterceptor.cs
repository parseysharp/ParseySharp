using System.Reflection;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;

namespace ParseySharp.HotChocolate;

/// <summary>
/// Walks every output field at type-discovery time, finds discriminated-union
/// positions structurally (<c>Either&lt;,&gt;</c> or <c>OneOf&lt;,..&gt;</c>) in
/// the field's CLR result-type tree, rewrites <c>field.Type</c> to a syntax
/// type reference that preserves list/non-null structure, and lazily yields the
/// synthesized <see cref="UnionType"/> (or collapses to the single inhabited
/// leaf) via Hot Chocolate's own type-creation pipeline. Naming is derived from
/// the field's owning type + its name (<c>&lt;OwningType&gt;&lt;PascalField&gt;</c>);
/// alphabetical concat is the no-parent fallback. A result formatter on the
/// same field unwraps <c>Either</c>/<c>OneOf</c> instances at runtime so the
/// leaf object reaches HC's <c>__typename</c> dispatch.
/// </summary>
internal sealed class DuOutputTypeInterceptor : TypeInterceptor
{
    static readonly MethodInfo UnionDescriptorTypeGenericMethod =
        typeof(IUnionTypeDescriptor)
            .GetMethods()
            .First(m =>
                m.Name == "Type"
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 0);

    readonly ParseySharpHotChocolateOptions _options;
    readonly Dictionary<string, ResolvedDu> _byName = new();

    public DuOutputTypeInterceptor(ParseySharpHotChocolateOptions options)
    {
        _options = options;
    }

    public override void OnBeforeRegisterDependencies(
        ITypeDiscoveryContext discoveryContext,
        TypeSystemConfiguration configuration) =>
        toSeq(FieldsOf(configuration)).Iter(f => ProcessOutputField(OwningName(configuration), f));

    static IEnumerable<OutputFieldConfiguration> FieldsOf(TypeSystemConfiguration c) =>
        c switch
        {
            ObjectTypeConfiguration o    => o.Fields,
            InterfaceTypeConfiguration i => i.Fields,
            _ => Enumerable.Empty<OutputFieldConfiguration>(),
        };

    static string? OwningName(TypeSystemConfiguration c) =>
        c switch
        {
            ObjectTypeConfiguration { Name: { Length: > 0 } n } => n,
            InterfaceTypeConfiguration { Name: { Length: > 0 } n } => n,
            ObjectTypeConfiguration { RuntimeType: { } rt } => rt.Name,
            InterfaceTypeConfiguration { RuntimeType: { } rt } => rt.Name,
            _ => null,
        };

    void ProcessOutputField(string? owningName, OutputFieldConfiguration field) =>
        TypeShapeAnalyzer.Analyze(field.Type).Iter(d => ApplyDu(owningName, field, d));

    void ApplyDu(string? owningName, OutputFieldConfiguration field, DuShape shape)
    {
        var resolved = ResolveDu(shape, owningName, field.Name);
        field.Type = RewriteToNamed((ExtendedTypeReference)field.Type!, resolved.SchemaName, resolved.Factory);
        if (field is ObjectFieldConfiguration objField)
        {
            objField.FormatterConfigurations.Add(
                new ResultFormatterConfiguration(
                    DuUnwrapFormatter.For(shape.ClrShape),
                    isRepeatable: false,
                    key: $"ParseySharp.DuUnwrap:{shape.ClrShape.FullName ?? shape.ClrShape.Name}"));
        }
    }

    ResolvedDu ResolveDu(DuShape shape, string? owningName, string? fieldName)
    {
        var leaves = ValidateLeaves(
            shape,
            TypeDecomposer.Decompose<Deep>(shape.DuRoot, _options.UninhabitedTypes));
        var name = leaves.Count == 1
            ? leaves[0].Name
            : DuTypeNaming.NameFor<OutputNaming>(owningName, fieldName, leaves);
        return _byName.TryGetValue(name, out var cached)
            ? cached
            : _byName[name] = BuildResolved(name, leaves);
    }

    static IReadOnlyList<Type> ValidateLeaves(DuShape shape, IReadOnlyList<Type> leaves) =>
        leaves switch
        {
            { Count: 0 } => throw new SchemaException(SchemaErrorBuilder.New()
                .SetMessage(
                    "Discriminated union {0} has no inhabited leaves after pruning.",
                    shape.DuRoot.FullName ?? shape.DuRoot.Name)
                .Build()),
            _ when TypeDecomposer.Duplicates(leaves) is { Count: > 0 } dups
                => throw new SchemaException(SchemaErrorBuilder.New()
                    .SetMessage(
                        "Discriminated union {0} has duplicate leaf type(s) [{1}] in its decomposition. "
                            + "Distinct GraphQL union members require distinct CLR leaf types.",
                        shape.DuRoot.FullName ?? shape.DuRoot.Name,
                        string.Join(", ", dups.Select(d => d.FullName ?? d.Name)))
                    .Build()),
            _ => leaves,
        };

    static ResolvedDu BuildResolved(string name, IReadOnlyList<Type> leaves) =>
        leaves.Count == 1 ? BuildCollapseToLeaf(leaves[0]) : BuildUnion(name, leaves);

    static ResolvedDu BuildUnion(string name, IReadOnlyList<Type> leaves) =>
        new(name, _ => new UnionType(d => leaves.Aggregate(d.Name(name), AddUnionMember)));

    static IUnionTypeDescriptor AddUnionMember(IUnionTypeDescriptor d, Type leaf) =>
        (IUnionTypeDescriptor)UnionDescriptorTypeGenericMethod
            .MakeGenericMethod(typeof(ObjectType<>).MakeGenericType(leaf))
            .Invoke(d, parameters: null)!;

    static ResolvedDu BuildCollapseToLeaf(Type leaf) =>
        new(leaf.Name,
            _ => (TypeSystemObject)Activator.CreateInstance(typeof(ObjectType<>).MakeGenericType(leaf))!);

    static TypeReference RewriteToNamed(
        ExtendedTypeReference original,
        string schemaName,
        Func<IDescriptorContext, TypeSystemObject> factory) =>
        TypeReference.Create(
            BuildTypeNode(original.Type, schemaName),
            original.Context,
            scope: original.Scope,
            factory: factory);

    static ITypeNode Wrap(INullableTypeNode node, bool isNullable) =>
        isNullable ? node : new NonNullTypeNode(node);

    static ITypeNode BuildTypeNode(IExtendedType ext, string schemaName) =>
        ext.ElementType is { } inner
            ? Wrap(new ListTypeNode(BuildTypeNode(inner, schemaName)), ext.IsNullable)
            : Wrap(new NamedTypeNode(schemaName), ext.IsNullable);

    sealed record ResolvedDu(string SchemaName, Func<IDescriptorContext, TypeSystemObject> Factory);
}
