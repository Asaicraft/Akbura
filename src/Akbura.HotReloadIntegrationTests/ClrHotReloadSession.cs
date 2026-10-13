using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Akbura.HotReloadIntegrationTests;

/// <summary>
/// Loads one assembly and applies real metadata, IL and PDB deltas to that assembly.
/// </summary>
internal sealed class ClrHotReloadSession : IDisposable
{
    private readonly ModuleMetadata _module;
    private readonly PEReader _peReader;
    private readonly MetadataReaderProvider _pdbProvider;
    private readonly string _componentName;
    private Compilation _compilation;
    private EmitBaseline _baseline;

    public ClrHotReloadSession(Compilation compilation, string componentName)
    {
        Assert.True(MetadataUpdater.IsSupported, "Real CLR Hot Reload must be enabled in the isolated child process.");
        _compilation = compilation;
        _componentName = componentName;
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = compilation.Emit(pe, pdb, options: new EmitOptions(
            debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var peImage = ImmutableArray.Create(pe.ToArray());
        var pdbImage = ImmutableArray.Create(pdb.ToArray());
        _module = ModuleMetadata.CreateFromImage(peImage);
        _peReader = new PEReader(peImage);
        _pdbProvider = MetadataReaderProvider.FromPortablePdbImage(pdbImage);
        _baseline = EmitBaseline.CreateInitialBaseline(compilation, _module,
            method => ReadDebugInformation(_pdbProvider.GetMetadataReader(), method), GetLocalSignature,
            hasPortableDebugInformation: true);
        Assembly = Assembly.Load(peImage.ToArray(), pdbImage.ToArray());
        ComponentType = Assert.IsAssignableFrom<Type>(Assembly.GetType(componentName));
    }

    public Assembly Assembly { get; }
    public Type ComponentType { get; }
    public int AppliedUpdates { get; private set; }

    public void ApplyUpdate(Compilation updated)
    {
        var edits = GetEdits(_compilation, updated);
        Assert.NotEmpty(edits);
        using var metadata = new MemoryStream();
        using var il = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = updated.EmitDifference(_baseline, edits, static _ => false, metadata, il, pdb);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotEqual(0, metadata.Length);
        Assert.NotEqual(0, il.Length);
        Assert.NotEqual(0, pdb.Length);
        // UpdatedMethods contains only methods with sequence points. Generated
        // hidden methods can change without appearing in that debugger list.
        using var deltaReader = MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(metadata.ToArray()));
        Assert.True(deltaReader.GetMetadataReader().MethodDefinitions.Count > 0,
            "The delta must contain actual method definitions, not only metadata headers.");
        MetadataUpdater.ApplyUpdate(Assembly, metadata.ToArray(), il.ToArray(), pdb.ToArray());
        _baseline = Assert.IsAssignableFrom<EmitBaseline>(result.Baseline);
        _compilation = updated;
        AppliedUpdates++;
    }

    public void DispatchMetadataUpdateHandlers()
    {
        // ApplyUpdate alone does not run the SDK's handler dispatcher. Invoke the
        // actual generated handlers in SDK order, on the live owner's UI thread.
        var handlers = Assembly.GetCustomAttributes<MetadataUpdateHandlerAttribute>().ToArray();
        Assert.NotEmpty(handlers);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var phase in new[] { "ClearCache", "UpdateApplication" })
        {
            foreach (var handler in handlers)
            {
                var method = Assert.IsAssignableFrom<MethodInfo>(handler.HandlerType.GetMethod(phase, flags));
                method.Invoke(null, [new[] { ComponentType }]);
            }
        }
    }

    private SemanticEdit[] GetEdits(Compilation previous, Compilation updated)
    {
        var oldType = Assert.IsAssignableFrom<INamedTypeSymbol>(previous.GetTypeByMetadataName(_componentName));
        var newType = Assert.IsAssignableFrom<INamedTypeSymbol>(updated.GetTypeByMetadataName(_componentName));
        var oldMembers = GetMembers(oldType);
        var newMembers = GetMembers(newType);
        Assert.True(oldMembers.Count != newMembers.Count || newMembers.Any(entry =>
                !oldMembers.TryGetValue(entry.Key, out var old) || !Syntax(old).IsEquivalentTo(Syntax(entry.Value))),
            "The generator must produce changed component members; unchanged output is not a Hot Reload.");
        var edits = new List<SemanticEdit>();
        foreach (var (key, member) in newMembers)
        {
            if (!oldMembers.TryGetValue(key, out var old))
            {
                edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, member));
            }
            // State type edits can change a method's bound getter call even when
            // its source text is identical. Recompile all retained component
            // methods/accessors rather than relying on a syntax-only diff.
            else if (member is IMethodSymbol method)
            {
                edits.Add(new SemanticEdit(SemanticEditKind.Update, old, method,
                    syntaxMap: CreateSyntaxMap(Syntax(old), Syntax(member))));
            }
            else if (member is IPropertySymbol property)
            {
                var oldProperty = (IPropertySymbol)old;
                AddAccessorUpdate(oldProperty.GetMethod, property.GetMethod);
                AddAccessorUpdate(oldProperty.SetMethod, property.SetMethod);
            }
        }

        foreach (var (key, member) in oldMembers)
        {
            // CLR metadata retains deleted fields. Generated code stops referring
            // to them; only methods/properties can receive delete edits.
            if (!newMembers.ContainsKey(key) && member is IMethodSymbol or IPropertySymbol)
            {
                edits.Add(new SemanticEdit(SemanticEditKind.Delete, member, newType));
            }
        }
        return edits.ToArray();

        void AddAccessorUpdate(IMethodSymbol? oldAccessor, IMethodSymbol? newAccessor)
        {
            if (oldAccessor != null && newAccessor != null)
            {
                edits.Add(new SemanticEdit(SemanticEditKind.Update, oldAccessor, newAccessor,
                    syntaxMap: CreateSyntaxMap(Syntax(oldAccessor), Syntax(newAccessor))));
            }
        }
    }

    private static Dictionary<string, ISymbol> GetMembers(INamedTypeSymbol type) => type.GetMembers()
        .Where(member => !member.IsImplicitlyDeclared && !member.DeclaringSyntaxReferences.IsDefaultOrEmpty &&
            (member is IFieldSymbol or IPropertySymbol || member is IMethodSymbol { AssociatedSymbol: null }))
        .ToDictionary(MemberKey, StringComparer.Ordinal);

    private static string MemberKey(ISymbol member)
    {
        var signature = member switch
        {
            IMethodSymbol method => method.Arity + "|" + method.ReturnType.ToDisplayString() + "|" +
                string.Join(",", method.Parameters.Select(parameter => parameter.RefKind + ":" + parameter.Type.ToDisplayString())),
            IPropertySymbol property => property.Type.ToDisplayString(),
            IFieldSymbol field => field.Type.ToDisplayString(),
            _ => throw new InvalidOperationException("Unsupported member: " + member),
        };
        return member.Kind + "|" + member.MetadataName + "|" + member.IsStatic + "|" + signature;
    }

    private static SyntaxNode Syntax(ISymbol symbol) => Assert.Single(symbol.DeclaringSyntaxReferences).GetSyntax();

    private static Func<SyntaxNode, SyntaxNode?> CreateSyntaxMap(SyntaxNode oldSyntax, SyntaxNode newSyntax)
    {
        var map = new Dictionary<SyntaxNode, SyntaxNode> { [newSyntax] = oldSyntax };
        Map<VariableDeclaratorSyntax>(node => node.Identifier.ValueText);
        Map<ParameterSyntax>(node => node.Identifier.ValueText);
        Map<LambdaExpressionSyntax>(node => node switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax parenthesized => string.Join(",",
                parenthesized.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText)),
            _ => throw new InvalidOperationException(),
        });
        foreach (var (current, previous) in map.ToArray())
        {
            if (current is LambdaExpressionSyntax currentLambda && previous is LambdaExpressionSyntax previousLambda)
            {
                map[currentLambda.Body] = previousLambda.Body;
            }
        }
        return node => map.GetValueOrDefault(node);

        void Map<T>(Func<T, string> key) where T : SyntaxNode
        {
            var oldNodes = oldSyntax.DescendantNodes().OfType<T>().GroupBy(key, StringComparer.Ordinal)
                .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());
            foreach (var group in newSyntax.DescendantNodes().OfType<T>().GroupBy(key, StringComparer.Ordinal))
            {
                if (group.Count() == 1 && oldNodes.TryGetValue(group.Key, out var old))
                {
                    map[group.Single()] = old;
                }
            }
        }
    }

    private StandaloneSignatureHandle GetLocalSignature(MethodDefinitionHandle handle)
    {
        var definition = _peReader.GetMetadataReader().GetMethodDefinition(handle);
        return definition.RelativeVirtualAddress == 0 ? default :
            _peReader.GetMethodBody(definition.RelativeVirtualAddress).LocalSignature;
    }

    private static EditAndContinueMethodDebugInformation ReadDebugInformation(
        MetadataReader reader, MethodDefinitionHandle method)
    {
        var slots = ImmutableArray<byte>.Empty;
        var lambdas = ImmutableArray<byte>.Empty;
        var states = ImmutableArray<byte>.Empty;
        foreach (var handle in reader.GetCustomDebugInformation(method))
        {
            var info = reader.GetCustomDebugInformation(handle);
            var kind = reader.GetGuid(info.Kind);
            var bytes = ImmutableArray.Create(reader.GetBlobBytes(info.Value));
            if (kind == new Guid("755F52A8-91C5-45BE-B4B8-209571E552BD"))
            {
                slots = bytes;
            }
            else if (kind == new Guid("A643004C-0240-496F-A783-30D64F4979DE"))
            {
                lambdas = bytes;
            }
            else if (kind == new Guid("8B78CD68-2EDE-420B-980B-E15884B8AAA3"))
            {
                states = bytes;
            }
        }
        return EditAndContinueMethodDebugInformation.Create(slots, lambdas, states);
    }

    public void Dispose()
    {
        _pdbProvider.Dispose();
        _peReader.Dispose();
        _module.Dispose();
    }
}
