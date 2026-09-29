namespace Bloodybot2.Configuration;

using System.Text;

// Web saves are the sole writer. Host SaveSettings deliberately never writes defaults.
public sealed class ConfigStore(string directory)
{
    public string FilePath { get; } = Path.Combine(directory, "config.json");
    public string Warning { get; private set; } = "";

    public BotConfig Load()
    {
        this.Warning = "";
        if (!File.Exists(this.FilePath) && !File.Exists(this.FilePath + ".bak")) return new();
        try { return BotConfig.Parse(File.ReadAllText(this.FilePath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            this.Warning = "主配置无法读取，原文件已保留：" + ex.Message;
            try
            {
                var recovered = BotConfig.Parse(File.ReadAllText(this.FilePath + ".bak"));
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
