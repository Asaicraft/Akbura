namespace Akbura.Language.CodeGeneration;

internal readonly ref partial struct ComponentScopeWriter
{
    private ComponentScopeWriteContext WriteComponentNameScope(in ComponentPlan plan,
        in ComponentScopePlan scope, in ComponentScopeWriteContext context, string name)
    {
        if (scope.Id != 0 || !NeedsComponentNameScope(plan))
        {
            return context;
        }

        _writer.Write("var ").Write(name).Write(" = ")
            .Write(ComponentStructuralHotReloadWriter.RenderStateFieldName).WriteLine(".GetLocalNameScope(null);");
        WriteNativeNameScopeContents(plan, scope, name, parentNameScope: null);
        return context.WithNameScope(name);
    }

    public static bool NeedsComponentNameScope(in ComponentPlan plan)
    {
        if (plan.HasConditionalRegions)
        {
            foreach (var scope in plan.Scopes)
            {
                if (scope.Kind is ComponentElementScopeKind.DataTemplate or ComponentElementScopeKind.DeferredContent &&
                    ComponentLocalScopeWriter.CanWrite(plan, scope))
                {
                    return true;
                }
            }
        }

        var hasComponentNames = false;
        foreach (var reference in plan.ElementReferences)
        {
            if (reference.IsClassMember && reference.IsComponentRuntimeStorage)
            {
                hasComponentNames = true;
                break;
            }
        }
        if (!hasComponentNames)
        {
            return false;
        }

        foreach (var element in plan.Elements)
        {
            if (!element.UsesRuntimeStorage || element.RuntimeStorageRootScopeId <= 0)
            {
                continue;
            }
            for (var i = 0; i < element.PropertyWrites.Length; i++)
            {
                ref readonly var property = ref plan.PropertyWrites.ItemRef(element.PropertyWrites.Start + i);
                if (property.ValueKind != ComponentPropertyValueKind.MarkupBinding)
                {
                    continue;
                }
                ref readonly var binding = ref plan.Bindings.ItemRef(property.PayloadIndex);
                if (binding.SourceExpression != null)
                {
                    continue;
                }
                foreach (var extensionProperty in binding.Extension.Properties)
                {
                    if (extensionProperty.Name == "ElementName")
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private string WriteConditionalNameScope(in ComponentPlan plan, in ComponentConditionalRegionPlan region,
        int branchId, in MarkupExtensionWriteContext context)
    {
        var name = "__conditionalNameScope" + region.Id;
        var runtimeId = ComponentRuntimeScopeFacts.GetConditionalRuntimeId(plan, region.Id);
        ref readonly var scope = ref plan.Scopes.ItemRef(region.Branches[branchId].ScopeId);
        _writer.Write("var ").Write(name).Write(" = ")
            .Write(ComponentStructuralHotReloadWriter.RenderStateFieldName)
            .Write(".GetConditionalNameScope(").WriteIntegerLiteral(runtimeId).Write(", ")
            .WriteIntegerLiteral(branchId).Write(", ").Write(context.NameScopeExpression ?? "null").WriteLine(");");
        WriteNativeNameScopeContents(plan, scope, name, context.NameScopeExpression);
        return name;
    }

    public void WriteLocalNameScope(in ComponentPlan plan, in ComponentScopePlan scope)
    {
        _writer.WriteLine("var __localParentNameScope = (__services?.GetService(typeof(global::Akbura.Markup.AkburaRetainedFactoryServiceProvider)) as global::Akbura.Markup.AkburaRetainedFactoryServiceProvider)?.CurrentNameScope ??");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__parentRenderState.GetNameScopeForNode(__parentOwner) ??");
        _writer.WriteLine("__services?.GetService(typeof(global::Avalonia.Controls.INameScope)) as global::Avalonia.Controls.INameScope ?? __nameScope;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("var __localNameScope = __akburaRenderState.GetLocalNameScope(__localParentNameScope);");
        WriteNativeNameScopeContents(plan, scope, "__localNameScope", "__localParentNameScope");
    }

    private void WriteNativeNameScopeContents(in ComponentPlan plan, in ComponentScopePlan scope, string name,
        string? parentNameScope)
    {
        _writer.Write("if (!").Write(name).WriteLine(".IsCompleted)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        for (var i = 0; i < scope.Elements.Length; i++)
        {
            ref readonly var element = ref plan.Elements.ItemRef(GetScopeElementId(plan, scope, i));
            if (!element.UsesRuntimeStorage)
            {
                continue;
            }

            for (var actionIndex = 0; actionIndex < element.FirstUpdateActions.Length; actionIndex++)
            {
                ref readonly var action = ref plan.FirstUpdateActions.ItemRef(element.FirstUpdateActions.Start + actionIndex);
                if (action.Kind != ComponentFirstUpdateActionKind.NameAssignment)
                {
                    continue;
                }

                ref readonly var assignment = ref plan.NameAssignments.ItemRef(action.Index);
                _writer.Write(name).Write(".Register(").WriteStringLiteral(assignment.Name)
                    .Write(", ").Write(element.Identifier).WriteLine(");");
            }

            if (element.IsControl && IsNativeNameScopeRoot(plan, scope, element))
            {
                _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName)
                    .Write(".ReconcileConditionalAvaloniaValue(").WriteIntegerLiteral(element.RuntimeStorageId)
                    .Write(", \"$conditional-name-scope\", ").Write(element.Identifier)
                    .Write(", global::Avalonia.Controls.NameScope.NameScopeProperty, \"lexical-branch\", ")
                    .Write(name).WriteLine(");");
            }
        }
        WriteNativeAncestorNames(plan, scope, name, parentNameScope);
        _writer.Write(name).WriteLine(".Complete();");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteNativeAncestorNames(in ComponentPlan plan, in ComponentScopePlan scope, string name,
        string? parentNameScope)
    {
        var declarationId = 0;
        for (var ancestorId = scope.ParentScopeId; ancestorId >= 0; ancestorId = plan.Scopes.ItemRef(ancestorId).ParentScopeId)
        {
            ref readonly var ancestor = ref plan.Scopes.ItemRef(ancestorId);
            for (var i = 0; i < ancestor.Elements.Length; i++)
            {
                ref readonly var element = ref plan.Elements.ItemRef(GetScopeElementId(plan, ancestor, i));
                for (var actionIndex = 0; actionIndex < element.FirstUpdateActions.Length; actionIndex++)
                {
                    ref readonly var action = ref plan.FirstUpdateActions.ItemRef(element.FirstUpdateActions.Start + actionIndex);
                    if (action.Kind != ComponentFirstUpdateActionKind.NameAssignment)
                    {
                        continue;
                    }
                    ref readonly var assignment = ref plan.NameAssignments.ItemRef(action.Index);
                    if (parentNameScope == null && (!element.UsesRuntimeStorage || scope.OwnerElementId < 0 ||
                        element.RuntimeStorageRootScopeId != plan.Elements.ItemRef(scope.OwnerElementId).RuntimeStorageRootScopeId))
                    {
                        continue;
                    }
                    _writer.Write("if (").Write(name).Write(".Find(").WriteStringLiteral(assignment.Name).WriteLine(") == null)");
                    _writer.WriteLine("{");
                    _writer.CurrentIndent += _writer.TabSize;
                    if (parentNameScope != null)
                    {
                        var source = "__ancestorName" + scope.Id + "_" + declarationId++;
                        _writer.Write("if (").Write(parentNameScope).Write(".Find(").WriteStringLiteral(assignment.Name)
                            .Write(") is { } ").Write(source).WriteLine(")");
                        _writer.WriteLine("{");
                        _writer.CurrentIndent += _writer.TabSize;
                        _writer.Write(name).Write(".Register(").WriteStringLiteral(assignment.Name)
                            .Write(", ").Write(source).WriteLine(");");
                        _writer.CurrentIndent -= _writer.TabSize;
                        _writer.WriteLine("}");
                    }
                    else if (element.UsesRuntimeStorage && scope.OwnerElementId >= 0 &&
                        element.RuntimeStorageRootScopeId == plan.Elements.ItemRef(scope.OwnerElementId).RuntimeStorageRootScopeId)
                    {
                        _writer.Write(name).Write(".Register(").WriteStringLiteral(assignment.Name)
                            .Write(", ").Write(element.Identifier).WriteLine(");");
                    }
                    _writer.CurrentIndent -= _writer.TabSize;
                    _writer.WriteLine("}");
                }
            }
            if (ancestorId == 0)
            {
                break;
            }
        }
    }

    private static bool IsNativeNameScopeRoot(in ComponentPlan plan, in ComponentScopePlan scope,
        in ComponentElementPlan element)
    {
        if (element.ParentId == scope.OwnerElementId)
        {
            return true;
        }
        for (var i = 0; i < scope.Roots.Length; i++)
        {
            if (plan.ScopeRootElementIds[scope.Roots.Start + i] == element.Id)
            {
                return true;
            }
        }
        return false;
    }
}
