namespace GameHelper.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>Compatibility for the renamed wisp plugin; no user files are removed.</summary>
    internal static class PluginRenames
    {
        internal const string CurrentWispName = "ShowMeWisp";
        internal const string LegacyWispName = "WhereTheWispsAt";

        internal static bool IsSuperseded(DirectoryInfo directory) =>
            directory.Name.Equals(LegacyWispName, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Join(directory.Parent!.FullName, CurrentWispName, CurrentWispName + ".dll"));

        internal static PluginMetadata InitialMetadata(string name, IReadOnlyDictionary<string, PluginMetadata> saved) =>
            name.Equals(CurrentWispName, StringComparison.OrdinalIgnoreCase) && saved.TryGetValue(LegacyWispName, out var legacy)
                ? new PluginMetadata { Enable = legacy.Enable }
                : new PluginMetadata();
    }
}
