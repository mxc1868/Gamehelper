namespace Bloodybot2.Configuration;

using System.Text;
using System.Text.Json;
using BloodyBot.Combat;
using Bloodybot2.Navigation;

// Web saves are the sole writer. Host SaveSettings deliberately never writes defaults.
public sealed class ConfigStore(string directory, string? legacyFollowerPath = null)
{
    public string FilePath { get; } = Path.Combine(directory, "config.json");
    public string Warning { get; private set; } = "";

    public BotConfig Load()
    {
        this.Warning = "";
        if (!File.Exists(this.FilePath) && !File.Exists(this.FilePath + ".bak")) return this.MigrateFollower(new(), includeGeneral: true);
        try { return this.ReadCurrent(this.FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            this.Warning = "主配置无法读取，原文件已保留：" + ex.Message;
            try
            {
                var recovered = this.ReadCurrent(this.FilePath + ".bak");
                this.Warning += " 已加载上一次备份；请核对后保存。";
                return recovered;
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            {
                this.Warning += " 备份也不可用，当前显示空白配置；尚未覆盖磁盘文件。";
                return new();
            }
        }
    }

    private BotConfig ReadCurrent(string path)
    {
        var json = File.ReadAllText(path);
        var config = BotConfig.Parse(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("schemaVersion", out var version) && version.GetInt32() == 1)
        {
            this.Warning += " 旧版 Bloodybot2 配置已读取，请在 General 中选择模式后保存。";
            config = this.MigrateFollower(config, includeGeneral: false);
        }
        return config;
    }

    private BotConfig MigrateFollower(BotConfig config, bool includeGeneral)
    {
        if (legacyFollowerPath == null || !File.Exists(legacyFollowerPath)) return config;
        var original = config;
        try
        {
            if (new FileInfo(legacyFollowerPath).Length > 256 * 1024) throw new IOException("旧配置过大。");
            var legacy = JsonSerializer.Deserialize<LegacyFollowerSettings>(File.ReadAllText(legacyFollowerPath),
                new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true })
                ?? throw new ArgumentException("旧 Follower 配置为空。");
            legacy.Normalize();
            config = config.Copy();
            config.Follow = legacy;
            if (includeGeneral)
            {
                config.ToggleKey = BotConfig.IsToggleKeyAllowed(legacy.ToggleKey) ? legacy.ToggleKey : 0x75;
                config.Preview = legacy.PreviewOnly;
                config.MonitorCharacter = legacy.LocalCoopFollow ? (legacy.CombatUseP2 ? legacy.P2Name : legacy.P1Name) : "";
                config.Combat = legacy.Combat ?? new();
                config.Combat.Normalize();
            }
            config.Validate();
            this.Warning += " 已读取旧 Follower 的跟随设置；请选择 Follow 模式并核对后保存。旧文件未修改。";
            return config;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            this.Warning += " 旧 Follower 配置无法迁移，原文件已保留：" + ex.Message;
            return original;
        }
    }

    private sealed class LegacyFollowerSettings : FollowSettings
    {
        public int ToggleKey = 0x75;
        public bool PreviewOnly = true;
        public bool CombatUseP2 = false;
        public CombatSettings? Combat = new();
    }

    public void Save(BotConfig config)
    {
        config.Validate();
        Directory.CreateDirectory(directory);
        var temporary = this.FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(config.Serialize());
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(flushToDisk: true); }
            if (File.Exists(this.FilePath))
            {
                var validPrevious = true;
                try { BotConfig.Parse(File.ReadAllText(this.FilePath)); }
                catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException) { validPrevious = false; }
                var backup = validPrevious ? this.FilePath + ".bak" :
                    this.FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
                File.Replace(temporary, this.FilePath, backup);
            }
            else File.Move(temporary, this.FilePath);
            this.Warning = "";
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
