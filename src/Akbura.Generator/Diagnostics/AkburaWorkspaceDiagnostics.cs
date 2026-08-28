using System.Diagnostics;
using System.Threading;

namespace Akbura;

/// <summary>
/// Central debug-only trace sink for compiler, workspace, and editor diagnostics.
/// </summary>
internal static class AkburaWorkspaceDiagnostics
{
    internal enum Category
    {
        Workspace,
        Completion,
        CompletionPerformance,
        Navigation,
        CSharp,
        QuickInfo,
        Classification,
        Diagnostics,
        AutoClosingTag,
        Syntax,
        ObjectPool,
        SyntaxCache,
    }

    [Conditional("DEBUG")]
    internal static void Write(
        Category category,
        string message)
    {
#if DEBUG
        Debug.WriteLine(FormatLine(category, message));
#endif
    }

    [Conditional("DEBUG")]
    internal static void Write(
        Category category,
        string message,
        Exception exception)
    {
#if DEBUG
        Write(
            category,
            message + "\n" + exception);
#endif
    }

    [Conditional("DEBUG")]
    internal static void WriteElapsed(
        Category category,
        string operation,
        TimeSpan elapsed)
    {
#if DEBUG
        Write(
            category,
            $"{operation}: {elapsed.TotalMilliseconds:F2} ms");
#endif
    }

    [Conditional("DEBUG")]
    internal static void WriteCompletionElapsed(
        string operation,
        TimeSpan elapsed)
    {
#if DEBUG
        WriteElapsed(
            Category.CompletionPerformance,
            operation,
            elapsed);
#endif
    }

    [Conditional("DEBUG")]
    internal static void WriteAkcssCompilationReferences(
        IEnumerable<string> componentFiles,
        IEnumerable<string> globalImports,
        IEnumerable<string> akcssModules,
        IEnumerable<string> referencedProjects)
    {
#if DEBUG
        Write(
            Category.Navigation,
            $"Current component files: " +
            $"[{string.Join(", ", componentFiles)}]; " +
            $"global AKCSS imports: " +
            $"[{string.Join(", ", globalImports)}].");
        Write(
            Category.Navigation,
            $"Current AKCSS modules: " +
            $"[{string.Join(", ", akcssModules)}]; " +
            $"referenced projects: " +
            $"[{string.Join("; ", referencedProjects)}].");
#endif
    }

#if DEBUG
    private static string FormatLine(
        Category category,
        string message)
    {
        return $"{DateTimeOffset.Now:O} " +
            $"[{GetCategoryName(category)}] " +
            $"[thread {Thread.CurrentThread.ManagedThreadId}] " +
            message;
    }

    private static string GetCategoryName(Category category)
    {
        return category switch
        {
            Category.Workspace => "Akbura.Workspace",
            Category.Completion => "Akbura.Completion",
            Category.CompletionPerformance =>
                "Akbura.Completion.Performance",
            Category.Navigation => "Akbura.Navigation",
            Category.CSharp => "Akbura.CSharp",
            Category.QuickInfo => "Akbura.QuickInfo",
            Category.Classification => "Akbura.Classification",
            Category.Diagnostics => "Akbura.Diagnostics",
            Category.AutoClosingTag => "Akbura.AutoClose",
            Category.Syntax => "Akbura.Syntax",
            Category.ObjectPool => "Akbura.ObjectPool",
            Category.SyntaxCache => "Akbura.Syntax.Cache",
            _ => "Akbura.Workspace",
        };
    }

#endif
}
