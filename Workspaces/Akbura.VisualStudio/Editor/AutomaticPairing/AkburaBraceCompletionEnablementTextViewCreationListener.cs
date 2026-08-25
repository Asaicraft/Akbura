using Akbura.Workspaces;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

[Export(typeof(ITextViewCreationListener))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Document)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class
    AkburaBraceCompletionEnablementTextViewCreationListener :
    ITextViewCreationListener
{
    public void TextViewCreated(ITextView textView)
    {
        if (textView == null)
        {
            throw new ArgumentNullException(nameof(textView));
        }

        RestoreInheritedBraceCompletion(
            textView,
            trigger: "view-created");

        void OnOptionChanged(
            object? sender,
            EditorOptionChangedEventArgs eventArgs)
        {
            if (!string.Equals(
                    eventArgs.OptionId,
                    DefaultTextViewOptions
                        .BraceCompletionEnabledOptionName,
                    StringComparison.Ordinal))
            {
                return;
            }

            RestoreInheritedBraceCompletion(
                textView,
                trigger: "option-changed");
        }

        void OnClosed(
            object? sender,
            EventArgs eventArgs)
        {
            textView.Options.OptionChanged -= OnOptionChanged;
            textView.Closed -= OnClosed;
        }

        textView.Options.OptionChanged += OnOptionChanged;
        textView.Closed += OnClosed;
    }

    private static void RestoreInheritedBraceCompletion(
        ITextView textView,
        string trigger)
    {
        try
        {
            var options = textView.Options;
            var globalOptions = options.GlobalOptions;
            if (globalOptions == null)
            {
                return;
            }

            var option = DefaultTextViewOptions
                .BraceCompletionEnabledOptionId;
            var definedOnThisView = options.IsOptionDefined(
                option,
                localScopeOnly: true);
            var effectiveValue = options.GetOptionValue(option);
            var globalValue = globalOptions.GetOptionValue(option);

            var shouldClear =
                definedOnThisView &&
                !effectiveValue &&
                globalValue;
            if (shouldClear)
            {
                options.ClearOptionValue(option);
            }

            var finalValue = options.GetOptionValue(option);
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                $"Brace completion option: trigger={trigger}, " +
                $"local={definedOnThisView}, " +
                $"effective={effectiveValue}, " +
                $"global={globalValue}, " +
                $"cleared={shouldClear}, " +
                $"final={finalValue}.");
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                InvalidOperationException)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Brace completion option restoration failed: " +
                exception);
        }
    }
}