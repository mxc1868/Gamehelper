namespace GameHelper.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>Compatibility for renamed/integrated plugins; no user settings are removed.</summary>
    internal static class PluginRenames
    {
        internal const string CurrentWispName = "ShowMeWisp";
        internal const string LegacyWispName = "WhereTheWispsAt";
        internal const string CurrentBotName = "Bloodybot2";
        internal const string LegacyFollowerName = "Follower";

        internal static bool IsSuperseded(DirectoryInfo directory) =>
            directory.Name.Equals(LegacyWispName, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Join(directory.Parent!.FullName, CurrentWispName, CurrentWispName + ".dll")) ||
            directory.Name.Equals(LegacyFollowerName, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Join(directory.Parent!.FullName, CurrentBotName, CurrentBotName + ".dll"));

        internal static PluginMetadata InitialMetadata(string name, IReadOnlyDictionary<string, PluginMetadata> saved) =>
            name.Equals(CurrentWispName, StringComparison.OrdinalIgnoreCase) && saved.TryGetValue(LegacyWispName, out var legacy)
                ? new PluginMetadata { Enable = legacy.Enable }
                : name.Equals(CurrentBotName, StringComparison.OrdinalIgnoreCase) && saved.TryGetValue(LegacyFollowerName, out var follower)
                    ? new PluginMetadata { Enable = follower.Enable }
                    : new PluginMetadata();
    }
}
