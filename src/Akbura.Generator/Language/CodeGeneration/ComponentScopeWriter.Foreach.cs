using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.CodeGeneration;

internal readonly ref partial struct ComponentScopeWriter
{
    private void WriteForeachDestinationValidation(in ComponentPlan plan, in ComponentConditionalContentPlan content)
    {
        var first = FindFirstForeach(plan, content.Items);
        
        if (first < 0)
        {
            return;
        }

        _writer.Write("global::Akbura.HotReload.AkburaForeachDestination.Validate<");
        new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(
            plan.ForeachRegions.ItemRef(first).Operation.OutputType.Symbol as ITypeSymbol);
        _writer.Write(">((object)");
        new CollectionWriter(_writer).WriteTarget(content.Collection, plan.Elements.ItemRef(content.OwnerElementId).Identifier);
        _writer.WriteLine(");");
    }

    private static int FindFirstForeach(in ComponentPlan plan, ComponentPlanRange items)
    {
        for (var i = 0; i < items.Length; i++)
        {
            var value = plan.ContentItems.ItemRef(items.Start + i).Value;
            if (value.Kind == ComponentContentValueKind.Foreach) return value.Index;
            if (value.Kind != ComponentContentValueKind.Conditional) continue;
            foreach (var branch in plan.ConditionalRegions.ItemRef(value.Index).Branches)
            {
                var nested = FindFirstForeach(plan, branch.Items);
                if (nested >= 0) return nested;
            }
        }
        return -1;
    }

    private static bool RegionContainsForeach(in ComponentPlan plan, in ComponentConditionalRegionPlan region)
    {
        foreach (var branch in region.Branches)
        {
            for (var i = 0; i < branch.Items.Length; i++)
            {
                var value = plan.ContentItems.ItemRef(branch.Items.Start + i).Value;
                if (value.Kind == ComponentContentValueKind.Foreach ||
                    value.Kind == ComponentContentValueKind.Conditional &&
                    RegionContainsForeach(plan, plan.ConditionalRegions.ItemRef(value.Index)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void WriteForeachRegionAccess(in ComponentPlan plan, in ComponentForeachPlan region)
    {
        ref readonly var owner = ref plan.Elements.ItemRef(region.OwnerElementId);
        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).Write(".GetForeachRegion<");
        WriteForeachTypes(plan, region);
        _writer.Write(">(").WriteIntegerLiteral(owner.RuntimeStorageId).Write(", ");
        WriteForeachRegionSlot(plan, region);
        _writer.Write(", this.InvalidState)");
    }

    private void WriteForeachRegionSlot(in ComponentPlan plan, in ComponentForeachPlan region)
    {
        _writer.Write("\"foreach:");
        _writer.WriteIntegerLiteral(GetForeachOrdinal(plan, region));
        _writer.Write("\"");
    }

    private static int GetForeachOrdinal(in ComponentPlan plan, in ComponentForeachPlan region)
    {
        var ordinal = 0;
        for (var i = 0; i < region.Id; i++)
        {
            if (plan.ForeachRegions.ItemRef(i).OwnerElementId == region.OwnerElementId)
            {
                ordinal++;
            }
        }

        return ordinal;
    }

    private void WriteForeachTypes(in ComponentPlan plan, in ComponentForeachPlan region)
    {
        var types = new CSharpValueWriter(_writer);
        types.WriteTypeNameWithNullableAnnotation(region.Operation.IterationType.Symbol as ITypeSymbol);
        _writer.Write(", ");
        // Output contract belongs to the exact destination, not the source.
        types.WriteTypeNameWithNullableAnnotation(region.Operation.OutputType.Symbol as ITypeSymbol);
    }

    private void WriteForeachRegion(in ComponentPlan plan, in ComponentForeachPlan region,
        in MarkupExtensionWriteContext context, string changed, string cursor)
    {
        var name = "__foreachRegion" + region.Id;
        _writer.Write("var ").Write(name).Write(" = ");
        WriteForeachRegionAccess(plan, region);
        _writer.WriteLine(";");
        WriteForeachRender(plan, region, context, name);
        // The outer collection only needs reconciliation when this region changes
        // membership or order. Pending frames still commit through the owner frame.
        _writer.Write(changed).Write(" |= ").Write(name).WriteLine(".ChildrenChanged;");
        _writer.Write(cursor).Write(".AdvanceDynamicRegion(").Write(name).WriteLine(".Children.Count);");
    }

    private void WriteForeachRender(in ComponentPlan plan, in ComponentForeachPlan region,
        in MarkupExtensionWriteContext context, string name)
    {
        var operation = region.Operation;
        var frame = "__foreachFrame" + region.Id;
        var sourceItemType = GetForeachSourceItemType(operation.Source.Type);
        var iterationType = operation.IterationType.Symbol as ITypeSymbol;
        var nonGenericSource = sourceItemType == null;
        var needsConversion = sourceItemType != null &&
            !Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(sourceItemType, iterationType);
        _writer.Write(name).Write(nonGenericSource ? ".RenderNonGeneric" : ".Render");
        if (needsConversion || !operation.Key.IsDefault)
        {
            _writer.Write("<");
            if (needsConversion)
            {
                new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(sourceItemType);
                if (!operation.Key.IsDefault) _writer.Write(", ");
            }
            if (!operation.Key.IsDefault)
                new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(operation.Key.Type);
            _writer.Write(">");
        }
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(CSharpProbeBuilder.GetMarkupLoopCodeGenerationSyntax(operation.Source)!.ToString()).WriteLine(",");
        if (needsConversion || nonGenericSource)
        {
            _writer.Write("static __sourceItem => (");
            new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(iterationType);
            _writer.WriteLine(")__sourceItem!,");
        }
        _writer.WriteStringLiteral(region.TemplateRevision).WriteLine(",");
        _writer.WriteLine(GetForeachDependencies(operation) + ",");
        _writer.Write(frame).WriteLine(" =>");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(operation.IterationType.Symbol as ITypeSymbol);
        _writer.Write(" ");
        new CSharpValueWriter(_writer).WriteIdentifier(operation.IterationVariableName);
        _writer.Write(" = ").Write(frame).WriteLine(".Item;");
        _writer.Write("var ").Write(operation.IndexVariableName).Write(" = ").Write(frame).WriteLine(".Index;");
        WriteForeachBody(plan, region, operation.Body, context, frame);
        if (!ForeachBodyTerminates(operation.Body))
        {
            _writer.WriteLine("return global::Akbura.HotReload.LoopFlow.Next;");
        }
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.Write("}");
        if (!operation.Key.IsDefault)
        {
            _writer.WriteLine(",");
            new CSharpValueWriter(_writer).WriteIdentifier(operation.IterationVariableName);
            _writer.Write(" => ");
            _writer.Write(CSharpProbeBuilder.GetMarkupLoopCodeGenerationSyntax(operation.Key)!.ToString()).WriteLine(",");
            _writer.WriteStringLiteral(region.KeyContractIdentity);
        }
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private static ITypeSymbol? GetForeachSourceItemType(ITypeSymbol? sourceType)
    {
        if (sourceType is IArrayTypeSymbol array) return array.ElementType;
        if (sourceType is not INamedTypeSymbol named) return null;
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            return named.TypeArguments[0];
        foreach (var contract in named.AllInterfaces)
        {
            if (contract.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                return contract.TypeArguments[0];
        }
        return null;
    }

    private static bool ForeachBodyTerminates(IEnumerable<MarkupForeachBodyItem> body)
    {
        var last = body.LastOrDefault();

        if (last is null)
        {
            // Empty bodies fall through and need LoopFlow.Next.
            return false;
        }

        return last.Syntax is MarkupCodeStatementSyntax statement &&
            statement.GetRawCSharpStatement() is
                CSharp.BreakStatementSyntax or CSharp.ContinueStatementSyntax ||
            last.Syntax is MarkupCodeIfStatementSyntax &&
            !last.ElseBody.IsDefaultOrEmpty &&
            ForeachBodyTerminates(last.Body) &&
            ForeachBodyTerminates(last.ElseBody);
    }

    private static string GetForeachDependencies(IMarkupForeachOperation operation)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        InspectForeachBody(operation.Body, operation.IterationVariableName,
            operation.Source.Syntax?.ToString(), operation.IterationType.Symbol as ITypeSymbol, flags);
        if (flags.Count == 0)
        {
            return "global::Akbura.HotReload.AkburaForeachDependencies.ItemLocal";
        }
        return string.Join(" | ", flags.OrderBy(static x => x, StringComparer.Ordinal)
            .Select(static x => "global::Akbura.HotReload.AkburaForeachDependencies." + x));
    }

    private static void InspectForeachBody(IEnumerable<MarkupForeachBodyItem> body, string itemName,
        string? sourceName, ITypeSymbol? itemType, HashSet<string> flags)
    {
        foreach (var item in body)
        {
            var syntax = item.Syntax;
            if (syntax is MarkupCodeStatementSyntax statement)
            {
                var raw = statement.GetRawCSharpStatement();
                if (raw is CSharp.BreakStatementSyntax) flags.Add("MayBreak");
                else if (raw is CSharp.ContinueStatementSyntax) flags.Add("MayContinue");
            }
            foreach (var node in syntax.DescendantNodesAndSelf())
            {
                SyntaxNode? raw = node switch
                {
                    CSharpExpressionSyntax expression => expression.GetRawCSharpExpression(),
                    MarkupCodeStatementSyntax codeStatement => codeStatement.GetRawCSharpStatement(),
                    _ => null
                };
                if (raw == null) continue;
                foreach (var expression in raw.DescendantNodesAndSelf())
                {
                    if (expression is CSharp.MemberAccessExpressionSyntax access &&
                        access.Expression is CSharp.IdentifierNameSyntax receiver)
                    {
                        if (receiver.Identifier.ValueText == sourceName)
                            flags.Add("ReadsSourceWideData");
                        else if (receiver.Identifier.ValueText != itemName && receiver.Identifier.ValueText != "index")
                            flags.Add("ReadsComponentEnvironment");
                        if (receiver.Identifier.ValueText == itemName && itemType?.AllInterfaces.Any(static type =>
                            type.ToDisplayString() == "System.ComponentModel.INotifyPropertyChanged") == true)
                            flags.Add("ReadsNotifyingItemProperties");
                    }
                    if (expression is CSharp.InvocationExpressionSyntax or CSharp.AwaitExpressionSyntax or
                        CSharp.AssignmentExpressionSyntax or CSharp.PostfixUnaryExpressionSyntax or
                        CSharp.ObjectCreationExpressionSyntax or CSharp.ImplicitObjectCreationExpressionSyntax)
                    {
                        flags.Add("HasOpaqueEffectsOrReads");
                    }
                    if (expression is CSharp.IdentifierNameSyntax name &&
                        !(name.Parent is CSharp.MemberAccessExpressionSyntax memberAccess && memberAccess.Name == name))
                    {
                        if (name.Identifier.ValueText == "index") flags.Add("UsesSourceIndex");
                        else if (name.Identifier.ValueText != itemName && name.Parent is not CSharp.MemberAccessExpressionSyntax)
                            flags.Add("ReadsComponentEnvironment");
                    }
                }
            }
            InspectForeachBody(item.Body.IsDefault ? [] : item.Body, itemName, sourceName, itemType, flags);
            InspectForeachBody(item.ElseBody.IsDefault ? [] : item.ElseBody, itemName, sourceName, itemType, flags);
        }
    }

    private void WriteForeachBody(in ComponentPlan plan, in ComponentForeachPlan region,
        IEnumerable<MarkupForeachBodyItem> body, in MarkupExtensionWriteContext context, string frame)
    {
        foreach (var item in body)
        {
            if (item.ForeachOperation is { } nested)
            {
                var child = FindForeachPlan(plan, nested.Syntax);
                var name = "__foreachRegion" + child.Id;
                _writer.Write("var ").Write(name).Write(" = ").Write(frame).Write(".GetForeachRegion<");
                WriteForeachTypes(plan, child);
                _writer.Write(">(").WriteIntegerLiteral(child.Id).WriteLine(", this.InvalidState);");
                WriteForeachRender(plan, child, context, name);
                _writer.Write("foreach (var __child in ").Write(name).WriteLine(".Children)");
                _writer.WriteLine("{");
                _writer.CurrentIndent += _writer.TabSize;
                _writer.Write(frame).WriteLine(".Emit(__child);");
                _writer.CurrentIndent -= _writer.TabSize;
                _writer.WriteLine("}");
            }
            else if (item.Syntax is MarkupCodeStatementSyntax statement)
            {
                var raw = statement.GetRawCSharpStatement();
                if (raw is CSharp.BreakStatementSyntax)
                    _writer.WriteLine("return global::Akbura.HotReload.LoopFlow.Break;");
                else if (raw is CSharp.ContinueStatementSyntax)
                    _writer.WriteLine("return global::Akbura.HotReload.LoopFlow.Continue;");
                else
                    _writer.WriteLine(CSharpProbeBuilder.RewriteMarkupLoopIdentifiers(statement, raw).ToString());
            }
            else if (item.Syntax is MarkupCodeIfStatementSyntax)
            {
                _writer.Write("if (").Write(CSharpProbeBuilder.GetMarkupLoopCodeGenerationSyntax(item.Code)!.ToString()).WriteLine(")");
                _writer.WriteLine("{");
                _writer.CurrentIndent += _writer.TabSize;
                WriteForeachBody(plan, region, item.Body, context, frame);
                _writer.CurrentIndent -= _writer.TabSize;
                _writer.WriteLine("}");
                if (!item.ElseBody.IsDefaultOrEmpty)
                {
                    _writer.WriteLine("else");
                    _writer.WriteLine("{");
                    _writer.CurrentIndent += _writer.TabSize;
                    WriteForeachBody(plan, region, item.ElseBody, context, frame);
                    _writer.CurrentIndent -= _writer.TabSize;
                    _writer.WriteLine("}");
                }
            }
            else
            {
                WriteForeachEmits(plan, region, item.Content, context, frame);
            }
        }
    }

    private static ComponentForeachPlan FindForeachPlan(in ComponentPlan plan, MarkupForeachStatementSyntax syntax)
    {
        foreach (var region in plan.ForeachRegions)
        {
            if (region.Operation.Syntax == syntax) return region;
        }
        throw new InvalidOperationException("Missing foreach generation plan.");
    }

    private void WriteForeachEmits(in ComponentPlan plan, in ComponentForeachPlan region,
        IEnumerable<MarkupChildContent> content, in MarkupExtensionWriteContext context, string frame)
    {
        foreach (var child in content)
        {
            if (child.ConditionalOperation is { } conditional)
            {
                for (var branchId = 0; branchId < conditional.Branches.Length; branchId++)
                {
                    var branch = conditional.Branches[branchId];
                    if (!branch.Condition.IsDefault)
                        _writer.Write("if (").Write(CSharpProbeBuilder.GetMarkupLoopCodeGenerationSyntax(branch.Condition)!.ToString()).WriteLine(")");
                    _writer.WriteLine("{");
                    _writer.CurrentIndent += _writer.TabSize;
                    WriteForeachEmits(plan, region, branch.Content, context, frame);
                    _writer.CurrentIndent -= _writer.TabSize;
                    _writer.WriteLine("}");
                    if (branchId < conditional.Branches.Length - 1) _writer.Write("else ");
                }
            }
            else if (child.Syntax is MarkupElementContentSyntax element)
            {
                foreach (var root in region.Roots)
                {
                    if (root.Syntax == element.Element)
                    {
                        WriteForeachRoot(plan, root, context, frame);
                        break;
                    }
                }
            }
            else if (child.Syntax is MarkupInlineExpressionSyntax inline)
            {
                _writer.Write(frame).Write(".Emit(").Write(CSharpProbeBuilder.RewriteMarkupLoopIdentifiers(inline,
                    AkburaSemanticModel.ParseInlineExpression(inline.Expression)!).ToString()).WriteLine(");");
            }
        }
    }

    private void WriteForeachRoot(in ComponentPlan plan, in ComponentForeachRootPlan root,
        in MarkupExtensionWriteContext inherited, string frame)
    {
        using var writer = new CodeWriter();
        writer.CurrentIndent = _writer.CurrentIndent;
        writer.WriteLine("{");
        writer.CurrentIndent += writer.TabSize;
        var nameScope = "__foreachNameScope" + root.ElementId;
        var renderState = "__foreachState" + root.ElementId;
        writer.Write("var ").Write(nameScope).Write(" = ").Write(frame).Write(".GetNameScope(");
        writer.Write(inherited.NameScopeExpression ?? "null").WriteLine(");");
        writer.Write("var ").Write(renderState).Write(" = ").Write(frame).Write(".GetRenderState");
        if (root.Key is { IsIterationKey: false } localKey)
        {
            writer.Write("<");
            new CSharpValueWriter(writer).WriteTypeNameWithNullableAnnotation(localKey.KeyType.Symbol as ITypeSymbol);
            writer.Write(">(").WriteIntegerLiteral(root.ElementId).Write(", ");
            writer.Write(CSharpProbeBuilder.GetMarkupLoopCodeGenerationSyntax(localKey.ValueOperation)!.ToString());
            writer.WriteLine(");");
        }
        else
        {
            writer.Write("(").WriteIntegerLiteral(root.ElementId).WriteLine(");");
        }
        writer.Write("__akburaRenderState.BeginRevision(");
        writer.WriteStringLiteral(ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(root.Syntax)).WriteLine(", __builder =>");
        writer.WriteLine("{");
        writer.CurrentIndent += writer.TabSize;
        new ComponentStructuralHotReloadWriter(writer).WriteDescriptionContents(plan, root.ScopeId);
        writer.CurrentIndent -= writer.TabSize;
        writer.WriteLine("}, __localId => __localId switch");
        writer.WriteLine("{");
        writer.CurrentIndent += writer.TabSize;
        foreach (var element in plan.Elements)
        {
            if (!element.UsesRuntimeStorage || element.RuntimeStorageRootScopeId != root.ScopeId) continue;
            writer.WriteIntegerLiteral(element.RuntimeStorageId).Write(" => new ");
            new CSharpValueWriter(writer).WriteTypeName(element.Type);
            writer.WriteLine("(),");
        }
        writer.WriteLine("_ => throw new global::System.ArgumentOutOfRangeException(nameof(__localId)),");
        writer.CurrentIndent -= writer.TabSize;
        writer.WriteLine("});");
        writer.WriteLine("__akburaRenderState.BeginForeachFrame();");
        var context = new ComponentScopeWriteContext(plan.Elements.ItemRef(root.ElementId).Identifier,
            inherited.BaseUriExpression, inherited.FallbackServiceProviderExpression, null, root.ScopeId,
            MarkupParentStackTraversalKind.RuntimeStorageRoot, plan.Elements.AsSpan(), plan.ElementReferences.AsSpan());
        var scopes = new ComponentScopeWriter(writer, _bindingEnvironment, _sourceMap, _ownerTypeName, _generationMode);
        scopes.WriteForeachNameScopeContents(plan, plan.Scopes.ItemRef(root.ScopeId), nameScope);
        context = context.WithNameScope(nameScope);
        scopes.WriteLocalConditionalRender(plan, plan.Scopes.ItemRef(root.ScopeId), context);
        writer.Write(frame).Write(".Emit(").Write(plan.Elements.ItemRef(root.ElementId).Identifier).WriteLine(");");
        writer.CurrentIndent -= writer.TabSize;
        writer.WriteLine("}");
        // Rename only reserved compiler identifiers, not user strings or
        // comments. Separate item-root stores must not shadow enclosing stores.
        var parsed = CSharpSyntaxTree.ParseText(writer.GetText()).GetRoot();
        var rewritten = new ForeachStoreNameRewriter(renderState).Visit(parsed)!;
        _writer.Write(rewritten.ToFullString());
    }

    private void WriteForeachNameScopeContents(in ComponentPlan plan, in ComponentScopePlan scope, string name)
    {
        foreach (var elementId in plan.ScopeElementIds.AsSpan().Slice(scope.Elements.Start, scope.Elements.Length))
        {
            ref readonly var element = ref plan.Elements.ItemRef(elementId);
            if (!element.UsesRuntimeStorage) continue;
            foreach (var action in element.Assignments)
            {
                if (action.Kind != ComponentAssignmentKind.FirstUpdateAction) continue;
                ref readonly var first = ref plan.FirstUpdateActions.ItemRef(action.Index);
                if (first.Kind != ComponentFirstUpdateActionKind.NameAssignment) continue;
                var assignment = plan.NameAssignments.ItemRef(first.Index);
                _writer.Write(name).Write(".Register(").WriteStringLiteral(assignment.Name)
                    .Write(", ").Write(element.Identifier).WriteLine(");");
            }
            if (element.IsControl && element.ParentId == scope.OwnerElementId)
            {
                _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName)
                    .Write(".ReconcileConditionalAvaloniaValue(").WriteIntegerLiteral(element.RuntimeStorageId)
                    .Write(", \"$foreach-name-scope\", ").Write(element.Identifier)
                    .Write(", global::Avalonia.Controls.NameScope.NameScopeProperty, \"iteration\", ")
                    .Write(name).WriteLine(");");
            }
        }
    }

    private sealed class ForeachStoreNameRewriter(string name) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(CSharp.IdentifierNameSyntax node)
        {
            if (node.Identifier.ValueText == "__akburaRenderState" && IsOuterRenderNodeReference(node))
            {
                return node;
            }
            if (node.Identifier.ValueText is not ("__akburaRenderState" or "__akburaForeachRenderState"))
            {
                return base.VisitIdentifierName(node);
            }
            return node.WithIdentifier(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Identifier(
                node.Identifier.LeadingTrivia, name, node.Identifier.TrailingTrivia));
        }

        private static bool IsOuterRenderNodeReference(CSharp.IdentifierNameSyntax node) =>
            node.Parent is CSharp.MemberAccessExpressionSyntax
            {
                Expression: CSharp.IdentifierNameSyntax,
                Name: CSharp.GenericNameSyntax { Identifier.ValueText: "GetRequired" }
            } member && ReferenceEquals(member.Expression, node);
    }
}
