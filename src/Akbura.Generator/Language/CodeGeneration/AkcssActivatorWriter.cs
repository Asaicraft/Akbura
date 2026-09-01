using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Microsoft.CodeAnalysis;
using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct AkcssFactoryWriteContext
{
    public AkcssFactoryWriteContext(
        int elementId,
        in MarkupExtensionWriteContext markupExtensionContext)
    {
        ElementId = elementId;
        MarkupExtensionContext = markupExtensionContext;
    }

    public int ElementId { get; }

    public MarkupExtensionWriteContext MarkupExtensionContext { get; }
}

internal readonly ref struct AkcssActivatorWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly BindingWriterEnvironment _bindingEnvironment;

    public AkcssActivatorWriter(
        CodeWriter writer,
        in BindingWriterEnvironment bindingEnvironment)
    {
        Debug.Assert(writer != null);

        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _valueWriter = new CSharpValueWriter(_writer);
        _bindingEnvironment = bindingEnvironment;
    }

    public void WriteStaticMembers(in AkcssComponentActivatorPlan plan)
    {
        var hasPreviousSection = false;

        if (!plan.ClassCaches.IsDefaultOrEmpty)
        {
            WriteClassActivatorFields(plan);
            hasPreviousSection = true;
        }

        if (!plan.ApplicationCaches.IsDefaultOrEmpty)
        {
            if (hasPreviousSection)
            {
                _writer.WriteLine();
            }

            WriteApplicationFields(plan);
            hasPreviousSection = true;
        }

        WriteMarkupExtensionTargetProperties(plan, hasPreviousSection);
    }

    public void WriteFactoryMethods(
        in AkcssComponentActivatorPlan plan,
        in AkcssFactoryWriteContext context)
    {
        var wroteMethod = false;
        var slots = plan.MarkupExtensionSlots;

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];

            if (!slot.NeedsFactoryMethod || slot.ElementId != context.ElementId)
            {
                continue;
            }

            if (wroteMethod)
            {
                _writer.WriteLine();
            }

            WriteFactoryMethod(slot, context.MarkupExtensionContext);
            wroteMethod = true;
        }
    }

    public void WriteSetStyles(
        in AkcssComponentActivatorPlan plan,
        in AkcssPlanRange activators,
        string targetExpression,
        in MarkupExtensionWriteContext context)
    {
        if (activators.IsEmpty)
        {
            return;
        }

        EnsureRange(activators, plan.Activators.Length, nameof(activators));
        EnsureExpression(targetExpression, nameof(targetExpression));

        var indent = _writer.CurrentIndent;

        _writer.WriteLine("global::Akbura.AkburaControl.SetAkcssStyles(");
        _writer.CurrentIndent = indent + 4;
        _writer.Write(targetExpression).WriteLine(",");
        _writer.WriteLine(
            "global::System.Collections.Immutable.ImmutableArray.Create<" +
            "global::Akbura.Akcss.AkcssStyleActivator>(");
        _writer.CurrentIndent = indent + 8;

        for (var i = 0; i < activators.Length; i++)
        {
            WriteActivator(plan, plan.Activators[activators.Start + i], context);
            _writer.WriteLine(i + 1 == activators.Length ? string.Empty : ",");
        }

        _writer.CurrentIndent = indent + 4;
        _writer.WriteLine("));");
        _writer.CurrentIndent = indent;
    }

    public void WriteRefresh(
        in AkcssPlanRange activators,
        string targetExpression)
    {
        if (activators.IsEmpty)
        {
            return;
        }

        EnsureExpression(targetExpression, nameof(targetExpression));

        _writer
            .Write("global::Akbura.AkburaControl.ExecuteAkcssStyles(")
            .Write(targetExpression)
            .WriteLine(");");
    }

    private void WriteClassActivatorFields(in AkcssComponentActivatorPlan plan)
    {
        var classes = plan.ClassCaches;

        for (var i = 0; i < classes.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            var item = classes[i];
            var indent = _writer.CurrentIndent;

            _writer.Write("private static readonly global::Akbura.Akcss.AkcssClassActivator ");
            WriteClassFieldName(item.Id);
            _writer.WriteLine(" =");
            _writer.CurrentIndent = indent + 4;
            _writer.Write("new((global::Akbura.Akcss.AkcssClass)");
            WriteStyleReference(item.Style);
            _writer.WriteLine(");");
            _writer.CurrentIndent = indent;
        }
    }

    private void WriteApplicationFields(in AkcssComponentActivatorPlan plan)
    {
        var caches = plan.ApplicationCaches;

        for (var i = 0; i < caches.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            WriteApplicationField(plan, caches[i]);
        }
    }

    private void WriteApplicationField(
        in AkcssComponentActivatorPlan plan,
        in AkcssUtilityApplicationCachePlan cache)
    {
        EnsureRange(cache.Applications, plan.Applications.Length, nameof(cache.Applications));

        var indent = _writer.CurrentIndent;

        _writer
            .Write("private static readonly global::System.Collections.Immutable.ImmutableArray<")
            .Write("global::Akbura.Akcss.AkcssUtilityApplication> ");
        WriteApplicationFieldName(cache.Id);
        _writer.WriteLine(" =");
        _writer.CurrentIndent = indent + 4;
        _writer.WriteLine(
            "global::System.Collections.Immutable.ImmutableArray.Create<" +
            "global::Akbura.Akcss.AkcssUtilityApplication>(");
        _writer.CurrentIndent = indent + 8;

        var range = cache.Applications;

        for (var i = 0; i < range.Length; i++)
        {
            WriteApplication(plan.Applications[range.Start + i]);
            _writer.WriteLine(i + 1 == range.Length ? string.Empty : ",");
        }

        _writer.CurrentIndent = indent + 4;
        _writer.WriteLine(");");
        _writer.CurrentIndent = indent;
    }

    private void WriteApplication(in AkcssUtilityApplicationPlan plan)
    {
        var indent = _writer.CurrentIndent;

        _writer.WriteLine("new global::Akbura.Akcss.AkcssUtilityApplication(");
        _writer.CurrentIndent = indent + 4;
        _writer.Write("(global::Akbura.Akcss.AkcssUtility)");
        WriteStyleReference(plan.Reference);
        _writer.WriteLine(",");
        _writer.WriteLine("static (__target, __arguments) =>");
        _writer.CurrentIndent = indent + 8;
        _writer.Write("((");
        WriteRuntimeUtilityType(plan.Utility);
        _writer.Write(")");
        WriteStyleReference(plan.Reference);
        _writer.WriteLine(").Update(");
        _writer.CurrentIndent = indent + 12;
        _writer.Write("__target");

        var parameters = plan.Utility.Parameters;

        for (var i = 0; i < parameters.Length; i++)
        {
            _writer.WriteLine(",");
            _writer.Write("(");
            _valueWriter.WriteTypeName(parameters[i].Type.Symbol);
            _writer.Write(")__arguments[").WriteIntegerLiteral(i).Write("]!");
        }

        _writer.Write("))");
        _writer.CurrentIndent = indent;
    }

    private void WriteMarkupExtensionTargetProperties(
        in AkcssComponentActivatorPlan plan,
        bool writeLeadingBlankLine)
    {
        var slots = plan.MarkupExtensionSlots;
        var wroteProperty = false;

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];

            if (!slot.NeedsTargetProperty)
            {
                continue;
            }

            if (wroteProperty || writeLeadingBlankLine)
            {
                _writer.WriteLine();
            }

            WriteMarkupExtensionTargetProperty(slot);
            wroteProperty = true;
            writeLeadingBlankLine = false;
        }
    }

    private void WriteMarkupExtensionTargetProperty(in AkcssMarkupExtensionSlotPlan slot)
    {
        var indent = _writer.CurrentIndent;
        var ownerType = _bindingEnvironment.WithinType;

        if (ownerType == null)
        {
            throw new InvalidOperationException("AKCSS target properties require a component type.");
        }

        _writer
            .Write("private static readonly global::Avalonia.AttachedProperty<object?> ")
            .Write(slot.PropertyName)
            .WriteLine(" =");
        _writer.CurrentIndent = indent + 4;
        _writer
            .Write("global::Avalonia.AvaloniaProperty.RegisterAttached<");
        _valueWriter.WriteTypeName(ownerType);
        _writer.Write(", global::Avalonia.Controls.Control, object?>(");
        _writer.WriteStringLiteral(slot.PropertyName).WriteLine(");");
        _writer.CurrentIndent = indent;
    }

    private void WriteFactoryMethod(
        in AkcssMarkupExtensionSlotPlan slot,
        in MarkupExtensionWriteContext context)
    {
        var indent = _writer.CurrentIndent;

        _writer.Write("private ");
        WriteFactoryReturnType(slot.FactoryValueType, slot.HasPriorityMember);
        _writer.Write(" ").Write(slot.FactoryName).WriteLine("(");
        _writer.CurrentIndent = indent + 4;
        _writer.WriteLine(slot.IsControlTarget
            ? "global::Avalonia.Controls.Control __target)"
            : "object __target)");
        _writer.CurrentIndent = indent;
        _writer.WriteLine("{");
        _writer.CurrentIndent = indent + 4;

        var extensionContext = CreateExtensionContext(slot, context);
        var extensionWriter = new MarkupExtensionWriter(_writer, in _bindingEnvironment);

        if (slot.PriorityMember == null)
        {
            _writer.Write("return ");
            extensionWriter.Write(slot.Extension, extensionContext);
            _writer.WriteLine(";");
        }
        else
        {
            WritePriorityFactoryBody(slot, extensionWriter, extensionContext);
        }

        _writer.CurrentIndent = indent;
        _writer.WriteLine("}");
    }

    private void WritePriorityFactoryBody(
        in AkcssMarkupExtensionSlotPlan slot,
        MarkupExtensionWriter extensionWriter,
        in MarkupExtensionWriteContext context)
    {
        var indent = _writer.CurrentIndent;

        _writer.Write("var __extension = ");
        extensionWriter.WriteCreation(slot.Extension, context);
        _writer.WriteLine(";");
        _writer.Write("return new global::Akbura.Akcss.AkcssUtilityPrefixInvocation<");
        _valueWriter.WriteTypeName(slot.FactoryValueType);
        _writer.WriteLine(">(");
        _writer.CurrentIndent = indent + 4;
        extensionWriter.WriteProvideValueInvocation(slot.Extension, "__extension", context);
        _writer.WriteLine(",");
        _writer.Write("__extension.");
        _valueWriter.WriteIdentifier(slot.PriorityMember!.Name);
        _writer.WriteLine(");");
        _writer.CurrentIndent = indent;
    }

    private void WriteFactoryReturnType(
        ITypeSymbol resultType,
        bool hasPriorityMember)
    {
        if (hasPriorityMember)
        {
            _writer.Write("global::Akbura.Akcss.AkcssUtilityPrefixInvocation<");
            _valueWriter.WriteTypeName(resultType);
            _writer.Write(">");
            return;
        }

        _valueWriter.WriteTypeName(resultType);
    }

    private static MarkupExtensionWriteContext CreateExtensionContext(
        in AkcssMarkupExtensionSlotPlan slot,
        in MarkupExtensionWriteContext context)
    {
        return new MarkupExtensionWriteContext(
            targetObjectExpression: "__target",
            targetPropertyExpression: slot.NeedsTargetProperty ? slot.PropertyName : "default!",
            intermediateRootExpression: context.IntermediateRootExpression,
            baseUriExpression: context.BaseUriExpression,
            directParentsStackExpression: context.DirectParentsStackExpression,
            fallbackServiceProviderExpression: context.FallbackServiceProviderExpression,
            nameScopeExpression: context.NameScopeExpression,
            scopeId: context.ScopeId,
            elementReferences: context.ElementReferences);
    }

    private void WriteActivator(
        in AkcssComponentActivatorPlan plan,
        in AkcssActivatorPlan activator,
        in MarkupExtensionWriteContext context)
    {
        switch (activator.Kind)
        {
            case AkcssActivatorKind.Class:
                WriteClassFieldName(plan.ClassCaches[activator.Index].Id);
                return;

            case AkcssActivatorKind.UtilityCandidate:
                WriteCandidate(plan, plan.Candidates[activator.Index], context);
                return;

            default:
                throw new InvalidOperationException("Unexpected AKCSS activator kind.");
        }
    }

    private void WriteCandidate(
        in AkcssComponentActivatorPlan plan,
        in AkcssUtilityCandidatePlan candidate,
        in MarkupExtensionWriteContext context)
    {
        var indent = _writer.CurrentIndent;

        _writer.WriteLine("new global::Akbura.Akcss.AkcssUtilityCandidateActivator(");
        _writer.CurrentIndent = indent + 4;
        _writer.Write("conflictKey: ");
        _writer.WriteStringLiteral(candidate.ConflictKey).WriteLine(",");
        _writer.Write("sourceOrder: ").WriteIntegerLiteral(candidate.SourceOrder).WriteLine(",");
        _writer.Write("applications: ");
        WriteApplicationFieldName(candidate.ApplicationCacheId);

        if (!candidate.ValueSources.IsEmpty)
        {
            _writer.WriteLine(",");
            WriteCandidateValueSources(plan, candidate.ValueSources, context);
        }

        if (candidate.HasCondition)
        {
            _writer.WriteLine(",");
            _writer.Write("condition: () => ");
            WriteCondition(candidate);
        }

        if (candidate.VariantValueSourceIndex >= 0)
        {
            _writer.WriteLine(",");
            _writer.Write("variant: ");
            WriteValueSource(plan, plan.ValueSources[candidate.VariantValueSourceIndex], context);
        }

        WriteVariantArguments(candidate.Variant);
        WriteBindingPriority(plan, candidate.BindingPriority);
        _writer.WriteLine();
        _writer.CurrentIndent = indent;
        _writer.Write(")");
    }

    private void WriteCandidateValueSources(
        in AkcssComponentActivatorPlan plan,
        in AkcssPlanRange range,
        in MarkupExtensionWriteContext context)
    {
        EnsureRange(range, plan.ValueSources.Length, nameof(range));

        var indent = _writer.CurrentIndent;

        _writer.WriteLine(
            "arguments: global::System.Collections.Immutable.ImmutableArray.Create<" +
            "global::Akbura.Akcss.AkcssUtilityValueSource>(");
        _writer.CurrentIndent = indent + 4;

        for (var i = 0; i < range.Length; i++)
        {
            WriteValueSource(plan, plan.ValueSources[range.Start + i], context);
            _writer.WriteLine(i + 1 == range.Length ? string.Empty : ",");
        }

        _writer.CurrentIndent = indent;
        _writer.Write(")");
    }

    private void WriteCondition(in AkcssUtilityCandidatePlan candidate)
    {
        if (!candidate.ConditionOperation.IsDefault &&
            candidate.ConditionOperation.Syntax != null)
        {
            _writer.Write(candidate.ConditionOperation.Syntax.ToString());
            return;
        }

        _writer.Write(candidate.ConditionText ?? "false");
    }

    private void WriteVariantArguments(in TailwindUtilityVariant variant)
    {
        if (!variant.IsPrefixed)
        {
            return;
        }

        _writer.WriteLine(",");
        _writer.Write("order: ");
        _valueWriter.WriteConstant(variant.Order, targetType: null);

        if (variant.ConflictGroup != null)
        {
            _writer.WriteLine(",");
            _writer.Write("conflictGroup: ");
            _writer.WriteStringLiteral(variant.ConflictGroup);
        }

        _writer.WriteLine(",");
        _writer
            .Write("unprefixedPrecedence: global::Akbura.Markup.UnprefixedUtilityPrecedence.")
            .Write(variant.UnprefixedPrecedence.ToString());
    }

    private void WriteBindingPriority(
        in AkcssComponentActivatorPlan plan,
        in TailwindUtilityBindingPriority priority)
    {
        if (priority.Source != TailwindUtilityBindingPrioritySource.Constant)
        {
            return;
        }

        _writer.WriteLine(",");
        _writer.Write("bindingPriority: ");
        _valueWriter.WriteConstant(priority.ConstantValue, plan.BindingPriorityType);
    }

    private void WriteValueSource(
        in AkcssComponentActivatorPlan componentPlan,
        in AkcssUtilityValueSourcePlan plan,
        in MarkupExtensionWriteContext context)
    {
        _writer.Write("global::Akbura.Akcss.AkcssUtilityValueSource.");

        switch (plan.Kind)
        {
            case AkcssUtilityValueSourceKind.Direct:
                WriteDirectValueSource(componentPlan, plan, context);
                return;

            case AkcssUtilityValueSourceKind.Object:
                WriteObjectValueSource(componentPlan, plan, context);
                return;

            case AkcssUtilityValueSourceKind.Observable:
                WriteObservableValueSource(componentPlan, plan, context);
                return;

            case AkcssUtilityValueSourceKind.ObservableObject:
                WriteObservableObjectValueSource(componentPlan, plan, context);
                return;

            case AkcssUtilityValueSourceKind.Binding:
                WriteBindingValueSource(componentPlan, plan, context);
                return;

            default:
                throw new InvalidOperationException("Unexpected AKCSS value-source kind.");
        }
    }

    private void WriteDirectValueSource(
        in AkcssComponentActivatorPlan componentPlan,
        in AkcssUtilityValueSourcePlan plan,
        in MarkupExtensionWriteContext context)
    {
        if (!plan.IsControlTarget)
        {
            _writer.Write(plan.HasPriorityMember
                ? "CreateForObjectWithPriority<"
                : "CreateForObject<");
        }
        else
        {
            _writer.Write(plan.HasPriorityMember ? "CreateWithPriority<" : "Create<");
        }

        _valueWriter.WriteTypeName(plan.ExpectedType);
        _writer.Write(">(");
        WriteValueFactory(componentPlan, plan, context);
        WriteRecreateOnRefresh(plan.RecreateOnRefresh);
    }

    private void WriteObjectValueSource(
        in AkcssComponentActivatorPlan componentPlan,
        in AkcssUtilityValueSourcePlan plan,
        in MarkupExtensionWriteContext context)
    {
        _writer.Write(plan.HasPriorityMember ? "CreateObjectWithPriority<" : "CreateObject<");
        _valueWriter.WriteTypeName(plan.ExpectedType);
        _writer.Write(">(");
        WriteMarkupExtensionFactory(componentPlan, plan, context);
        WriteObjectConverter(plan.ExpectedType, useNullForgivingOperator: true);
        WriteRecreateOnRefresh(plan.RecreateOnRefresh);
    }

    private void WriteObservableValueSource(
        in AkcssComponentActivatorPlan componentPlan,
        in AkcssUtilityValueSourcePlan plan,
        in MarkupExtensionWriteContext context)
    {
        if (plan.ObservableElementType == null)
        {
            throw new InvalidOperationException("An observable AKCSS value source has no element type.");
        }

        _writer.Write(plan.HasPriorityMember
            ? "CreateObservableWithPriority<"
            : "CreateObservable<");
        _valueWriter.WriteTypeName(plan.ObservableElementType);
        _writer.Write(", ");
        _valueWriter.WriteTypeName(plan.ExpectedType);
        _writer.Write(">(");
        WriteMarkupExtensionFactory(componentPlan, plan, context);
        WriteObjectConverter(plan.ExpectedType, useNullForgivingOperator: false);
        WriteRecreateOnRefresh(plan.RecreateOnRefresh);
    }

    private void WriteObservableObjectValueSource(
        in AkcssComponentActivatorPlan componentPlan,
        in AkcssUtilityValueSourcePlan plan,
        in MarkupExtensionWriteContext context)
    {
        _writer.Write(plan.HasPriorityMember
            ? "CreateObservableObjectWithPriority<"
            : "CreateObservableObject<");
        _valueWriter.WriteTypeName(plan.ExpectedType);
        _writer.Write(">(");
        WriteMarkupExtensionFactory(componentPlan, plan, context);
        WriteObjectConverter(plan.ExpectedType, useNullForgivingOperator: true);
        WriteRecreateOnRefresh(plan.RecreateOnRefresh);
    }

    private void WriteBindingValueSource(
        in AkcssComponentActivatorPlan componentPlan,
        in AkcssUtilityValueSourcePlan plan,
        in MarkupExtensionWriteContext context)
    {
        var slot = GetSlot(componentPlan, plan.MarkupExtensionSlotId);

        if (!slot.NeedsTargetProperty)
        {
            throw new InvalidOperationException("A binding AKCSS value source requires a target property.");
        }

        _writer.Write(plan.HasPriorityMember ? "CreateBindingWithPriority<" : "CreateBinding<");
        _valueWriter.WriteTypeName(plan.ExpectedType);
        _writer.Write(">(");
        WriteMarkupExtensionFactory(componentPlan, plan, context);
        _writer.Write(", ").Write(slot.PropertyName);
        WriteObjectConverter(plan.ExpectedType, useNullForgivingOperator: true);
        WriteRecreateOnRefresh(plan.RecreateOnRefresh);
    }

    private void WriteObjectConverter(
        ITypeSymbol expectedType,
        bool useNullForgivingOperator)
    {
        _writer.Write(", static __value => (");
        _valueWriter.WriteTypeName(expectedType);
        _writer.Write(")__value");

        if (useNullForgivingOperator)
        {
            _writer.Write("!");
        }
    }

    private void WriteRecreateOnRefresh(bool recreateOnRefresh)
    {
        _writer.Write(", recreateOnRefresh: ");
        _writer.WriteBooleanLiteral(recreateOnRefresh);
        _writer.Write(")");
    }

    private void WriteValueFactory(
        in AkcssComponentActivatorPlan componentPlan,
        in AkcssUtilityValueSourcePlan plan,
        in MarkupExtensionWriteContext context)
    {
        if (plan.Extension != null)
        {
            WriteMarkupExtensionFactory(componentPlan, plan, context);
            return;
        }

        if (!plan.RecreateOnRefresh)
        {
            _writer.Write("static ");
        }

        _writer.Write("__target => ");
        WriteArgumentExpression(plan);
    }

    private void WriteArgumentExpression(in AkcssUtilityValueSourcePlan plan)
    {
        var operation = plan.Argument.ValueOperation;

        if (!operation.IsDefault && operation.Syntax != null)
        {
            _writer.Write(operation.Syntax.ToString());
            return;
        }

        if (operation.ConstantValue.HasValue)
        {
            _valueWriter.WriteConstant(operation.ConstantValue.Value, plan.ExpectedType);
            return;
        }

        if (plan.Argument.ConstantValue != null)
        {
            _valueWriter.WriteConstant(plan.Argument.ConstantValue, plan.ExpectedType);
            return;
        }

        _writer.Write(plan.Argument.Text);
    }

    private void WriteMarkupExtensionFactory(
        in AkcssComponentActivatorPlan componentPlan,
        in AkcssUtilityValueSourcePlan plan,
        in MarkupExtensionWriteContext context)
    {
        if (plan.Extension == null)
        {
            throw new InvalidOperationException("An AKCSS markup-extension value source has no extension.");
        }

        var slot = GetSlot(componentPlan, plan.MarkupExtensionSlotId);

        if (plan.UseFactoryMethod)
        {
            if (!slot.NeedsFactoryMethod)
            {
                throw new InvalidOperationException("The AKCSS value source references an unavailable factory.");
            }

            _writer.Write(slot.FactoryName);
            return;
        }

        var extensionContext = CreateExtensionContext(slot, context);
        var extensionWriter = new MarkupExtensionWriter(_writer, in _bindingEnvironment);

        _writer.Write("__target => ");

        if (!plan.HasPriorityMember)
        {
            extensionWriter.Write(plan.Extension, extensionContext);
            return;
        }

        if (plan.PriorityMember == null)
        {
            throw new InvalidOperationException("A priority-aware AKCSS value source has no priority member.");
        }

        _writer.Write("{ var __extension = ");
        extensionWriter.WriteCreation(plan.Extension, extensionContext);
        _writer.Write("; return new global::Akbura.Akcss.AkcssUtilityPrefixInvocation<");
        _valueWriter.WriteTypeName(slot.FactoryValueType);
        _writer.Write(">(");
        extensionWriter.WriteProvideValueInvocation(plan.Extension, "__extension", extensionContext);
        _writer.Write(", __extension.");
        _valueWriter.WriteIdentifier(plan.PriorityMember.Name);
        _writer.Write("); }");
    }

    private void WriteRuntimeUtilityType(Akbura.Language.Symbols.ITailwindUtilitySymbol utility)
    {
        var parameters = utility.Parameters;

        if (parameters.IsDefaultOrEmpty)
        {
            _writer.Write("global::Akbura.Akcss.ZeroAkcssUtility");
            return;
        }

        _writer.Write("global::Akbura.Akcss.AkcssUtility<");

        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            _valueWriter.WriteTypeName(parameters[i].Type.Symbol);
        }

        _writer.Write(">");
    }

    private void WriteStyleReference(in AkcssStyleReferencePlan reference)
    {
        if (!reference.IsValid)
        {
            throw new InvalidOperationException("An invalid AKCSS style reference reached code generation.");
        }

        switch (reference.Kind)
        {
            case AkcssStyleReferenceKind.MetadataModule:
                _valueWriter.WriteTypeName(reference.RuntimeModuleType);
                break;

            case AkcssStyleReferenceKind.GeneratedModule:
                _writer.Write(reference.GeneratedModuleTypeName!);
                break;

            default:
                throw new InvalidOperationException("Unexpected AKCSS style-reference kind.");
        }

        _writer.Write(".Styles[").WriteIntegerLiteral(reference.StyleIndex).Write("]");
    }

    private void WriteClassFieldName(int id)
    {
        _writer.Write("s_akcssClass").WriteIntegerLiteral(id);
    }

    private void WriteApplicationFieldName(int id)
    {
        _writer.Write("s_akcssApplications").WriteIntegerLiteral(id);
    }

    private static AkcssMarkupExtensionSlotPlan GetSlot(
        in AkcssComponentActivatorPlan plan,
        int slotId)
    {
        var slots = plan.MarkupExtensionSlots;

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Id == slotId)
            {
                return slots[i];
            }
        }

        throw new InvalidOperationException("The AKCSS markup-extension slot was not found.");
    }

    private static void EnsureRange(
        in AkcssPlanRange range,
        int collectionLength,
        string parameterName)
    {
        if (range.Start < 0 ||
            range.Length < 0 ||
            range.Start > collectionLength - range.Length)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void EnsureExpression(string expression, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("A generated expression is required.", parameterName);
        }
    }
}
