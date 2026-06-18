using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Clipton.WinUI;

/// <summary>
/// Enables or disables launch-at-sign-in for packaged and unpackaged WinUI builds.
/// </summary>
/// <remarks>
/// Packaged builds must use the Windows StartupTask API, while unpackaged builds fall
/// back to the current-user Run registry key.
/// </remarks>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Clipton";
    private const string StartupTaskId = "CliptonStartup";

    /// <summary>
    /// Requests startup registration and returns the effective OS-level result.
    /// </summary>
    public static async Task<StartupRegistrationResult> SetEnabledAsync(bool enabled)
    {
        if (IsPackaged())
        {
            return await SetPackagedStartupAsync(enabled);
        }

        return SetRegistryStartup(enabled)
            ? enabled ? StartupRegistrationResult.Enabled : StartupRegistrationResult.Disabled
            : StartupRegistrationResult.Unsupported;
    }

    private static async Task<StartupRegistrationResult> SetPackagedStartupAsync(bool enabled)
    {
        try
        {
            var startupTask = await StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                var state = await startupTask.RequestEnableAsync();
                return state switch
                {
                    StartupTaskState.Enabled => StartupRegistrationResult.Enabled,
                    StartupTaskState.EnabledByPolicy => StartupRegistrationResult.EnabledByPolicy,
                    StartupTaskState.DisabledByPolicy => StartupRegistrationResult.DisabledByPolicy,
                    StartupTaskState.DisabledByUser => StartupRegistrationResult.DisabledByUser,
                    _ => StartupRegistrationResult.Disabled
                };
            }

            startupTask.Disable();
            return StartupRegistrationResult.Disabled;
        }
        catch (Exception)
        {
            return StartupRegistrationResult.Unsupported;
        }
    }

    private static bool SetRegistryStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                var processPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
                {
                    return false;
                }

                key.SetValue(ValueName, $"\"{processPath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Package.Current.Id.FullName;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>
/// Result of attempting to change Windows startup registration.
/// </summary>
public enum StartupRegistrationResult
{
    /// <summary>Startup is enabled.</summary>
    Enabled,

    /// <summary>Startup is enabled by policy and cannot be disabled by the app.</summary>
    EnabledByPolicy,

    /// <summary>Startup is disabled.</summary>
    Disabled,

    /// <summary>Startup is disabled by the user outside the app.</summary>
    DisabledByUser,

    /// <summary>Startup is disabled by policy.</summary>
    DisabledByPolicy,

    /// <summary>The current install/runtime mode does not support the requested operation.</summary>
    Unsupported
}
