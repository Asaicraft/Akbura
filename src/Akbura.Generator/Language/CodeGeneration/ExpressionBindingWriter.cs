using Akbura.Language.Binder;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using IStateSymbol = Akbura.Language.Symbols.IStateSymbol;
using IParamSymbol = Akbura.Language.Symbols.IParamSymbol;
using IInjectSymbol = Akbura.Language.Symbols.IInjectSymbol;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ExpressionBindingWriter(CodeWriter writer, ComponentGenerationSourceMap sourceMap)
{
    public void Write(in ComponentPlan component, in ComponentPropertyWritePlan property,
        in ComponentElementPlan element, in MarkupExtensionWriteContext context)
    {
        ref readonly var value = ref component.CSharpValues.ItemRef(property.PayloadIndex);
        var plan = value.ExpressionBinding!;
        using var mapping = new SourceMappingWriter(writer, sourceMap).WriteStart(property.Syntax);
        var structural = element.UsesRuntimeStorage;
        ref readonly var scope = ref component.Scopes.ItemRef(element.ScopeId);
        var lifetimeRoot = element.ScopeId == 0 ? "this" :
            component.Elements.ItemRef(component.ScopeRootElementIds[scope.Roots.Start]).Identifier;
        writer.Write(structural ? "__akburaRenderState.ReconcileExpressionBinding<" : "this.__AkburaBindExpression<");
        var types = new CSharpValueWriter(writer);
        types.WriteTypeNameWithNullableAnnotation(plan.TargetType);
        writer.Write(">(");
        if (structural) writer.WriteIntegerLiteral(element.RuntimeStorageId);
        else writer.Write(element.Identifier);
        writer.Write(", ").WriteStringLiteral(ComponentHotReloadIdentity.CreatePropertySlot(property.Destination));
        writer.Write(", ");
        if (structural)
            writer.WriteStringLiteral(ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(property.Syntax));
        else
            writer.Write(lifetimeRoot);
        var rewriter = new TrackingRewriter(plan, value.ExpressionStateAccessors);
        writer.Write(", ").Write(rewriter.TrackerName).Write(" => ");
        var expression = rewriter.Visit(plan.Expression.Syntax)!;
        writer.Write(expression.NormalizeWhitespace().ToFullString());
        var expressionValue = rewriter.NewIdentifier("__expressionValue");
        writer.Write(", ").Write(expressionValue).Write(" => { ");
        var assignment = new PropertyWriter(writer);
        var end = assignment.WriteStart(property.Destination, element.Identifier);
        writer.Write(expressionValue);
        assignment.WriteEnd(end);
        writer.Write(" }");
        if (structural)
        {
            if (property.Destination.HasAvaloniaPropertyTarget)
            {
                writer.Write(", (global::Avalonia.AvaloniaObject)").Write(element.Identifier).Write(", ");
                new MarkupTargetPropertyWriter(writer).Write(property.Destination.TargetProperty);
            }
            else if (property.Destination.Kind is PropertyWriteKind.ClrProperty or PropertyWriteKind.DirectMember or PropertyWriteKind.ComponentParameter)
            {
                var expressionBaseline = rewriter.NewIdentifier("__expressionBaseline");
                writer.Write(", null, null, () => { var ").Write(expressionBaseline).Write(" = ((");
                types.WriteTypeName(property.Destination.ReceiverType);
                writer.Write(")").Write(element.Identifier).Write(").");
                types.WriteIdentifier(property.Destination.ClrProperty?.Name ?? property.Destination.MemberName!);
                writer.Write("; return () => { ");
                var restoreEnd = assignment.WriteStart(property.Destination, element.Identifier);
                writer.Write(expressionBaseline);
                assignment.WriteEnd(restoreEnd);
                writer.Write(" }; }");
            }
            var lifetimeName = rewriter.NewIdentifier("__akburaExpressionLifetime");
            writer.Write(", ownLifetime: ").Write(lifetimeName)
                .Write(" => this.__AkburaRegisterLocalRenderScope(").Write(lifetimeRoot)
                .Write(", ").Write(lifetimeName).Write(".Resume, ")
                .Write(lifetimeName).Write(".Suspend, __akburaRenderState)");
        }
        writer.WriteLine(");");
    }

    private sealed class TrackingRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<TextSpan, ExpressionBindingEvaluation> _reads = new();
        private readonly Dictionary<TextSpan, ExpressionSyntax> _projectedLoopSyntax = new();
        private readonly Dictionary<TextSpan, IConditionalAccessOperation> _conditional = new();
        private readonly Stack<IdentifierNameSyntax> _receivers = new();
        private readonly HashSet<string> _identifiers;
        private readonly IReadOnlyDictionary<string, string> _stateNames;
        private readonly Dictionary<ISymbol, int> _memberIdentities = new(SymbolEqualityComparer.Default);
        private int _temporary;
        public string TrackerName { get; }

        public TrackingRewriter(ExpressionBindingPlan plan, IReadOnlyDictionary<string, string> stateNames)
        {
            _stateNames = stateNames;
            _identifiers = new HashSet<string>(plan.Expression.Syntax!.DescendantTokens()
                .Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText));
            TrackerName = FreshName("__akburaExpressionTracker");
            Add(plan.Evaluation!);
        }

        private string FreshName(string prefix)
        {
            var name = prefix;
            while (!_identifiers.Add(name)) name = prefix + _temporary++;
            return name;
        }

        public string NewIdentifier(string prefix) => FreshName(prefix);

        private void Add(ExpressionBindingEvaluation evaluation)
        {
            // A projected foreach index can look like an observable property in
            // the probe. Its declaration annotation, not that probe receiver,
            // determines the emitted local. The shared helper also preserves
            // nameof(index) as the original name rather than the generated one.
            if (evaluation.Operation is INameOfOperation or
                IPropertyReferenceOperation { Syntax: IdentifierNameSyntax })
            {
                var syntax = CSharpProbeBuilder.GetMarkupLoopCodeGenerationSyntax(new CSharpOperationDefinition(evaluation.Operation));
                if (syntax is ExpressionSyntax projected && !ReferenceEquals(syntax, evaluation.Operation.Syntax))
                    _projectedLoopSyntax[evaluation.Operation.Syntax.Span] = projected;
            }
            if (evaluation.Operation is IConditionalAccessOperation conditional)
                _conditional[conditional.Syntax.Span] = conditional;
            if (evaluation.Operation is IPropertyReferenceOperation or ILocalReferenceOperation or
                IParameterReferenceOperation or IFieldReferenceOperation)
                _reads[evaluation.Operation.Syntax.Span] = evaluation;
            if (evaluation.Dependency is { Member: { } member } dependency && !_memberIdentities.ContainsKey(member))
                _memberIdentities.Add(member, dependency.Id);
            foreach (var child in evaluation.Children) Add(child);
        }

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null) return null;
            if (node is ExpressionSyntax && _projectedLoopSyntax.TryGetValue(node.Span, out var projected))
                return projected.WithTriviaFrom(node);
            var updated = base.Visit(node);
            if (updated is not ExpressionSyntax expression || !_reads.TryGetValue(node.Span, out var evaluation))
                return updated;
            var dependency = evaluation.Dependency;
            if (dependency == null) return updated;
            if (dependency.SourceSymbol is IStateSymbol state)
                return Call("State", IdentifierName(_stateNames[state.Name]), dependency.Id).WithTriviaFrom(node);
            if (dependency.SourceSymbol is IParamSymbol or IInjectSymbol)
            {
                var source = dependency.SourceSymbol;
                var receiver = Call("Avalonia", ThisExpression(), dependency.Id,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(source.Name + "Property"), IdentifierName("AvaloniaProperty")));
                return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, EscapedIdentifier(source.Name)).WithTriviaFrom(node);
            }
            if (evaluation.Operation is IPropertyReferenceOperation property)
            {
                if (dependency.Observation == ExpressionBindingObservation.None)
                    return updated;
                ExpressionSyntax Trace(ExpressionSyntax receiver)
                {
                    var observationId = _memberIdentities[property.Property];
                    if ((dependency.Observation & ExpressionBindingObservation.CollectionChanged) != 0)
                        return Call("Collection", receiver, observationId);
                    if ((dependency.Observation & ExpressionBindingObservation.AvaloniaProperty) != 0 && dependency.NotificationMember is { } registration)
                        return Call("Avalonia", receiver, observationId,
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                ParseName(registration.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)), IdentifierName(registration.Name)));
                    return Call("Property", receiver, observationId,
                        LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(property.Property.IsIndexer ? "Item[]" : property.Property.Name)));
                }
                return expression switch
                {
                    MemberAccessExpressionSyntax member => member.WithExpression(Trace(member.Expression)).WithTriviaFrom(node),
                    ElementAccessExpressionSyntax indexer => indexer.WithExpression(Trace(indexer.Expression)).WithTriviaFrom(node),
                    IdentifierNameSyntax name => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, Trace(ThisExpression()), name.WithoutTrivia()).WithTriviaFrom(node),
                    _ => updated,
                };
            }
            return updated;
        }

        public override SyntaxNode? VisitMemberBindingExpression(MemberBindingExpressionSyntax node) =>
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, _receivers.Peek(), node.Name).WithTriviaFrom(node);

        public override SyntaxNode? VisitElementBindingExpression(ElementBindingExpressionSyntax node) =>
            ElementAccessExpression(_receivers.Peek(), (BracketedArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var receiver = (ExpressionSyntax)Visit(node.Expression)!;
            var temporary = IdentifierName(FreshName("__akburaExpressionReceiver"));
            _receivers.Push(temporary);
            ExpressionSyntax whenNotNull;
            try { whenNotNull = (ExpressionSyntax)Visit(node.WhenNotNull)!; }
            finally { _receivers.Pop(); }
            var type = ParseTypeName(_conditional[node.Span].Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            var test = IsPatternExpression(ParenthesizedExpression(receiver),
                RecursivePattern().WithPropertyPatternClause(PropertyPatternClause()).WithDesignation(SingleVariableDesignation(temporary.Identifier)));
            return ParenthesizedExpression(ConditionalExpression(test,
                CastExpression(type, ParenthesizedExpression(whenNotNull)), DefaultExpression(type))).WithTriviaFrom(node);
        }

        private InvocationExpressionSyntax Call(string method, ExpressionSyntax receiver, int id, params ExpressionSyntax[] arguments) =>
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName(TrackerName), IdentifierName(method)),
                ArgumentList(SeparatedList(new[] { receiver,
                    LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(id)) }.Concat(arguments).Select(Argument))));

        private static IdentifierNameSyntax EscapedIdentifier(string name) =>
            IdentifierName(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name);
    }
}
