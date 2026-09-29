namespace Bloodybot2.Game;

using System.Diagnostics;
using Microsoft.Win32;

internal static class ChromeBrowser
{
    public static void Open(string url)
    {
        var candidates = new List<string?>
        {
            Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string,
        };
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
            candidates.Add(Path.Combine(Environment.GetFolderPath(folder), "Google", "Chrome", "Application", "chrome.exe"));
        var path = candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            ?? throw new FileNotFoundException("未找到 Chrome，请在 Chrome 中手动打开配置网址。");
        var start = new ProcessStartInfo(path) { UseShellExecute = true };
        start.ArgumentList.Add(url);
        Process.Start(start);
    }
}
