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
}