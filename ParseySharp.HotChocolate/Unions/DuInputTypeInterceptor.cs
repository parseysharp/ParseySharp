using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;

namespace ParseySharp.HotChocolate;

/// <summary>
/// Walks every input position at type-discovery time (input-object fields and
/// argument types on object/interface fields), finds discriminated-union
/// positions structurally (<c>Either&lt;,&gt;</c> or <c>OneOf&lt;,..&gt;</c>) in
/// the field's CLR input-type tree, and rewrites the field's <see cref="TypeReference"/>
/// to point at a synthesized <c>@oneOf</c> <see cref="InputObjectType"/> while
/// preserving list/non-null structure. Naming is derived from the parent type
/// + the field/argument name (<c>&lt;Parent&gt;&lt;PascalField&gt;</c>);
/// alphabetical concat is the no-parent fallback. Nested DUs are caught one
/// level deeper at the next field walk, so input projections mirror the CLR
/// shape rather than flattening.
/// </summary>
internal sealed class DuInputTypeInterceptor : TypeInterceptor
{
    readonly ParseySharpHotChocolateOptions _options;
    readonly Dictionary<string, ResolvedDu> _byName = new();

    public DuInputTypeInterceptor(ParseySharpHotChocolateOptions options)
    {
        _options = options;
    }

    public override void OnBeforeRegisterDependencies(
        ITypeDiscoveryContext discoveryContext,
        TypeSystemConfiguration configuration) =>
        toSeq(InputPositionsOf(configuration)).Iter(p => ProcessInputPosition(p.Parent, p.Field));

    static IEnumerable<(string? Parent, ArgumentConfiguration Field)> InputPositionsOf(TypeSystemConfiguration c) =>
        c switch
        {
            InputObjectTypeConfiguration io => io.Fields.Select(f => (NameOrRuntime(io.Name, io.RuntimeType), (ArgumentConfiguration)f)),
            ObjectTypeConfiguration o       => o.Fields.SelectMany(f => f.Arguments.Select(a => ((string?)f.Name, a))),
            InterfaceTypeConfiguration i    => i.Fields.SelectMany(f => f.Arguments.Select(a => ((string?)f.Name, a))),
            _ => Enumerable.Empty<(string?, ArgumentConfiguration)>(),
        };

    static string? NameOrRuntime(string? name, Type? runtimeType) =>
        !string.IsNullOrEmpty(name) ? name : runtimeType?.Name;

    void ProcessInputPosition(string? parent, ArgumentConfiguration field) =>
        TypeShapeAnalyzer.Analyze(field.Type).Iter(d => ApplyDu(parent, field, d));

    void ApplyDu(string? parent, ArgumentConfiguration field, DuShape shape)
    {
        var resolved = ResolveDu(shape, parent, field.Name);
        field.Type = RewriteToNamed((ExtendedTypeReference)field.Type!, resolved.SchemaName, _ => resolved.Instance);
    }

    ResolvedDu ResolveDu(DuShape shape, string? parent, string? fieldName)
    {
        var leaves = ValidateLeaves(
            shape,
            TypeDecomposer.Decompose<Shallow>(shape.DuRoot, _options.UninhabitedTypes));
        var name = DuTypeNaming.NameFor<InputNaming>(parent, fieldName, leaves);
        return _byName.TryGetValue(name, out var cached)
            ? cached
            : Build(shape, leaves, name);
    }

    ResolvedDu Build(DuShape shape, IReadOnlyList<Type> leaves, string name)
    {
        var arms = leaves.Select(arm => (Arm: arm, Nested: ResolveNestedArm(arm))).ToArray();
        var instance = new InputObjectType(d => ConfigureOneOf(d, name, arms));
        return _byName[name] = new ResolvedDu(name, instance);
    }

    ResolvedDu? ResolveNestedArm(Type arm) =>
        ScanArm(arm) is { } nested
            ? ResolveDu(nested, parent: null, fieldName: null)
            : null;

    static IReadOnlyList<Type> ValidateLeaves(DuShape shape, IReadOnlyList<Type> leaves) =>
        leaves switch
        {
            { Count: 0 } => throw new SchemaException(SchemaErrorBuilder.New()
                .SetMessage(
                    "Discriminated union {0} has no inhabited arms after pruning.",
                    shape.DuRoot.FullName ?? shape.DuRoot.Name)
                .Build()),
            _ when TypeDecomposer.Duplicates(leaves) is { Count: > 0 } dups
                => throw new SchemaException(SchemaErrorBuilder.New()
                    .SetMessage(
                        "Discriminated union {0} has duplicate arm type(s) [{1}] in its decomposition. "
                            + "Distinct @oneOf input fields require distinct CLR arm types.",
                        shape.DuRoot.FullName ?? shape.DuRoot.Name,
                        string.Join(", ", dups.Select(d => d.FullName ?? d.Name)))
                    .Build()),
            _ => leaves,
        };

    static void ConfigureOneOf(
        IInputObjectTypeDescriptor d,
        string name,
        IReadOnlyList<(Type Arm, ResolvedDu? Nested)> arms)
    {
        d.Name(name).OneOf();
        foreach (var (arm, nested) in arms)
        {
            if (nested is { } ns)
            {
                d.Field(ToCamel(ns.SchemaName)).Type(ns.Instance);
            }
            else
            {
                d.Field(ToCamel(arm.Name)).Type(arm);
            }
        }
    }

    static DuShape? ScanArm(Type t) =>
        TypeShapeClassifier.Classify(t).Match<DuShape?>(
            _ => new DuShape(DuRoot: t, ClrShape: t),
            _ => new DuShape(DuRoot: t, ClrShape: t),
            n => ScanArm(n.Underlying),
            _ => null,
            _ => null,
            _ => null);

    static string ToCamel(string s) =>
        string.IsNullOrEmpty(s) || char.IsLower(s[0])
            ? s
            : char.ToLowerInvariant(s[0]) + s[1..];

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

    sealed record ResolvedDu(string SchemaName, InputObjectType Instance);
}
