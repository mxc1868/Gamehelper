namespace Launcher
{
    using System;
    using System.Windows.Forms;

    public static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ApplicationConfiguration.Initialize();

            LauncherLog.Write($"Start (PID {Environment.ProcessId})");

            if (!GameHelperFinder.TryFindGameHelperExe(out var installDir, out var appExePath))
            {
                LauncherDialogs.ShowError(
                    LauncherLocalization.L(
                        $"GameHelper.App.exe was not found in:{Environment.NewLine}{installDir}",
                        $"GameHelper.App.exe wurde nicht gefunden in:{Environment.NewLine}{installDir}"));
                return;
            }

            LauncherLog.Write($"InstallDir={installDir}");
            LegacyPluginCleanup.Apply(installDir);

            // This fork is maintained through source builds. Upstream release binaries
            // lack the core APIs required by ShowMeWisp and UniqueLoot, so do not
            // offer an in-place binary update that would replace this patched core.
            LauncherLog.Write("Automatic updates disabled for mxc1868/Gamehelper; update and rebuild the fork source.");

            if (GameStarter.TryStart(installDir, appExePath))
            {
                Application.Exit();
            }
        }
    }
}
