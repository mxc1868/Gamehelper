namespace ShowMeWisp
{
    using System.IO;

    internal static class WispSettingsFile
    {
        internal static string FindForRead(string pluginDirectory)
        {
            var current = Path.Join(pluginDirectory, "config", "settings.txt");
            if (File.Exists(current)) return current;
            var legacy = Path.Join(Directory.GetParent(Path.GetFullPath(pluginDirectory))!.FullName,
                "WhereTheWispsAt", "config", "settings.txt");
            return File.Exists(legacy) ? legacy : current;
        }
    }
}
