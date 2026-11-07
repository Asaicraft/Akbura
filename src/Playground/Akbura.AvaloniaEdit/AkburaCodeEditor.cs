namespace Akbura.AvaloniaEdit;

/// <summary>
/// An AvaloniaEdit buffer bound directly to a host-owned Akbura workspace.
/// </summary>
public class AkburaCodeEditor : global::AvaloniaEdit.TextEditor
{
    // Reuse TextEditor's control theme, including all normal editing commands.
    protected override Type StyleKeyOverride => typeof(global::AvaloniaEdit.TextEditor);

    public AkburaEditorSession? Session
    {
        get; private set;
    }

    public AkburaEditorSession Bind(AkburaWorkspace workspace, AkburaProjectId projectId,
        Uri documentUri, AkburaEditorOptions? options = null)
    {
        Avalonia.Threading.Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(documentUri);

        if (!documentUri.IsAbsoluteUri)
        {
            throw new ArgumentException("An absolute document URI is required.", nameof(documentUri));
        }

        var extension = Path.GetExtension(documentUri.AbsolutePath);

        if (!extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only .akbura and .akcss documents are supported.", nameof(documentUri));
        }

        workspace.CurrentSolution.GetRequiredProject(projectId);
        if (workspace.CurrentSolution.TryGetDocument(documentUri, out var existing) && existing.ProjectId != projectId)
        {
            throw new ArgumentException("The document belongs to another project.", nameof(projectId));
        }

        options ??= new AkburaEditorOptions();
        ArgumentNullException.ThrowIfNull(options.Scheduler);
        if (options.AnalysisDelay < TimeSpan.Zero || options.HoverDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        Session?.Dispose();
        Session = new AkburaEditorSession(this, workspace, projectId, documentUri, options);
        return Session;
    }

    public Task RequestCompletionAsync(CancellationToken cancellationToken = default) =>
        Session?.RequestCompletionAsync(cancellationToken) ?? Task.CompletedTask;

    internal void Release(AkburaEditorSession session)
    {
        if (ReferenceEquals(Session, session))
        {
            Session = null;
        }
    }
}
