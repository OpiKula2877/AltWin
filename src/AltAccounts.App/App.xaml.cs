using System.Windows;
using AltAccounts.App.Helper;
using AltAccounts.App.Services;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Audit;
using AltAccounts.Core.Config;

namespace AltAccounts.App;

/// <summary>
/// One exe, several modes:
///   (no args)                     tray app with the Alt popup (one per signed-in session)
///   --settings                    open Settings (signals the running tray app, or runs standalone)
///   --elevated &lt;req&gt; &lt;res&gt;        elevated helper, started with the "runas" verb (UAC)
///   --init-programdata            called by the installer (already elevated)
///   --uninstall-cleanup [--delete-config]   called by the uninstaller (already elevated)
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\AltAccounts.Tray";
    public const string ShowSettingsEventName = @"Local\AltAccounts.ShowSettings";

    private Mutex? _singleInstance;
    private AppHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            AuditLog.Append("Error", ex.Exception.GetType().Name + ": " + ex.Exception.Message);
            MessageBox.Show(ex.Exception.Message, "Alt Account Switcher", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        var args = e.Args;
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        var user = ConfigStore.LoadUser();
        Theme.Apply(user.Theme);

        switch (mode)
        {
            case "--elevated" when args.Length >= 3:
                Theme.EnsureFluent();
                Shutdown(await HelperHost.RunAsync(args[1], args[2]));
                return;

#if DEBUG
            case "--render-preview" when args.Length >= 2:
                Shutdown(await Diagnostics.PreviewRenderer.RunAsync(args[1]));
                return;
#endif

            case "--init-programdata":
                // Called by the installer (elevated): creates %ProgramData%\AltAccounts with its locked-down ACL.
                if (!LocalAccountService.IsCurrentProcessElevated()) { Shutdown(5); return; }
                ProgramDataSetup.EnsureSecured();
                Shutdown(0);
                return;

            case "--uninstall-cleanup":
                Shutdown(UninstallCleanup.Run(deleteConfig: args.Contains("--delete-config", StringComparer.OrdinalIgnoreCase)));
                return;

            case "--settings":
                if (SignalRunningInstance()) { Shutdown(0); return; }
                var standalone = new Settings.SettingsWindow(null);
                standalone.Closed += (_, _) => Shutdown(0);
                standalone.Show();
                return;
        }

        _singleInstance = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            SignalRunningInstance();
            Shutdown(0);
            return;
        }

        var shared = ConfigStore.LoadShared();
        if (shared.PolicyFor(LocalAccountService.CurrentUserSid).DisableAppHere)
        {
            // An admin turned the app off for this account. Settings stay reachable through --settings.
            Shutdown(0);
            return;
        }

        _host = new AppHost();
        _host.Start();
    }

    private static bool SignalRunningInstance()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ShowSettingsEventName);
            ev.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
