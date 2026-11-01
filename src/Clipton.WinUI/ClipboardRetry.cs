using System.Runtime.InteropServices;

namespace Clipton.WinUI;

internal static class ClipboardRetry
{
    private const int MaxAttempts = 20;
    private const int RetryDelayMilliseconds = 75;

    internal static T? Execute<T>(
        Func<T?> action,
        Action<int>? retryDelay = null,
        Action<Exception>? onExhausted = null)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                if (attempt == MaxAttempts - 1)
                {
                    onExhausted?.Invoke(exception);
                    return default;
                }

                retryDelay?.Invoke(RetryDelayMilliseconds);
            }
        }

        return default;
    }

    private static bool IsTransient(Exception exception) =>
        exception is ExternalException or COMException or UnauthorizedAccessException;
}
