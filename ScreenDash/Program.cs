namespace ScreenDash;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--smoke-test")
            return SmokeTest.RunAsync(Path.GetFullPath(args[1])).GetAwaiter().GetResult();
        if (args.Length == 1 && args[0] == "--create-desktop-shortcut")
        {
            try { WindowsIntegration.EnsureDesktopShortcut(Application.ExecutablePath); return 0; }
            catch (Exception ex) { AppLog.Write(ex); return 1; }
        }
        try { WindowsIntegration.SetAppIdentity(); }
        catch (Exception ex) { AppLog.Write(ex); }
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, "Local\\ScreenDash_" + Environment.UserName, out bool first);
        if (!first)
        {
            if (!WindowsIntegration.TryRestoreExistingWindow())
                MessageBox.Show("ScreenDash läuft bereits. Öffne die App über die Taskleiste oder das Symbol neben der Windows-Uhr.", "ScreenDash", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write(e.ExceptionObject.ToString() ?? "Unbekannter Fehler");
        TaskScheduler.UnobservedTaskException += (_, e) => { AppLog.Write(e.Exception); e.SetObserved(); };
        try
        {
            using var controller = new AppController();
            using var form = new MainForm(controller, args.Contains("--tray"));
            try { WindowsIntegration.EnsureDesktopShortcut(Application.ExecutablePath); }
            catch (Exception ex) { AppLog.Write("Desktop-Verknüpfung konnte nicht erstellt werden: " + ex.Message); }
            form.Shown += async (_, _) =>
            {
                if (controller.Settings.RecordOnLaunch)
                {
                    try { await controller.ToggleRecordingAsync(); }
                    catch (Exception ex) { Report(ex); }
                }
            };
            Application.Run(form);
            return 0;
        }
        catch (Exception ex) { Report(ex); return 1; }
    }

    private static void Report(Exception ex)
    {
        AppLog.Write(ex);
        MessageBox.Show(ex.Message + "\n\nDetails stehen im lokalen Protokoll.", "ScreenDash", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
