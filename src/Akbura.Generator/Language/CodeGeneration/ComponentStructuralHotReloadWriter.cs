using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes the fixed CLR contract used to reconcile component-scope elements in
/// Debug builds. Only method bodies and render-plan data vary between revisions.
/// </summary>
internal readonly ref struct ComponentStructuralHotReloadWriter
{
    internal const string RenderStateFieldName = "__akburaRenderState";
    internal const string EnsureRenderTreeMethodName = "__AkburaEnsureRenderTree";

    private const string RevisionMethodName = "__AkburaCurrentRenderRevision";
    private const string DescribeMethodName = "__AkburaDescribeRenderTree";
    private const string FactoryMethodName = "__AkburaCreateRenderNode";

    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;

    public ComponentStructuralHotReloadWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
    }

    public void WriteFields(in ComponentPlan plan)
    {
        _writer.WriteLine(
            "private readonly global::Akbura.HotReload.AkburaRenderState " +
            RenderStateFieldName + " = new();");

        for (var i = 0; i < plan.Elements.Length; i++)
        {
            ref readonly var element = ref plan.Elements.ItemRef(i);
            if (!element.UsesRuntimeStorage ||
                element.RuntimeStorageRootScopeId != 0 ||
                element.ConditionalRegionId >= 0 ||
                !element.HasName ||
                string.IsNullOrEmpty(element.ExplicitKey))
            {
                continue;
            }

            _writer.WriteLine();
            _writer.Write("private ");
            _valueWriter.WriteTypeName(element.Type);
            _writer.Write(" ");
            _valueWriter.WriteIdentifier(element.ExplicitKey!);
            _writer.WriteLine(" =>");
            _writer.CurrentIndent += _writer.TabSize;
            _writer.Write(element.Identifier);
            _writer.WriteLine(";");
            _writer.CurrentIndent -= _writer.TabSize;
        }
    }

    public void WriteMembers(in ComponentPlan plan)
    {
        WriteRevisionMethod(plan);
        _writer.WriteLine();
        WriteDescriptionMethod(plan);
        _writer.WriteLine();
        WriteFactoryMethod(plan);
        _writer.WriteLine();
        WriteEnsureMethod();
    }

    private void WriteRevisionMethod(in ComponentPlan plan)
    {
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private static string ");
        _writer.Write(RevisionMethodName);
        _writer.WriteLine("() =>");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreateRenderFingerprint(plan));
        _writer.WriteLine(";");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteDescriptionMethod(in ComponentPlan plan)
    {
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private static void ");
        _writer.Write(DescribeMethodName);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "global::Akbura.HotReload.AkburaRenderPlanBuilder __builder)");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        WriteDescriptionContents(plan, 0);
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    internal void WriteDescriptionContents(in ComponentPlan plan, int rootScopeId)
    {

        for (var i = 0; i < plan.Elements.Length; i++)
        {
            ref readonly var element = ref plan.Elements.ItemRef(i);
            if (!element.UsesRuntimeStorage || element.RuntimeStorageRootScopeId != rootScopeId)
            {
                continue;
            }

            _writer.WriteLine(
                "__builder.Add(new global::Akbura.HotReload." +
                "AkburaRenderNodeDefinition(");
            _writer.CurrentIndent += _writer.TabSize;
            _writer.WriteIntegerLiteral(element.RuntimeStorageId);
            _writer.WriteLine(",");
            _writer.WriteIntegerLiteral(GetRuntimeParentId(plan, element));
            _writer.WriteLine(",");
            _writer.WriteStringLiteral(GetRuntimeParentId(plan, element) < 0 ? "$root" : GetElementSlot(plan, element));
            _writer.WriteLine(",");
            _writer.Write("typeof(");
            _valueWriter.WriteTypeName(element.Type);
            _writer.WriteLine("),");

            if (element.ExplicitKey == null)
            {
                _writer.WriteLine("null,");
            }
            else
            {
                _writer.WriteStringLiteral(element.ExplicitKey);
                _writer.WriteLine(",");
            }

            _writer.WriteStringLiteral(
                ComponentHotReloadIdentity.CreateRenderSyntaxIdentity(
                    element.Syntax));
            _writer.WriteLine(",");
            var conditionalRuntimeId = element.ConditionalRegionId >= 0 &&
                plan.ConditionalRegions.ItemRef(element.ConditionalRegionId).RuntimeStorageRootScopeId == rootScopeId
                ? ComponentRuntimeScopeFacts.GetConditionalRuntimeId(plan, element.ConditionalRegionId)
                : -1;
            _writer.WriteIntegerLiteral(conditionalRuntimeId).WriteLine(",");
            _writer.WriteIntegerLiteral(conditionalRuntimeId >= 0 ? element.ConditionalBranchId : -1);
            _writer.WriteLine("));");
            _writer.CurrentIndent -= _writer.TabSize;
        }

        foreach (var region in plan.ConditionalRegions)
        {
            ref readonly var owner = ref plan.Elements.ItemRef(region.OwnerElementId);
            if (!owner.UsesRuntimeStorage || region.RuntimeStorageRootScopeId != rootScopeId)
            {
                continue;
            }
            _writer.WriteLine("__builder.AddConditional(new global::Akbura.HotReload.AkburaRenderConditionalDefinition(");
            _writer.CurrentIndent += _writer.TabSize;
            _writer.WriteIntegerLiteral(ComponentRuntimeScopeFacts.GetConditionalRuntimeId(plan, region.Id)).WriteLine(",");
            _writer.WriteIntegerLiteral(owner.RuntimeStorageId).WriteLine(",");
            _writer.WriteStringLiteral(GetConditionalSlot(plan, region.Id)).WriteLine(",");
            _writer.WriteStringLiteral(ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(region.Syntax)).WriteLine(",");
            _writer.WriteLine("new global::Akbura.HotReload.AkburaRenderConditionalBranchDefinition[]");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
            foreach (var branch in region.Branches)
            {
                _writer.Write("new(");
                _writer.WriteStringLiteral(branch.Condition?.ToString() ?? string.Empty).Write(", ");
                _writer.WriteStringLiteral(branch.ShapeIdentity).WriteLine("),");
            }

            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("},");
            var parentRuntimeId = ComponentRuntimeScopeFacts.GetConditionalParentRuntimeId(plan, region.Id);
            _writer.WriteIntegerLiteral(parentRuntimeId).WriteLine(",");
            _writer.WriteIntegerLiteral(parentRuntimeId >= 0 ? region.ParentBranchId : -1).WriteLine("));");
            _writer.CurrentIndent -= _writer.TabSize;
        }

    }

    private void WriteFactoryMethod(in ComponentPlan plan)
    {
        _writer.WriteHiddenApiAttributes();
        _writer.WriteLine(
            "private static object __AkburaCreateRenderNode(int __localId)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("return __localId switch");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        for (var i = 0; i < plan.Elements.Length; i++)
        {
            ref readonly var element = ref plan.Elements.ItemRef(i);
            if (!element.UsesRuntimeStorage || element.RuntimeStorageRootScopeId != 0)
            {
                continue;
            }

            _writer.WriteIntegerLiteral(element.RuntimeStorageId);
            _writer.Write(" => new ");
            _valueWriter.WriteTypeName(element.Type);
            _writer.WriteLine("(),");
        }

        _writer.WriteLine(
            "_ => throw new global::System.ArgumentOutOfRangeException(" +
            "nameof(__localId)),");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("};");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteEnsureMethod()
    {
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private bool ");
        _writer.Write(EnsureRenderTreeMethodName);
        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("return ");
        _writer.Write(RenderStateFieldName);
        _writer.WriteLine(".BeginRevision(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(RevisionMethodName);
        _writer.WriteLine("(),");
        _writer.Write(DescribeMethodName);
        _writer.WriteLine(",");
        _writer.Write(FactoryMethodName);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private static int GetRuntimeParentId(
        in ComponentPlan plan,
        in ComponentElementPlan element)
    {
        return ComponentRuntimeScopeFacts.GetRuntimeParentId(plan, element);
    }

    internal static string GetElementSlot(
        in ComponentPlan plan,
        in ComponentElementPlan element)
    {
        if (element.ParentId < 0)
        {
            return "$root";
        }

        foreach (var content in plan.ConditionalContents)
        {
            if (content.OwnerElementId == element.ParentId && ContainsConditionalElement(plan, content.Items, element.Id))
            {
                return GetConditionalContentSlot(content);
            }
        }

        for (var i = 0; i < plan.CollectionContents.Length; i++)
        {
            ref readonly var content = ref plan.CollectionContents.ItemRef(i);
            if (content.OwnerElementId != element.ParentId)
            {
                continue;
            }

            for (var itemIndex = 0; itemIndex < content.Items.Length; itemIndex++)
            {
                ref readonly var item = ref plan.ContentItems.ItemRef(
                    content.Items.Start + itemIndex);
                if (item.Value.Kind == ComponentContentValueKind.Element &&
                    item.Value.Index == element.Id)
                {
                    return ComponentHotReloadIdentity.CreateCollectionSlot(
                        content.Destination);
                }
            }
        }

        for (var i = 0; i < plan.PropertyContents.Length; i++)
        {
            ref readonly var content = ref plan.PropertyContents.ItemRef(i);
            if (content.OwnerElementId == element.ParentId &&
                (IsElementReference(content.FirstUpdateValue, element.Id) ||
                    IsElementReference(content.UpdateValue, element.Id)))
            {
                return ComponentHotReloadIdentity.CreatePropertySlot(
                    content.Destination);
            }
        }

        return "content";
    }

    internal static string GetConditionalContentSlot(in ComponentConditionalContentPlan content) =>
        content.Collection.IsValid ? ComponentHotReloadIdentity.CreateCollectionSlot(content.Collection)
            : ComponentHotReloadIdentity.CreatePropertySlot(content.Property);

    private static string GetConditionalSlot(in ComponentPlan plan, int regionId)
    {
        foreach (var content in plan.ConditionalContents)
        {
            if (ContainsConditionalRegion(plan, content.Items, regionId))
            {
                return GetConditionalContentSlot(content);
            }
        }

        return "content";
    }

    private static bool ContainsConditionalElement(in ComponentPlan plan, ComponentPlanRange items, int elementId)
    {
        for (var i = 0; i < items.Length; i++)
        {
            var value = plan.ContentItems.ItemRef(items.Start + i).Value;
            if (value.Kind == ComponentContentValueKind.Element && value.Index == elementId)
            {
                return true;
            }

            if (value.Kind == ComponentContentValueKind.Conditional)
            {
                foreach (var branch in plan.ConditionalRegions.ItemRef(value.Index).Branches)
                {
                    if (ContainsConditionalElement(plan, branch.Items, elementId))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool ContainsConditionalRegion(in ComponentPlan plan, ComponentPlanRange items, int regionId)
    {
        for (var i = 0; i < items.Length; i++)
        {
            var value = plan.ContentItems.ItemRef(items.Start + i).Value;
            if (value.Kind != ComponentContentValueKind.Conditional)
            {
                continue;
            }

            if (value.Index == regionId)
            {
                return true;
            }

            foreach (var branch in plan.ConditionalRegions.ItemRef(value.Index).Branches)
            {
                if (ContainsConditionalRegion(plan, branch.Items, regionId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsElementReference(
        in ComponentContentValueReference value,
        int elementId)
    {
        return value.Kind == ComponentContentValueKind.Element &&
            value.Index == elementId;
    }
}
