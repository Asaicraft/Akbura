namespace Akbura.LanguageServer.Hosting;

internal enum AkburaServerLogLevel
{
    Trace,
    Information,
    Warning,
    Error,
    None,
}

internal interface IAkburaServerLogger : IDisposable
{
    void Log(
        AkburaServerLogLevel level,
        string message,
        Exception? exception = null);
}

internal sealed class TextWriterAkburaServerLogger : IAkburaServerLogger
{
    private readonly TextWriter _writer;
    private readonly TextWriter? _fileWriter;
    private readonly AkburaServerLogLevel _minimumLevel;
    private readonly object _gate = new();
    private int _disposeState;

    public TextWriterAkburaServerLogger(
        TextWriter writer,
        AkburaServerLogLevel minimumLevel,
        string? logFile)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _minimumLevel = minimumLevel;
        if (!string.IsNullOrWhiteSpace(logFile))
        {
            var fullPath = Path.GetFullPath(logFile);
            Directory.CreateDirectory(
                Path.GetDirectoryName(fullPath) ??
                Environment.CurrentDirectory);
            _fileWriter = TextWriter.Synchronized(
                new StreamWriter(
                    new FileStream(
                        fullPath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read))
                {
                    AutoFlush = true,
                });
        }
    }

    public void Log(
        AkburaServerLogLevel level,
        string message,
        Exception? exception = null)
    {
        if (level < _minimumLevel ||
            _minimumLevel == AkburaServerLogLevel.None ||
            Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {message}";
        if (exception != null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_gate)
        {
            _writer.WriteLine(line);
            _writer.Flush();
            _fileWriter?.WriteLine(line);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _fileWriter?.Dispose();
    }
}