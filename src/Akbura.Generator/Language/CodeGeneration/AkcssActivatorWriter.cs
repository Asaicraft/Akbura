using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct AkcssActivatorWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly SourceMappingWriter _sourceMappingWriter;
    private readonly bool _hasSourceMap;
    private readonly string _ownerTypeName;
    private readonly ComponentGenerationMode _generationMode;
    private readonly bool _forIdentity;

    public AkcssActivatorWriter(
        CodeWriter writer,
        in BindingWriterEnvironment bindingEnvironment,
        string ownerTypeName,
        ComponentGenerationSourceMap? sourceMap = null,
        ComponentGenerationMode generationMode =
            ComponentGenerationMode.ReleaseDirect,
        bool forIdentity = false)
    {
        Debug.Assert(writer != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _valueWriter = new CSharpValueWriter(_writer);
        _bindingEnvironment = bindingEnvironment;

        if (string.IsNullOrEmpty(ownerTypeName))
        {
            throw new ArgumentException("The owner type name cannot be empty.", nameof(ownerTypeName));
        }

        _ownerTypeName = ownerTypeName;
        _generationMode = generationMode;
        _forIdentity = forIdentity;
        _hasSourceMap = sourceMap != null;
        _sourceMappingWriter = sourceMap != null
            ? new SourceMappingWriter(_writer, sourceMap)
            : default;
    }

    public void WriteStaticMembers(in AkcssComponentActivatorPlan plan)
    {
        if (_generationMode.UsesStructuralRuntime())
        {
            return;
        }

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

    public bool WriteFactoryMethods(
        in AkcssComponentActivatorPlan plan,
        in AkcssElementActivatorPlan element,
        in MarkupExtensionWriteContext context)
    {
        if (_generationMode.UsesStructuralRuntime())
        {
            return false;
        }

        var range = element.MarkupExtensionSlots;
        if (range.IsEmpty)
        {
            return false;
        }

        EnsureRange(range, plan.MarkupExtensionSlots.Length, nameof(element));

        var wroteMethod = false;

        for (var i = 0; i < range.Length; i++)
        {
            var slot = plan.MarkupExtensionSlots[range.Start + i];

            if (!slot.NeedsFactoryMethod)
            {
                continue;
            }

            if (wroteMethod)
            {
                _writer.WriteLine();
            }

            WriteFactoryMethod(slot, context);
            wroteMethod = true;
        }

        return wroteMethod;
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

        _writer.Write("global::Akbura.AkburaControl.");
        _writer.WriteLine(
            _generationMode.UsesStructuralRuntime()
                ? "ReplaceAkcssStylesForHotReload("
                : "SetAkcssStyles(");
        _writer.CurrentIndent = indent + 4;
        _writer.Write(targetExpression).WriteLine(",");
        WriteActivatorArray(plan, activators, context);
        _writer.WriteLine(");");
        _writer.CurrentIndent = indent;
    }

    public void WriteApplyHotReloadStyles(
        in AkcssComponentActivatorPlan plan,
        in AkcssPlanRange activators,
        int localId,
        string targetExpression,
        in MarkupExtensionWriteContext context,
        bool canRetainCascade = true)
    {
        if (activators.IsEmpty)
        {
            return;
        }

        EnsureRange(activators, plan.Activators.Length, nameof(activators));
        EnsureExpression(targetExpression, nameof(targetExpression));

        var indent = _writer.CurrentIndent;
        var knownRevision = _generationMode == ComponentGenerationMode.DebugStructural && canRetainCascade
            ? WriteRevisionGuard(plan, activators, localId, context)
            : null;
        var registered = knownRevision != null;
        if (registered)
        {
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
        }

        if (registered && CanWriteDescriptors(plan, activators))
        {
            WriteReconciliation(plan, activators, localId, targetExpression, context, knownRevision!);
            _writer.CurrentIndent = indent;
            _writer.WriteLine("}");
            return;
        }

        _writer
            .Write(ComponentStructuralHotReloadWriter.RenderStateFieldName)
            .WriteLine(".ApplyAkcssStylesOperation(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(localId).WriteLine(",");
        _writer.Write(targetExpression).WriteLine(",");
        WriteActivatorArray(plan, activators, context);
        if (registered)
        {
            _writer.WriteLine(",");
            _writer.Write("operationRegistered: ");
            _writer.Write(knownRevision!);
        }
        _writer.WriteLine(");");
        if (registered)
        {
            _writer.CurrentIndent = indent;
            _writer.WriteLine("}");
        }
        _writer.CurrentIndent = indent;
    }

    private string? WriteRevisionGuard(
        in AkcssComponentActivatorPlan plan,
        in AkcssPlanRange activators,
        int localId,
        in MarkupExtensionWriteContext context)
    {
        var references = new List<AkcssStyleReferencePlan>();
        for (var index = activators.Start; index < activators.Start + activators.Length; index++)
        {
            var activator = plan.Activators[index];
            if (activator.Kind == AkcssActivatorKind.Class)
            {
                references.Add(plan.ClassCaches[activator.Index].Style);
            }
            else
            {
                var candidate = plan.Candidates[activator.Index];
                var applications = GetApplicationCache(plan, candidate.ApplicationCacheId).Applications;
                for (var applicationIndex = applications.Start; applicationIndex < applications.Start + applications.Length; applicationIndex++)
                {
                    references.Add(plan.Applications[applicationIndex].Reference);
                }
            }
        }

        var revisionName = "__akcssRevision" + localId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var reliable = references.Count != 0 &&
            references.TrueForAll(static reference => reference.Kind == AkcssStyleReferenceKind.GeneratedModule);
        if (!reliable)
        {
            return null;
        }
        using var code = new CodeWriter("\n");
        new AkcssActivatorWriter(code, _bindingEnvironment, _ownerTypeName,
            generationMode: _generationMode).WriteActivatorArray(plan, activators, context);
        _writer.Write("var ");
        _writer.Write(revisionName);
        _writer.Write(" = ");
        _writer.WriteStringLiteral(AkcssHotReloadIdentity.Create(code.GetText().ToString()));
        _writer.WriteLine(";");
        for (var index = 0; index < references.Count; index++)
        {
            _writer.Write("var ");
            WriteReferenceRevisionName(revisionName, index);
            _writer.Write(" = ");
            _writer.Write(references[index].GeneratedModuleTypeName!);
            _writer.Write(".");
            _writer.Write(AkcssModuleWriter.DebugStyleRevisionName);
            _writer.Write("(");
            _writer.WriteIntegerLiteral(references[index].StyleIndex);
            _writer.WriteLine(");");
        }
        _writer.Write("var ");
        _writer.Write(revisionName + "Known = ");
        for (var index = 0; index < references.Count; index++)
        {
            if (index != 0)
            {
                _writer.Write(" && ");
            }
            WriteReferenceRevisionName(revisionName, index);
            _writer.Write(" != null");
        }
        _writer.WriteLine(";");
        _writer.Write(revisionName);
        _writer.Write(" += ");
        for (var index = 0; index < references.Count; index++)
        {
            if (index != 0)
            {
                _writer.Write(" + ");
            }
            WriteReferenceRevisionName(revisionName, index);
        }
        _writer.WriteLine(";");

        _writer.Write("if (!");
        _writer.Write(revisionName + "Known || ");
        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.Write(".ShouldApplyOwnedOperation(");
        _writer.WriteIntegerLiteral(localId);
        _writer.Write(", \"$akcss\", ");
        _writer.Write(revisionName);
        _writer.WriteLine("))");
        return revisionName + "Known";
    }

    private void WriteReferenceRevisionName(string revisionName, int index)
    {
        _writer.Write(revisionName + "_");
        _writer.WriteIntegerLiteral(index);
    }

    private static bool CanWriteDescriptors(in AkcssComponentActivatorPlan plan, in AkcssPlanRange range)
    {
        for (var index = range.Start; index < range.Start + range.Length; index++)
        {
            var activator = plan.Activators[index];
            if (activator.Kind != AkcssActivatorKind.UtilityCandidate)
            {
                return false;
            }
            var candidate = plan.Candidates[activator.Index];
            if (candidate.VariantValueSourceIndex >= 0)
            {
                var variant = plan.ValueSources[candidate.VariantValueSourceIndex];
                if (variant.Kind == AkcssUtilityValueSourceKind.Observable &&
                    !SymbolEqualityComparer.Default.Equals(variant.ExpectedType, variant.ObservableElementType))
                {
                    return false;
                }
            }
            for (var valueIndex = candidate.ValueSources.Start;
                 valueIndex < candidate.ValueSources.Start + candidate.ValueSources.Length; valueIndex++)
            {
                var value = plan.ValueSources[valueIndex];
                if (value.Kind == AkcssUtilityValueSourceKind.Observable &&
                    !SymbolEqualityComparer.Default.Equals(value.ExpectedType, value.ObservableElementType))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private void WriteReconciliation(in AkcssComponentActivatorPlan plan, in AkcssPlanRange range,
        int localId, string target, in MarkupExtensionWriteContext context, string knownRevision)
    {
        var names = new string[range.Length];
        var identities = new string[range.Length];
        var revisions = new string[range.Length];
        var familyOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var anchorOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < range.Length; index++)
        {
            var candidate = plan.Candidates[plan.Activators[range.Start + index].Index];
            familyOccurrences.TryGetValue(candidate.ConflictKey, out var occurrence);
            familyOccurrences[candidate.ConflictKey] = occurrence + 1;
            identities[index] = candidate.ConflictKey + "/" + occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            anchorOccurrences.TryGetValue(candidate.SyntaxIdentity, out var anchorOccurrence);
            anchorOccurrences[candidate.SyntaxIdentity] = anchorOccurrence + 1;
            names[index] = "CreateAkcss_" + candidate.SyntaxIdentity + "_" +
                anchorOccurrence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var definition = new CodeWriter("\n");
            new AkcssActivatorWriter(definition, _bindingEnvironment, _ownerTypeName,
                generationMode: _generationMode, forIdentity: true).WriteCandidate(plan, candidate, context);
            revisions[index] = AkcssHotReloadIdentity.Create(definition.GetText().ToString());
            _writer.Write("global::Akbura.Akcss.AkcssStyleActivator ");
            _writer.Write(names[index]);
            _writer.WriteLine("()");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
            _writer.Write("return ");
            WriteCandidate(plan, candidate, context);
            _writer.WriteLine(";");
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
        }
        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.WriteLine(".ReconcileAkcssStylesOperation(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(localId).WriteLine(",");
        _writer.Write(target).WriteLine(",");
        _writer.WriteLine("global::System.Collections.Immutable.ImmutableArray.Create<global::Akbura.Akcss.AkcssActivatorDescriptor>(");
        _writer.CurrentIndent += _writer.TabSize;
        var referenceIndex = 0;
        var revisionName = "__akcssRevision" + localId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0; index < range.Length; index++)
        {
            var candidate = plan.Candidates[plan.Activators[range.Start + index].Index];
            _writer.Write("new global::Akbura.Akcss.AkcssActivatorDescriptor(");
            _writer.WriteStringLiteral(identities[index]).Write(", ");
            _writer.WriteStringLiteral(candidate.ConflictKey).Write(", ");
            _writer.WriteStringLiteral(candidate.SyntaxIdentity).Write(", ");
            _writer.Write(knownRevision).Write(" ? ");
            _writer.WriteStringLiteral(revisions[index]);
            var applications = GetApplicationCache(plan, candidate.ApplicationCacheId).Applications;
            for (var application = 0; application < applications.Length; application++)
            {
                _writer.Write(" + ");
                WriteReferenceRevisionName(revisionName, referenceIndex++);
            }
            _writer.Write(" : null)");
            _writer.WriteLine(index + 1 == range.Length ? ")," : ",");
        }
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("__index => __index switch");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        for (var index = 0; index < names.Length; index++)
        {
            _writer.WriteIntegerLiteral(index).Write(" => ").Write(names[index]).WriteLine("(),");
        }
        _writer.WriteLine("_ => throw new global::System.ArgumentOutOfRangeException(nameof(__index)),");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("},");
        _writer.Write("operationRegistered: ").Write(knownRevision).WriteLine(");");
    }

    private void WriteActivatorArray(
        in AkcssComponentActivatorPlan plan,
        in AkcssPlanRange activators,
        in MarkupExtensionWriteContext context)
    {
        var indent = _writer.CurrentIndent;

        _writer.WriteLine(
            "global::System.Collections.Immutable.ImmutableArray.Create<" +
            "global::Akbura.Akcss.AkcssStyleActivator>(");
        _writer.CurrentIndent = indent + 4;

        for (var i = 0; i < activators.Length; i++)
        {
            WriteActivator(plan, plan.Activators[activators.Start + i], context);
            _writer.WriteLine(i + 1 == activators.Length ? string.Empty : ",");
        }

        _writer.CurrentIndent = indent;
        _writer.Write(")");
    }

    public void WriteRefresh(
        in AkcssComponentActivatorPlan plan,
        in AkcssPlanRange activators,
        string targetExpression)
    {
        if (activators.IsEmpty) return;
        EnsureRange(activators, plan.Activators.Length, nameof(activators));
        EnsureExpression(targetExpression, nameof(targetExpression));
        for (var index = 0; index < activators.Length; index++)
        {
            var activator = plan.Activators[activators.Start + index];
            if (activator.Kind != AkcssActivatorKind.UtilityCandidate) continue;
            var candidate = plan.Candidates[activator.Index];
            if (!candidate.RequiresReactiveConditionRefresh) continue;

            _writer.Write("global::Akbura.Akcss.AkcssUtilityCandidateActivator.RefreshReactiveCondition(")
                .Write(targetExpression).Write(", ").WriteIntegerLiteral(index).Write(", ");
            WriteReactiveEvaluator(candidate);
            _writer.WriteLine(");");
        }
        WriteRefresh(activators, targetExpression);
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
            .Write(_generationMode == ComponentGenerationMode.DebugStructural
                ? "global::Akbura.AkburaControl.RefreshAkcssStylesForHotReload("
                : "global::Akbura.AkburaControl.ExecuteAkcssStyles(")
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
        WriteApplicationArray(plan, cache.Applications);
        _writer.WriteLine(";");
        _writer.CurrentIndent = indent;
    }

    private void WriteApplicationArray(
        in AkcssComponentActivatorPlan plan,
        in AkcssPlanRange range)
    {
        EnsureRange(range, plan.Applications.Length, nameof(range));

        var indent = _writer.CurrentIndent;

        _writer.WriteLine(
            "global::System.Collections.Immutable.ImmutableArray.Create<" +
            "global::Akbura.Akcss.AkcssUtilityApplication>(");
        _writer.CurrentIndent = indent + 4;

        for (var i = 0; i < range.Length; i++)
        {
            WriteApplication(plan.Applications[range.Start + i]);
            _writer.WriteLine(i + 1 == range.Length ? string.Empty : ",");
        }

        _writer.CurrentIndent = indent;
        _writer.Write(")");
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

        _writer
            .Write("private static readonly global::Avalonia.AttachedProperty<object?> ")
            .Write(slot.PropertyName)
            .WriteLine(" =");
        _writer.CurrentIndent = indent + 4;
        _writer
            .Write("global::Avalonia.AvaloniaProperty.RegisterAttached<")
            .Write(_ownerTypeName)
            .Write(", global::Avalonia.Controls.Control, object?>(");
        _writer.WriteStringLiteral(slot.PropertyName).WriteLine(");");
        _writer.CurrentIndent = indent;
    }

    private void WriteFactoryMethod(
        in AkcssMarkupExtensionSlotPlan slot,
        in MarkupExtensionWriteContext context)
    {
        var indent = _writer.CurrentIndent;

        _writer.WriteHiddenApiAttributes();
        _writer.Write("private ");
        WriteFactoryReturnType(slot.FactoryValueType, slot.HasPriorityMember);
        _writer.Write(" ").Write(slot.FactoryName).Write("(");

        var parameter = slot.IsControlTarget
            ? "global::Avalonia.Controls.Control __target)"
            : "object __target)";

        if (_writer.Location.CharacterIndex + parameter.Length <= 120)
        {
            _writer.WriteLine(parameter);
        }
        else
        {
            _writer.WriteLine();
            _writer.CurrentIndent = indent + 4;
            _writer.WriteLine(parameter);
            _writer.CurrentIndent = indent;
        }

        _writer.WriteLine("{");
        _writer.CurrentIndent = indent + 4;

        var extensionContext = CreateExtensionContext(slot, context);
        var extensionWriter = new MarkupExtensionWriter(_writer, in _bindingEnvironment);

        if (slot.PriorityMember == null)
        {
            using var mapping = WriteSourceMappingStart(slot.Syntax, "return ".Length);
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

        {
            using var creationMapping = WriteSourceMappingStart(slot.Syntax, "var __extension = ".Length);
            _writer.Write("var __extension = ");
            extensionWriter.WriteCreation(slot.Extension, context);
            _writer.WriteLine(";");
        }

        {
            using var resultMapping = WriteSourceMappingStart(slot.Syntax, "return ".Length);
            _writer.Write("return new global::Akbura.Akcss.AkcssUtilityPrefixInvocation<");
            _valueWriter.WriteTypeNameWithNullableAnnotation(slot.FactoryValueType);
            _writer.WriteLine(">(");
            _writer.CurrentIndent = indent + 4;
            extensionWriter.WriteProvideValueInvocation(slot.Extension, "__extension", context);
            _writer.WriteLine(",");
            _writer.Write("__extension.");
            _valueWriter.WriteIdentifier(slot.PriorityMember!.Name);
            _writer.WriteLine(");");
        }

        _writer.CurrentIndent = indent;
    }

    private void WriteFactoryReturnType(
        ITypeSymbol resultType,
        bool hasPriorityMember)
    {
        if (hasPriorityMember)
        {
            _writer.Write("global::Akbura.Akcss.AkcssUtilityPrefixInvocation<");
            _valueWriter.WriteTypeNameWithNullableAnnotation(resultType);
            _writer.Write(">");
            return;
        }

        _valueWriter.WriteTypeNameWithNullableAnnotation(resultType);
    }

    private MarkupExtensionWriteContext CreateExtensionContext(
        in AkcssMarkupExtensionSlotPlan slot,
        in MarkupExtensionWriteContext context)
    {
        var targetProperty = MarkupTargetPropertyPlan.CreateExpression(
            GetTargetPropertyExpression(slot));
        return new MarkupExtensionWriteContext(
            targetObjectExpression: "__target",
            targetProperty: targetProperty,
            intermediateRootExpression: context.IntermediateRootExpression,
            baseUriExpression: context.BaseUriExpression,
            directParentsStack: context.DirectParentsStack,
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
                if (_generationMode.UsesStructuralRuntime())
                {
                    WriteClassActivator(plan.ClassCaches[activator.Index]);
                }
                else
                {
                    WriteClassFieldName(plan.ClassCaches[activator.Index].Id);
                }
                return;

            case AkcssActivatorKind.UtilityCandidate:
                WriteCandidate(plan, plan.Candidates[activator.Index], context);
                return;

            default:
                throw new InvalidOperationException("Unexpected AKCSS activator kind.");
        }
    }

    private void WriteClassActivator(in AkcssClassCachePlan plan)
    {
        _writer.Write("new global::Akbura.Akcss.AkcssClassActivator(");
        _writer.Write("(global::Akbura.Akcss.AkcssClass)");
        WriteStyleReference(plan.Style);
        _writer.Write(")");
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
        _writer.Write("sourceOrder: ").WriteIntegerLiteral(_forIdentity ? 0 : candidate.SourceOrder).WriteLine(",");
        _writer.Write("applications: ");

        if (_generationMode.UsesStructuralRuntime())
        {
            ref readonly var cache = ref GetApplicationCache(
                plan,
                candidate.ApplicationCacheId);
            WriteApplicationArray(plan, cache.Applications);
        }
        else
        {
            WriteApplicationFieldName(candidate.ApplicationCacheId);
        }

        if (!candidate.ValueSources.IsEmpty)
        {
            _writer.WriteLine(",");
            WriteCandidateValueSources(plan, candidate.ValueSources, context);
        }

        if (candidate.ConditionExpressionBinding != null)
        {
            _writer.WriteLine(",");
            _writer.Write("reactiveCondition: ");
            WriteReactiveEvaluator(candidate);
        }
        else if (candidate.HasCondition)
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

    private void WriteReactiveEvaluator(in AkcssUtilityCandidatePlan candidate)
    {
        var binding = candidate.ConditionExpressionBinding ??
            throw new InvalidOperationException("A reactive AKCSS evaluator requires an expression-binding plan.");
        var rewriter = new ExpressionBindingWriter.TrackingRewriter(binding,
            candidate.ConditionStateAccessors, candidate.ConditionNamedReceivers);
        using var mapping = candidate.ConditionSyntax == null ? default :
            WriteSourceMappingStart(candidate.ConditionSyntax.Expression);
        if (!candidate.RequiresReactiveConditionRefresh)
        {
            // Isolate stable callbacks from adjacent local/iteration closures.
            // The owner is an explicit input to a static factory, as for stable
            // property expression bindings; no old item display class is retained.
            var owner = rewriter.NewIdentifier("__akcssExpressionOwner");
            rewriter.SetStableScope(owner, store: null);
            _writer.Write("((global::System.Func<").Write(_ownerTypeName)
                .Write(", global::System.Func<global::Akbura.ComponentTree.ExpressionBindingContext, bool>>)(static ")
                .Write(owner).Write(" => ");
        }
        _writer.Write("(global::Akbura.ComponentTree.ExpressionBindingContext ")
            .Write(rewriter.TrackerName).Write(") => ")
            .Write(rewriter.Visit(binding.Expression.Syntax)!.NormalizeWhitespace().ToFullString());
        if (!candidate.RequiresReactiveConditionRefresh)
            _writer.Write("))(this)");
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
        WriteObjectConverter(plan.ExpectedType, useNullForgivingOperator: false, plan.ObservableElementType);
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
        _writer.Write(", ");
        WriteTargetPropertyReference(slot);
        WriteObjectConverter(plan.ExpectedType, useNullForgivingOperator: true);
        WriteRecreateOnRefresh(plan.RecreateOnRefresh);
    }

    private void WriteObjectConverter(
        ITypeSymbol expectedType,
        bool useNullForgivingOperator,
        ITypeSymbol? sourceType = null)
    {
        if (_generationMode == ComponentGenerationMode.DebugStructural &&
            (sourceType == null || SymbolEqualityComparer.Default.Equals(sourceType, expectedType)))
        {
            _writer.Write(", global::Akbura.Akcss.AkcssUtilityValueSource.");
            _writer.Write(sourceType == null ? "CastObject<" : "Identity<");
            _valueWriter.WriteTypeName(expectedType);
            _writer.Write(">");
            return;
        }
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

        if (plan.UseFactoryMethod &&
            !_generationMode.UsesStructuralRuntime())
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
        var valueOffset = plan.HasPriorityMember
            ? "__target => { var __extension = ".Length
            : "__target => ".Length;
        {
            using var mapping = WriteSourceMappingStart(slot.Syntax, valueOffset);
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
            _valueWriter.WriteTypeNameWithNullableAnnotation(slot.FactoryValueType);
            _writer.Write(">(");
            extensionWriter.WriteProvideValueInvocation(plan.Extension, "__extension", extensionContext);
            _writer.Write(", __extension.");
            _valueWriter.WriteIdentifier(plan.PriorityMember.Name);
            _writer.Write("); }");
        }
    }

    private SourceMappingToken WriteSourceMappingStart(
        AkburaSyntax syntax,
        int valueOffset = 0)
    {
        return _hasSourceMap
            ? _sourceMappingWriter.WriteStart(syntax, valueOffset)
            : default;
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

        if (_generationMode.UsesStructuralRuntime() &&
            reference.Kind == AkcssStyleReferenceKind.GeneratedModule)
        {
            _writer
                .Write(".")
                .Write(AkcssModuleWriter.DebugStyleAccessorName)
                .Write("(")
                .WriteIntegerLiteral(_forIdentity ? 0 : reference.StyleIndex)
                .Write(")");
            return;
        }

        _writer
            .Write(".Styles[")
            .WriteIntegerLiteral(reference.StyleIndex)
            .Write("]");
    }

    private void WriteClassFieldName(int id)
    {
        _writer.Write("s_akcssClass").WriteIntegerLiteral(id);
    }

    private void WriteApplicationFieldName(int id)
    {
        _writer.Write("s_akcssApplications").WriteIntegerLiteral(id);
    }

    private static AkcssMarkupExtensionSlotPlan GetSlot(in AkcssComponentActivatorPlan plan, int slotId)
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

    private static ref readonly AkcssUtilityApplicationCachePlan GetApplicationCache(
        in AkcssComponentActivatorPlan plan,
        int cacheId)
    {
        var caches = plan.ApplicationCaches;

        for (var i = 0; i < caches.Length; i++)
        {
            if (caches[i].Id == cacheId)
            {
                return ref caches.ItemRef(i);
            }
        }

        throw new InvalidOperationException("The AKCSS application cache was not found.");
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

    private string GetTargetPropertyExpression(in AkcssMarkupExtensionSlotPlan slot)
    {
        if (!_generationMode.UsesStructuralRuntime())
        {
            return slot.PropertyName;
        }

        return "global::Akbura.Akcss.AkcssUtilityValueSource.GetTargetProperty<" +
            _ownerTypeName +
            ">(" +
            slot.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ")";
    }

    private void WriteTargetPropertyReference(in AkcssMarkupExtensionSlotPlan slot)
    {
        _writer.Write(GetTargetPropertyExpression(slot));
    }
}
