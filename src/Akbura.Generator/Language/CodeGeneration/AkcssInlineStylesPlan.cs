using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal readonly record struct AkcssInlineStylesPlan(ImmutableArray<string> ModuleTypes)
{
    public bool IsEmpty => ModuleTypes.IsDefaultOrEmpty;

    public static AkcssInlineStylesPlan Create(IAkburaComponentSymbol component,
        IReadOnlyDictionary<AkburaSyntax, string> moduleNames)
    {
        var modules = ImmutableArray.CreateBuilder<string>();
        foreach (var module in component.AkcssModules)
        {
            if (module.IsInlined && module.DeclaringSyntax is { } syntax &&
                moduleNames.TryGetValue(syntax, out var name) &&
                module.Declarations.Any(static declaration => declaration is AkcssSelectorRuleSymbol))
            {
                modules.Add(name);
            }
        }

        return new AkcssInlineStylesPlan(modules.ToImmutable());
    }

    public void WriteField(CodeWriter writer) =>
        writer.WriteLine("private global::Avalonia.Styling.Styles? __akburaNativeStyles;");

    public void WriteInstallation(CodeWriter writer)
    {
        if (IsEmpty)
        {
            return;
        }

        writer.WriteLine("if (__akburaNativeStyles is null)");
        writer.WriteLine("{");
        writer.CurrentIndent += writer.TabSize;
        writer.WriteLine("var __nativeStyles = new global::Avalonia.Styling.Styles();");
        foreach (var module in ModuleTypes)
        {
            writer.Write(module).Write(".").Write(AkcssNativeWriter.AppendStylesMethod).WriteLine("(__nativeStyles);");
        }
        writer.WriteLine("this.Styles.Add(__nativeStyles);");
        writer.WriteLine("__akburaNativeStyles = __nativeStyles;");
        writer.CurrentIndent -= writer.TabSize;
        writer.WriteLine("}");
    }
}
