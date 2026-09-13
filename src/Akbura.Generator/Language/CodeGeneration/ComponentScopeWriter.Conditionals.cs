using System.Collections.Generic;

namespace Akbura.Language.CodeGeneration;

internal readonly ref partial struct ComponentScopeWriter
{
    private void WriteConditionalContent(in ComponentPlan plan, in ComponentConditionalContentPlan content,
        in MarkupExtensionWriteContext context)
    {
        var id = content.Id;
        var changed = "__conditionalChanged" + id;
        var cursor = "__conditionalCursorC" + id;
        var runtimeBacked = plan.Elements.ItemRef(content.OwnerElementId).UsesRuntimeStorage;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("var ").Write(changed).Write(" = ");
        if (runtimeBacked)
        {
            _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).WriteLine(".IsApplyingSourceRevision;");
        }
        else
        {
            _writer.WriteLine("true;");
        }

        _writer.Write("var ").Write(cursor).WriteLine(" = new global::Akbura.HotReload.AkburaRenderContentCursor();");
        var values = new List<int>();
        GatherConditionalValues(plan, content.Items, values);
        foreach (var itemId in values)
        {
            ref readonly var item = ref plan.ContentItems.ItemRef(itemId);
            if (item.Value.Kind == ComponentContentValueKind.CSharpExpression)
            {
                WriteConditionalValueDeclaration(plan, item.Value, "__conditionalValue" + itemId);
            }

            if (item.Key.Kind == ComponentContentValueKind.CSharpExpression)
            {
                WriteConditionalValueDeclaration(plan, item.Key, "__conditionalKey" + itemId);
            }
        }

        WriteConditionalStyleDeclarations(plan, content.Items);

        WriteConditionalActivations(plan, content.Items, context, changed, cursor);
        var isDynamic = false;
        foreach (var itemId in values)
        {
            ref readonly var item = ref plan.ContentItems.ItemRef(itemId);
            isDynamic |= item.Value.Kind == ComponentContentValueKind.CSharpExpression ||
                item.Key.Kind == ComponentContentValueKind.CSharpExpression;
        }
        _writer.Write("if (").Write(changed);
        if (isDynamic || content.ReplacesStyles)
        {
            _writer.Write(" || true");
        }
        else if (runtimeBacked && content.Collection.IsValid && !content.DictionaryShape.IsDictionary)
        {
            ref readonly var owner = ref plan.Elements.ItemRef(content.OwnerElementId);
            _writer.Write(" || !").Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).Write(".IsCollectionLayoutCurrent(");
            _writer.WriteIntegerLiteral(owner.RuntimeStorageId).Write(", ");
            _writer.WriteStringLiteral(ComponentStructuralHotReloadWriter.GetConditionalContentSlot(content)).Write(", ");
            new CollectionWriter(_writer).WriteTarget(content.Collection, owner.Identifier);
            _writer.Write(")");
        }

        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        if (content.Collection.IsValid)
        {
            WriteConditionalCollection(plan, content);
        }
        else
        {
            WriteConditionalScalar(plan, content);
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteConditionalValueDeclaration(in ComponentPlan plan, ComponentContentValueReference value, string name)
    {
        new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(plan.CSharpValues.ItemRef(value.Index).TargetType);
        _writer.Write(" ").Write(name).WriteLine(" = default!;");
    }

    private static void GatherConditionalValues(in ComponentPlan plan, ComponentPlanRange items, List<int> values)
    {
        for (var i = 0; i < items.Length; i++)
        {
            var index = items.Start + i;
            var value = plan.ContentItems.ItemRef(index).Value;
            if (value.Kind == ComponentContentValueKind.Conditional)
            {
                foreach (var branch in plan.ConditionalRegions.ItemRef(value.Index).Branches)
                {
                    GatherConditionalValues(plan, branch.Items, values);
                }
            }
            else
            {
                values.Add(index);
            }
        }
    }

    private void WriteConditionalActivations(in ComponentPlan plan, ComponentPlanRange items,
        in MarkupExtensionWriteContext context, string changed, string cursor)
    {
        var values = new ComponentValueWriter(_writer);
        for (var i = 0; i < items.Length; i++)
        {
            var index = items.Start + i;
            ref readonly var item = ref plan.ContentItems.ItemRef(index);
            if (item.Value.Kind == ComponentContentValueKind.Conditional)
            {
                WriteConditionalRegion(plan, plan.ConditionalRegions.ItemRef(item.Value.Index), context, changed, cursor);
            }
            else
            {
                _writer.Write(cursor).WriteLine(".AdvanceItem();");
                if (item.Value.Kind == ComponentContentValueKind.CSharpExpression)
                {
                    using var mapping = new SourceMappingWriter(_writer, _sourceMap).WriteStart(item.Syntax);
                    _writer.Write("__conditionalValue").WriteIntegerLiteral(index).Write(" = ");
                    values.WriteExpression(plan.CSharpValues.ItemRef(item.Value.Index));
                    _writer.WriteLine(";");
                }

                if (item.Key.Kind == ComponentContentValueKind.CSharpExpression)
                {
                    using var mapping = new SourceMappingWriter(_writer, _sourceMap).WriteStart(item.Syntax);
                    _writer.Write("__conditionalKey").WriteIntegerLiteral(index).Write(" = ");
                    values.WriteExpression(plan.CSharpValues.ItemRef(item.Key.Index));
                    _writer.WriteLine(";");
                }
            }
        }
    }

    private void WriteConditionalRegion(in ComponentPlan plan, in ComponentConditionalRegionPlan region,
        in MarkupExtensionWriteContext context, string changed, string parentCursor)
    {
        var hasElse = false;
        var runtimeId = ComponentRuntimeScopeFacts.GetConditionalRuntimeId(plan, region.Id);
        var activeCount = "__conditionalActive" + region.Id;
        var cursor = "__conditionalCursorR" + region.Id;
        _writer.Write("var ").Write(activeCount).WriteLine(" = 0;");
        if (runtimeId < 0)
        {
            _writer.Write("var __conditionalBranch").WriteIntegerLiteral(region.Id).WriteLine(" = -1;");
        }

        for (var i = 0; i < region.Branches.Length; i++)
        {
            var branch = region.Branches[i];
            if (branch.Condition != null)
            {
                using var mapping = new SourceMappingWriter(_writer, _sourceMap).WriteStart(branch.Condition, valueOffset: i == 0 ? 4 : 9);
                _writer.Write(i == 0 ? "if (" : "else if (");
                _writer.Write(branch.Condition.ToString()).WriteLine(")");
            }
            else
            {
                hasElse = true;
                _writer.WriteLine("else");
            }

            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
            if (runtimeId >= 0)
            {
                _writer.Write(changed).Write(" |= ").Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
                _writer.Write(".SelectConditionalBranch(").WriteIntegerLiteral(runtimeId).Write(", ").WriteIntegerLiteral(i).WriteLine(");");
            }
            else
            {
                _writer.Write("__conditionalBranch").WriteIntegerLiteral(region.Id).Write(" = ").WriteIntegerLiteral(i).WriteLine(";");
            }

            var captures = new ComponentConditionalCaptureWriter(_writer);
            captures.WriteClearsInactiveBranches(plan, region, i);
            captures.WritePublishes(branch);

            _writer.Write("var ").Write(cursor).WriteLine(" = new global::Akbura.HotReload.AkburaRenderContentCursor();");
            var branchContext = context;
            if (runtimeId >= 0)
            {
                var nameScope = WriteConditionalNameScope(plan, region, i, context);
                branchContext = context.WithNameScope(nameScope);
            }
            WriteConditionalBranchState(plan, plan.Scopes.ItemRef(branch.ScopeId), branchContext);
            WriteConditionalActivations(plan, branch.Items, branchContext, changed, cursor);
            _writer.Write(activeCount).Write(" = ").Write(cursor).WriteLine(".PhysicalCount;");
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
        }

        if (!hasElse && runtimeId >= 0)
        {
            _writer.WriteLine("else");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
            _writer.Write(changed).Write(" |= ").Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
            _writer.Write(".SelectConditionalBranch(").WriteIntegerLiteral(runtimeId).WriteLine(", -1);");
            new ComponentConditionalCaptureWriter(_writer).WriteClearsInactiveBranches(plan, region, -1);
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
        }

        _writer.Write(parentCursor).Write(".AdvanceRegion(").WriteIntegerLiteral(region.ReservedCapacity);
        _writer.Write(", ").Write(activeCount).WriteLine(");");
    }

    private void WriteConditionalBranchState(in ComponentPlan plan, in ComponentScopePlan scope,
        in MarkupExtensionWriteContext inherited, bool styleAlreadyDeclared = true)
    {
        var traversalKind = MarkupParentStackTraversalKind.FullHierarchy;
        if (scope.Kind is ComponentElementScopeKind.DataTemplate or ComponentElementScopeKind.DeferredContent ||
            scope.OwnerElementId >= 0 && plan.Elements.ItemRef(scope.OwnerElementId).RuntimeStorageRootScopeId > 0)
        {
            traversalKind = MarkupParentStackTraversalKind.RuntimeStorageRoot;
        }

        var intermediateRoot = inherited.IntermediateRootExpression;
        if (scope.OwnerElementId >= 0 && plan.Elements.ItemRef(scope.OwnerElementId).IsConditionalTemplateRoot)
        {
            for (var i = 0; i < scope.Elements.Length; i++)
            {
                ref readonly var element = ref plan.Elements.ItemRef(GetScopeElementId(plan, scope, i));
                if (element.ParentId == scope.OwnerElementId && element.IsControl)
                {
                    intermediateRoot = element.Identifier;
                    break;
                }
            }
        }

        var context = new ComponentScopeWriteContext(intermediateRoot,
            inherited.BaseUriExpression, inherited.FallbackServiceProviderExpression,
            inherited.NameScopeExpression, scope.Id, traversalKind,
            plan.Elements.AsSpan(), plan.ElementReferences.AsSpan(), inherited.IsConditionalNameScope);
        WriteStyleElementCreation(plan, scope, styleAlreadyDeclared);
        WriteConditionalCallbackRefresh(plan, scope);
        for (var i = scope.Elements.Length - 1; i >= 0; i--)
        {
            var elementId = GetScopeElementId(plan, scope, i);
            ref readonly var element = ref plan.Elements.ItemRef(elementId);
            if (!element.UsesRuntimeStorage)
            {
                continue;
            }

            _writer.Write("if (").Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).Write(".HasPendingRevision && (");
            _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).Write(".IsApplyingSourceRevision || ");
            _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).Write(".IsNew(").WriteIntegerLiteral(element.RuntimeStorageId).WriteLine(")))");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
            WriteStructuralOrderedAssignments(plan, element, context.ForElement(elementId),
                conditionalOneTime: true, skipFrameContents: true);
            _writer.Write("if (").Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
            _writer.Write(".IsNew(").WriteIntegerLiteral(element.RuntimeStorageId).WriteLine("))");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
            WriteSetStyles(plan, elementId, element.Identifier, context.ForElement(elementId));
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
            _writer.WriteLine("else");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
            WriteHotReloadStyles(plan, elementId, element.Identifier, context.ForElement(elementId));
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
        }

        var initialized = new bool[plan.Elements.Length];
        for (var i = 0; i < scope.Elements.Length; i++)
        {
            var elementId = GetScopeElementId(plan, scope, i);
            WriteElementAssignments(plan, elementId, context, initialized, ComponentAssignmentPhase.Update);
        }
    }

    private void WriteConditionalCallbackRefresh(in ComponentPlan plan, in ComponentScopePlan scope)
    {
        for (var i = 0; i < scope.Elements.Length; i++)
        {
            var elementId = GetScopeElementId(plan, scope, i);
            ref readonly var element = ref plan.Elements.ItemRef(elementId);
            if (!element.UsesRuntimeStorage || element.ConditionalRegionId < 0 && element.RuntimeStorageRootScopeId <= 0)
            {
                continue;
            }

            foreach (var assignment in element.Assignments)
            {
                if (assignment.Kind != ComponentAssignmentKind.FirstUpdateAction)
                {
                    continue;
                }

                ref readonly var action = ref plan.FirstUpdateActions.ItemRef(assignment.Index);
                if (action.Kind == ComponentFirstUpdateActionKind.CommandBinding)
                {
                    new ComponentFirstUpdateActionWriter(_writer, _sourceMap).WriteStructuralCommandBinding(
                        plan.CommandBindings.ItemRef(action.Index), element.RuntimeStorageId, element.Identifier, refreshClosure: true);
                }
                else if (action.Kind == ComponentFirstUpdateActionKind.RoutedEvent && element.ConditionalRegionId >= 0)
                {
                    new ComponentFirstUpdateActionWriter(_writer, _sourceMap).WriteStructuralRoutedEvent(
                        plan.RoutedEvents.ItemRef(action.Index), element.RuntimeStorageId, element.Identifier, refreshClosure: true);
                }
                else if (action.Kind == ComponentFirstUpdateActionKind.PropertySubscription && element.ConditionalRegionId >= 0)
                {
                    new PropertySubscriptionWriter(_writer, _sourceMap, writeInlineHandlers: true).WriteStructuralRegistration(
                        element, plan.PropertySubscriptions.ItemRef(action.Index), refreshClosure: true);
                }
            }
        }
    }

    private void WriteConditionalCollection(in ComponentPlan plan, in ComponentConditionalContentPlan content)
    {
        ref readonly var owner = ref plan.Elements.ItemRef(content.OwnerElementId);
        if (!owner.UsesRuntimeStorage)
        {
            WriteConditionalInlineCollection(plan, content);
            return;
        }

        var typed = !content.Collection.SupportsUntypedReconciliation && !content.DictionaryShape.IsDictionary;
        var name = "__conditionalItems" + content.Id;
        _writer.Write("var ").Write(name).Write(" = new global::System.Collections.Generic.List<");
        WriteConditionalCollectionItemType(content, typed);
        _writer.Write(">(__conditionalCursorC").WriteIntegerLiteral(content.Id).WriteLine(".PhysicalCount);");
        WriteConditionalDesiredItems(plan, content.Items, name, content, typed);
        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
        if (content.DictionaryShape.IsDictionary)
        {
            _writer.Write(".ReconcileDictionary(");
        }
        else
        {
            _writer.Write(content.Collection.Kind == CollectionWriteKind.ComponentParameter
                ? ".ReconcileComponentCollection" : ".ReconcileCollection");
            if (typed)
            {
                _writer.Write("<");
                WriteConditionalCollectionItemType(content, typed);
                _writer.Write(">");
            }

            _writer.Write("(");
        }

        _writer.WriteIntegerLiteral(owner.RuntimeStorageId).Write(", ");
        _writer.WriteStringLiteral(ComponentStructuralHotReloadWriter.GetConditionalContentSlot(content)).Write(", ");
        if (content.Collection.Kind == CollectionWriteKind.ComponentParameter && !content.DictionaryShape.IsDictionary)
        {
            _writer.Write(owner.Identifier).Write(", ");
        }

        if (content.DictionaryShape.IsDictionary)
        {
            _writer.Write("((");
            new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(content.DictionaryShape.ContractType);
            _writer.Write(")");
        }
        else if (!typed)
        {
            _writer.Write("((global::System.Collections.IList)");
        }

        new CollectionWriter(_writer).WriteTarget(content.Collection, owner.Identifier);
        if (content.DictionaryShape.IsDictionary || !typed)
        {
            _writer.Write(")");
        }

        _writer.Write(", ").Write(name);
        if (content.DictionaryShape.IsDictionary || !typed)
        {
            _writer.Write(".ToArray()");
        }

        _writer.WriteLine(");");
        if (content.ReplacesStyles)
        {
            _writer.Write("global::Akbura.HotReload.AkburaRenderStyleHelper.ApplyStyling(");
            _writer.Write(owner.Identifier).WriteLine(");");
        }
    }

    public void WriteLocalConditionalRender(in ComponentPlan plan, in ComponentScopePlan scope,
        in ComponentScopeWriteContext context)
    {
        var root = plan.ScopeRootElementIds[scope.Roots.Start];
        WriteConditionalBranchState(plan, scope, context.ForElement(root), styleAlreadyDeclared: false);
        _writer.Write("if (").Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).WriteLine(".HasPendingRevision)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).WriteLine(".PrepareRevisionCompletion();");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteConditionalBranchTest(in ComponentPlan plan, int regionId, int branchId)
    {
        var runtimeId = ComponentRuntimeScopeFacts.GetConditionalRuntimeId(plan, regionId);
        _writer.Write("if (");
        if (runtimeId < 0)
        {
            _writer.Write("__conditionalBranch").WriteIntegerLiteral(regionId);
        }
        else
        {
            _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).Write(".GetConditionalBranch(");
            _writer.WriteIntegerLiteral(runtimeId).Write(")");
        }

        _writer.Write(" == ").WriteIntegerLiteral(branchId).WriteLine(")");
    }

    private void WriteConditionalInlineCollection(in ComponentPlan plan, in ComponentConditionalContentPlan content)
    {
        WriteConditionalInlineItems(plan, content, content.Items);
    }

    private void WriteConditionalInlineItems(in ComponentPlan plan, in ComponentConditionalContentPlan content, ComponentPlanRange items)
    {
        for (var i = 0; i < items.Length; i++)
        {
            var index = items.Start + i;
            ref readonly var item = ref plan.ContentItems.ItemRef(index);
            if (item.Value.Kind == ComponentContentValueKind.Conditional)
            {
                var region = plan.ConditionalRegions.ItemRef(item.Value.Index);
                for (var branch = 0; branch < region.Branches.Length; branch++)
                {
                    WriteConditionalBranchTest(plan, region.Id, branch);
                    _writer.WriteLine("{");
                    _writer.CurrentIndent += _writer.TabSize;
                    WriteConditionalInlineItems(plan, content, region.Branches[branch].Items);
                    _writer.CurrentIndent -= _writer.TabSize;
                    _writer.WriteLine("}");
                }
            }
            else
            {
                var writer = new CollectionWriter(_writer);
                writer.WriteStart(content.Collection, plan.Elements.ItemRef(content.OwnerElementId).Identifier);
                if (item.InsertionMethod != null)
                {
                    _writer.Write("(");
                    new CSharpValueWriter(_writer).WriteTypeName(item.InsertionMethod.Parameters[0].Type);
                    _writer.Write(")(");
                }

                WriteConditionalLeafValue(plan, item.Value, index, isKey: false);
                if (item.InsertionMethod != null)
                {
                    _writer.Write(")");
                }

                writer.WriteEnd();
                _writer.WriteLine();
            }
        }
    }

    private void WriteConditionalStyleDeclarations(in ComponentPlan plan, ComponentPlanRange items)
    {
        for (var i = 0; i < items.Length; i++)
        {
            var value = plan.ContentItems.ItemRef(items.Start + i).Value;
            if (value.Kind != ComponentContentValueKind.Conditional)
            {
                continue;
            }

            foreach (var branch in plan.ConditionalRegions.ItemRef(value.Index).Branches)
            {
                var scope = plan.Scopes.ItemRef(branch.ScopeId);
                for (var j = 0; j < scope.Elements.Length; j++)
                {
                    ref readonly var element = ref plan.Elements.ItemRef(GetScopeElementId(plan, scope, j));
                    if (element.IsStyleSubtree)
                    {
                        new CSharpValueWriter(_writer).WriteTypeName(element.Type);
                        _writer.Write(" ").Write(element.Identifier).WriteLine(" = null!;");
                    }
                }

                WriteConditionalStyleDeclarations(plan, branch.Items);
            }
        }
    }

    private void WriteConditionalCollectionItemType(in ComponentConditionalContentPlan content, bool typed)
    {
        var types = new CSharpValueWriter(_writer);
        if (content.DictionaryShape.IsDictionary)
        {
            if (!content.DictionaryShape.IsGeneric)
            {
                _writer.Write("global::System.Collections.DictionaryEntry");
                return;
            }

            _writer.Write("global::System.Collections.Generic.KeyValuePair<");
            types.WriteTypeNameWithNullableAnnotation(content.DictionaryShape.KeyType);
            _writer.Write(", ");
            types.WriteTypeNameWithNullableAnnotation(content.DictionaryShape.ValueType);
            _writer.Write(">");
        }
        else if (typed)
        {
            types.WriteTypeNameWithNullableAnnotation(content.Collection.ElementType);
        }
        else
        {
            _writer.Write("object");
        }
    }

    private void WriteConditionalDesiredItems(in ComponentPlan plan, ComponentPlanRange items, string target,
        in ComponentConditionalContentPlan content, bool typed)
    {
        for (var i = 0; i < items.Length; i++)
        {
            var index = items.Start + i;
            ref readonly var item = ref plan.ContentItems.ItemRef(index);
            if (item.Value.Kind == ComponentContentValueKind.Conditional)
            {
                var region = plan.ConditionalRegions.ItemRef(item.Value.Index);
                for (var branchId = 0; branchId < region.Branches.Length; branchId++)
                {
                    WriteConditionalBranchTest(plan, region.Id, branchId);
                    _writer.WriteLine("{");
                    _writer.CurrentIndent += _writer.TabSize;
                    WriteConditionalDesiredItems(plan, region.Branches[branchId].Items, target, content, typed);
                    _writer.CurrentIndent -= _writer.TabSize;
                    _writer.WriteLine("}");
                }
            }
            else if (content.Collection.IsValid)
            {
                _writer.Write(target).Write(".Add(");
                if (content.DictionaryShape.IsDictionary)
                {
                    _writer.Write("new ");
                    WriteConditionalCollectionItemType(content, typed);
                    _writer.Write("(");
                    WriteConditionalLeafValue(plan, item.Key, index, isKey: true);
                    _writer.Write(", ");
                }

                WriteConditionalLeafValue(plan, item.Value, index, isKey: false);
                _writer.WriteLine(content.DictionaryShape.IsDictionary ? "));" : ");");
            }
            else
            {
                _writer.Write(target).Write(" = ");
                WriteConditionalLeafValue(plan, item.Value, index, isKey: false);
                _writer.WriteLine(";");
            }
        }
    }

    private void WriteConditionalScalar(in ComponentPlan plan, in ComponentConditionalContentPlan content)
    {
        var name = "__conditionalValue" + content.Id + "Result";
        _writer.Write("object? ").Write(name).WriteLine(" = null;");
        WriteConditionalDesiredItems(plan, content.Items, name, content, typed: false);
        ref readonly var owner = ref plan.Elements.ItemRef(content.OwnerElementId);
        if (!owner.UsesRuntimeStorage)
        {
            var property = new PropertyWriter(_writer);
            var end = property.WriteStart(content.Property, owner.Identifier);
            _writer.Write("(");
            new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(content.Property.ClrProperty?.Type);
            _writer.Write(")").Write(name);
            property.WriteEnd(end);
            _writer.WriteLine();
            return;
        }

        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.Write(content.Property.Kind == PropertyWriteKind.AvaloniaProperty ? ".ReconcileConditionalAvaloniaValue(" : ".ReconcileConditionalClrValue(");
        _writer.WriteIntegerLiteral(owner.RuntimeStorageId).Write(", ");
        _writer.WriteStringLiteral(ComponentStructuralHotReloadWriter.GetConditionalContentSlot(content)).Write(", ");
        _writer.Write(owner.Identifier).Write(", ");
        var types = new CSharpValueWriter(_writer);
        if (content.Property.Kind == PropertyWriteKind.AvaloniaProperty)
        {
            types.WriteStaticMemberReference(content.Property.AvaloniaProperty!);
        }
        else
        {
            _writer.Write("typeof(");
            types.WriteTypeName(content.Property.ReceiverType);
            _writer.Write("), ");
            _writer.WriteStringLiteral(content.Property.ClrProperty?.Name ?? content.Property.MemberName ?? string.Empty);
        }

        _writer.Write(", ");
        _writer.WriteStringLiteral(ComponentHotReloadIdentity.CreateContentSyntaxIdentity(content.Syntax));
        _writer.Write(", ").Write(name).WriteLine(");");
    }

    private void WriteConditionalLeafValue(in ComponentPlan plan, ComponentContentValueReference value, int itemId, bool isKey)
    {
        var writer = new ComponentValueWriter(_writer);
        if (value.Kind == ComponentContentValueKind.Element)
        {
            _writer.Write(plan.Elements.ItemRef(value.Index).Identifier);
        }
        else if (value.Kind == ComponentContentValueKind.CSharpExpression)
        {
            _writer.Write(isKey ? "__conditionalKey" : "__conditionalValue").WriteIntegerLiteral(itemId);
        }
        else
        {
            writer.WriteConstant(plan.CSharpValues.ItemRef(value.Index));
        }
    }
}
