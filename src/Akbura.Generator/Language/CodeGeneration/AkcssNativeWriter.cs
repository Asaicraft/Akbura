using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

internal sealed class AkcssNativeWriter
{
    internal const string CreateStylesMethod = "__CreateNativeStyles";
    internal const string AppendStylesMethod = "__AppendNativeStyles";

    private const string Selectors = "global::Avalonia.Styling.Selectors.";
    private readonly CodeWriter _writer;
    private readonly AkcssGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;
    private readonly AkcssNativeNames _names;

    public AkcssNativeWriter(CodeWriter writer, AkcssGenerationSourceMap sourceMap, AkcssNativeModulePlan plan, string ownerTypeName)
    {
        _writer = writer;
        _sourceMap = sourceMap;
        _ownerTypeName = ownerTypeName;
        _names = new AkcssNativeNames(plan.ReservedNames);
    }

    public void WriteModule(AkcssNativeModulePlan plan)
    {
        _writer.WriteLine();
        _writer.WriteLine("internal static global::Avalonia.Styling.Styles " + CreateStylesMethod + "()");
        OpenBlock();
        var styles = _names.Allocate("__akcssStyles");
        _writer.Write("var ").Write(styles).WriteLine(" = new global::Avalonia.Styling.Styles();");
        _writer.Write(AppendStylesMethod).Write("(").Write(styles).WriteLine(");");
        _writer.Write("return ").Write(styles).WriteLine(";");
        CloseBlock();

        _writer.Write("internal static void ").Write(AppendStylesMethod).Write("(global::Avalonia.Styling.Styles ").Write(styles).WriteLine(")");
        OpenBlock();
        foreach (var style in plan.Styles)
        {
            WriteStyle(style, styles, isRoot: true);
        }
        CloseBlock();

        foreach (var planTheme in plan.Themes)
        {
            WriteTheme(planTheme);
        }

        _writer.WriteLine();
        _writer.WriteLine("private static void __SetNative(global::Avalonia.Styling.StyleBase owner, global::Avalonia.AvaloniaProperty property, object? value)");
        OpenBlock();
        _writer.WriteLine("var setters = owner.Setters;");
        _writer.WriteLine("for (var index = 0; index < setters.Count; index++)");
        OpenBlock();
        _writer.WriteLine("if (setters[index] is global::Avalonia.Styling.Setter setter && global::System.Object.Equals(setter.Property, property))");
        OpenBlock();
        _writer.WriteLine("setters.RemoveAt(index);");
        _writer.WriteLine("break;");
        CloseBlock();
        CloseBlock();
        _writer.WriteLine("setters.Add(new global::Avalonia.Styling.Setter(property, value));");
        CloseBlock();
    }

    public void WriteFacade(AkcssNativeModulePlan plan, string moduleType, AkburaSemanticModel model)
    {
        if (plan.Export is not { IsValid: true } facade)
        {
            return;
        }

        _writer.WriteLine();
        if (facade.Namespace.Length != 0)
        {
            _writer.Write("namespace ").WriteLine(facade.Namespace);
            OpenBlock();
        }

        _writer.Write("public static class ").WriteLine(facade.TypeName);
        OpenBlock();
        foreach (var export in facade.Themes)
        {
            if (export.Resolve(model) is { } theme && plan.Themes.Any(candidate => ReferenceEquals(candidate.Theme, theme)))
            {
                _writer.Write("public static global::Avalonia.Styling.ControlTheme ").Write(export.MemberName)
                    .Write(" => ").Write(moduleType).Write(".").Write(AkcssNativePlanner.GetThemeAccessor(theme)).WriteLine(";");
            }
        }

        _writer.Write("public static global::Avalonia.Styling.Styles CreateStyles() => ").Write(moduleType)
            .Write(".").Write(CreateStylesMethod).WriteLine("();");
        CloseBlock();
        if (facade.Namespace.Length != 0)
        {
            CloseBlock();
        }
    }

    private void WriteTheme(AkcssNativeThemePlan theme)
    {
        var name = AkcssNativePlanner.GetThemeAccessor(theme.Theme);
        var instance = _names.Allocate("__akcssTheme");
        _writer.WriteLine();
        _writer.Write("private static readonly global::System.Lazy<global::Avalonia.Styling.ControlTheme> ")
            .Write(name).Write("Value = new(").Write(name).WriteLine("Build);");
        _writer.Write("internal static global::Avalonia.Styling.ControlTheme ").Write(name).Write(" => ")
            .Write(name).WriteLine("Value.Value;");
        _writer.Write("private static global::Avalonia.Styling.ControlTheme ").Write(name).WriteLine("Build()");
        OpenBlock();
        _writer.Write("var ").Write(instance).Write(" = new global::Avalonia.Styling.ControlTheme { TargetType = typeof(")
            .Write(Type(theme.Target)).WriteLine(") };");
        WriteMembers(theme.Members, instance, templateContext: null);
        _writer.Write("return ").Write(instance).WriteLine(";");
        CloseBlock();
    }

    private void WriteStyle(AkcssNativeStylePlan plan, string owner, bool isRoot)
    {
        var style = _names.Allocate("__akcssStyle");
        var selector = _names.Allocate("__akcssSelector");
        using (new AkcssSourceMappingWriter(_writer, _sourceMap).WriteStart(plan.Syntax))
        {
            _writer.Write("var ").Write(style).WriteLine(" = new global::Avalonia.Styling.Style();");
            _writer.Write("global::Avalonia.Styling.Selector? ").Write(selector).WriteLine(" = null;");
            WriteCompound(plan.Selector.Initial, selector);
            foreach (var step in plan.Selector.Steps)
            {
                var method = step.Relationship switch
                {
                    AkcssSelectorRelationship.Child => "Child",
                    AkcssSelectorRelationship.Descendant => "Descendant",
                    AkcssSelectorRelationship.Template => "Template",
                    _ => throw new InvalidOperationException("Unknown bound selector relationship."),
                };
                WriteSelectorStep(selector, method);
                WriteCompound(step.Target, selector);
            }
            _writer.Write(style).Write(".Selector = ").Write(selector).WriteLine(";");
        }
        WriteMembers(plan.Members, style, templateContext: null);
        _writer.Write(owner).Write(isRoot ? ".Add(" : ".Children.Add(").Write(style).WriteLine(");");
    }

    private void WriteCompound(AkcssSelectorCompound compound, string selector)
    {
        if (compound.Anchor != null)
        {
            WriteSelectorStep(selector, "Nesting");
        }

        foreach (var predicate in compound.Predicates)
        {
            switch (predicate.Kind)
            {
                case AkcssPredicateKind.ExactType:
                    _writer.Write(selector).Write(" = ").Write(Selectors).Write("OfType(").Write(selector)
                        .Write(", typeof(").Write(Type(predicate.Type.Symbol!)).WriteLine("));");
                    break;
                case AkcssPredicateKind.Class:
                case AkcssPredicateKind.PseudoClass:
                case AkcssPredicateKind.Name:
                    _writer.Write(selector).Write(" = ").Write(Selectors)
                        .Write(predicate.Kind == AkcssPredicateKind.Name ? "Name(" : "Class(")
                        .Write(selector).Write(", ");
                    _writer.WriteStringLiteral(predicate.Kind == AkcssPredicateKind.PseudoClass ? ":" + predicate.Name : predicate.Name!);
                    _writer.WriteLine(");");
                    break;
            }
        }
    }

    private void WriteSelectorStep(string selector, string method) =>
        _writer.Write(selector).Write(" = ").Write(Selectors).Write(method).Write("(").Write(selector).WriteLine(");");

    private void WriteMembers(ImmutableArray<AkcssNativeMemberPlan> members, string owner, TemplateContext? templateContext)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case AkcssNativeStylePlan style:
                    WriteStyle(style, owner, isRoot: false);
                    break;
                case AkcssNativeSetterPlan setter:
                    using (new AkcssSourceMappingWriter(_writer, _sourceMap).WriteStart(setter.Syntax))
                    {
                        WriteSetter(setter, owner, templateContext);
                    }
                    break;
                case AkcssNativeIncludePlan include:
                    OpenBlock();
                    foreach (var argument in include.Arguments)
                    {
                        using var mapping = new AkcssSourceMappingWriter(_writer, _sourceMap).WriteStart(argument.Syntax);
                        _writer.Write(Type(argument.Type)).Write(" ").Write(argument.Name).Write(" = ").Write(argument.Expression).WriteLine(";");
                    }
                    WriteMembers(include.Members, owner, templateContext);
                    CloseBlock();
                    break;
                case AkcssNativeBaseThemePlan basedOn:
                    using (new AkcssSourceMappingWriter(_writer, _sourceMap).WriteStart(basedOn.Syntax))
                    {
                        _writer.Write(owner).Write(".BasedOn = ").Write(basedOn.Reference).WriteLine(";");
                    }
                    break;
                case AkcssNativeControlTemplatePlan template:
                    WriteControlTemplate(template, owner);
                    break;
                case AkcssNativeTemplateBindingPlan binding:
                    using (new AkcssSourceMappingWriter(_writer, _sourceMap).WriteStart(binding.Syntax))
                    {
                        _writer.Write(owner).Write(".Bind(").Write(Member(binding.Destination))
                            .Write(", new global::Avalonia.Data.TemplateBinding(").Write(Member(binding.Source))
                            .WriteLine(") { Mode = global::Avalonia.Data.BindingMode.OneWay });");
                    }
                    break;
            }
        }
    }

    private void WriteSetter(AkcssNativeSetterPlan setter, string owner, TemplateContext? template)
    {
        var member = Member(setter.Property);
        if (setter.IsDynamicResource)
        {
            var value = "new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(" + setter.Expression + ")";
            if (template is null)
            {
                _writer.Write("__SetNative(").Write(owner).Write(", ").Write(member).Write(", ").Write(value).WriteLine(");");
            }
            else
            {
                _writer.Write(owner).Write(".Bind(").Write(member).Write(", ").Write(value)
                    .Write(".ProvideValue(global::Akbura.Markup.AkburaControlTemplateServices.Create(")
                    .Write(owner).Write(", ").Write(member).Write(", ").Write(template.Root).Write(", ")
                    .Write(template.BaseUri).Write(", global::System.Array.Empty<object>(), templatedParent: ")
                    .Write(template.Parent).Write(", nameScope: ").Write(template.NameScope).WriteLine(")));");
            }
            return;
        }

        var local = _names.Allocate("__akcssValue");
        _writer.Write(Type(setter.ValueType)).Write(" ").Write(local).Write(" = ").Write(setter.Expression).WriteLine(";");
        if (template is null)
        {
            _writer.Write("__SetNative(").Write(owner).Write(", ").Write(member).Write(", ").Write(local).WriteLine(");");
        }
        else if (setter.Property is Microsoft.CodeAnalysis.IPropertySymbol property)
        {
            _writer.Write("((").Write(Type(property.ContainingType)).Write(")").Write(owner).Write(").")
                .Write(AkcssExpressionGenerator.EscapeIdentifier(property.Name)).Write(" = ").Write(local).WriteLine(";");
        }
        else
        {
            _writer.Write(owner).Write(".SetValue(").Write(member).Write(", ").Write(local);
            if (setter.PropertyKind is AkcssSetterPropertyKind.Styled or AkcssSetterPropertyKind.Attached)
            {
                _writer.Write(", global::Avalonia.Data.BindingPriority.Template");
            }
            _writer.WriteLine(");");
        }
    }

    private void WriteControlTemplate(AkcssNativeControlTemplatePlan template, string owner)
    {
        var semanticModel = template.Template.Theme.DefinitionModel;
        var markup = ComponentPlanner.CreateControlTemplate(template.Template);
        try
        {
            if (markup.RootElementIds.Length != 1)
            {
                WriteDiagnostic(_writer, _sourceMap, AkcssNativePlanner.Error(template.Syntax,
                    "The control template root cannot be constructed."));
                return;
            }

            var parent = _names.Allocate("__templatedParent");
            var nameScope = _names.Allocate("__templateNameScope");
            var baseUri = _names.Allocate("__templateBaseUri");
            var root = markup.Elements.ItemRef(markup.RootElementIds[0]).Identifier;
            var context = new TemplateContext(root, parent, nameScope, baseUri);
            var sourceMap = new ComponentGenerationSourceMap(semanticModel.SyntaxTree);
            var environment = BindingWriterEnvironment.Create(semanticModel.Compilation.CSharpCompilation, withinType: null);

            _writer.Write("__SetNative(").Write(owner)
                .Write(", global::Avalonia.Controls.Primitives.TemplatedControl.TemplateProperty, new global::Avalonia.Controls.Templates.FuncControlTemplate<")
                .Write(Type(template.Template.TemplatedParentType.Symbol!)).Write(">(static (").Write(parent)
                .Write(", ").Write(nameScope).WriteLine(") =>");
            OpenBlock();
            _writer.Write("var ").Write(baseUri).Write(" = new global::System.Uri(");
            var path = semanticModel.SyntaxTree.FilePath.Replace('\\', '/');
            _writer.WriteStringLiteral(path);
            _writer.WriteLine(", global::System.UriKind.RelativeOrAbsolute);");

            var scopeWriter = new ComponentScopeWriter(_writer, in environment, sourceMap,
                _ownerTypeName, ComponentGenerationMode.ReleaseDirect, writeElementConfiguration: element =>
                {
                    if (template.Configurations.TryGetValue(element.Syntax, out var configurations))
                    {
                        WriteMembers(configurations, element.Identifier, context);
                    }
                });
            var writeContext = new ComponentScopeWriteContext(root, baseUri, null, nameScope, 0,
                MarkupParentStackTraversalKind.ControlTemplateHierarchy, markup.Elements.AsSpan(), markup.ElementReferences.AsSpan(),
                templatedParentExpression: parent, usesTemplateServices: true);
            scopeWriter.WriteLocalInitialState(markup, markup.Scopes.ItemRef(0), writeContext);
            _writer.Write(nameScope).WriteLine(".Complete();");
            _writer.Write("return ").Write(root).WriteLine(";");
            CloseBlock("));");
        }
        finally
        {
            markup.ReturnToPool();
        }
    }

    internal static void WriteDiagnostic(CodeWriter writer, AkcssGenerationSourceMap sourceMap, AkburaSemanticDiagnostic diagnostic)
    {
        const string prefix = "#error ";
        using var mapping = sourceMap.TryGetLineDirective(diagnostic.Syntax, out var span, out var path)
            ? SourceMappingWriter.WriteStartCore(writer, span, path, prefix.Length)
            : default;
        writer.Write(prefix).Write(diagnostic.Code).Write(" ")
            .Write(diagnostic.Message.Replace('\r', ' ').Replace('\n', ' '));
        foreach (var related in diagnostic.RelatedSyntax)
        {
            if (sourceMap.TryGetLineDirective(related, out var location, out var sourcePath))
            {
                writer.Write(" (related: ").Write(sourcePath).Write(":").WriteIntegerLiteral(location.Start.Line + 1)
                    .Write(":").WriteIntegerLiteral(location.Start.Character + 1).Write(")");
            }
        }
        writer.WriteLine();
    }

    private static string Type(RoslynSymbol symbol) => AkcssExpressionGenerator.GetMetadataTypeName(symbol);

    private static string Member(RoslynSymbol property) =>
        AkcssExpressionGenerator.GetStaticMemberReference(property)!;

    private void OpenBlock()
    {
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
    }

    private void CloseBlock(string suffix = "")
    {
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.Write("}").WriteLine(suffix);
    }

    private sealed record TemplateContext(string Root, string Parent, string NameScope, string BaseUri);
}
