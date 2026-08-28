namespace Akbura.Diagnostics;

internal static class DiagnosticValueCommit
{
    public static string TryCommit(
        Action<object> commitValue,
        object? value)
    {
        ArgumentNullException.ThrowIfNull(commitValue);

        try
        {
            commitValue(value!);
            return string.Empty;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }
}
