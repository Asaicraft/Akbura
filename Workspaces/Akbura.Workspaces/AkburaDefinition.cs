using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Workspaces;

/// <summary>
/// Represents a navigable source reference and its definition target.
/// </summary>
public sealed class AkburaDefinition
{
    public AkburaDefinition(
        TextSpan sourceSpan,
        string targetFilePath,
        LinePositionSpan targetLineSpan)
        : this(
            sourceSpan,
            targetFilePath,
            targetLineSpan,
            targetText: null)
    {
    }

    internal AkburaDefinition(
        TextSpan sourceSpan,
        string targetFilePath,
        LinePositionSpan targetLineSpan,
        SourceText? targetText,
        string? targetAssemblyName = null,
        string? targetSourcePath = null)
    {
        if (string.IsNullOrWhiteSpace(
                targetFilePath))
        {
            throw new ArgumentException(
                "The target file path cannot be empty.",
                nameof(targetFilePath));
        }

        SourceSpan = sourceSpan;
        TargetFilePath =
            Path.GetFullPath(
                targetFilePath);

        TargetLineSpan = targetLineSpan;
        TargetText = targetText;
        TargetAssemblyName = targetAssemblyName;
        TargetSourcePath = targetSourcePath;
    }

    /// <summary>
    /// Gets the reference span inside the current Akbura document.
    /// </summary>
    public TextSpan SourceSpan { get; }

    /// <summary>
    /// Gets the target source file.
    /// </summary>
    public string TargetFilePath { get; }

    /// <summary>
    /// Gets the target line and character range.
    /// </summary>
    public LinePositionSpan TargetLineSpan { get; }

    /// <summary>
    /// Gets source text that must be materialized before navigation, or
    /// <see langword="null"/> when <see cref="TargetFilePath"/> already
    /// identifies a physical source file.
    /// </summary>
    public SourceText? TargetText { get; }

    /// <summary>
    /// Gets the assembly that embedded the target source, or
    /// <see langword="null"/> for an ordinary physical source file.
    /// </summary>
    public string? TargetAssemblyName { get; }

    /// <summary>
    /// Gets the source path stored in the Akbura module manifest, or
    /// <see langword="null"/> when the target does not come from an
    /// embedded Akbura source.
    /// </summary>
    public string? TargetSourcePath { get; }
}
