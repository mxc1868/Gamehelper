namespace ShowMeWisp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using GameHelper.RemoteObjects.States.InGameStateObjects;

    internal sealed class WispScanReport
    {
        public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
        public DateTime? CompletedUtc { get; set; }
        public string AreaHash { get; init; } = "";
        public int Generation { get; init; }
        public string Filter { get; init; } = "classifier candidates";
        public bool Discarded { get; set; }
        public double Milliseconds { get; set; }
        public int Observations { get; set; }
        public EntityScanDiagnostics Stages { get; } = new();
    }

    internal sealed class WispRenderReport
    {
        public DateTime Utc { get; } = DateTime.UtcNow;
        public string Gate { get; set; } = "render_attempted";
        public Dictionary<string, object> Values { get; } = new();
        public EntityScanDiagnostics Stages { get; } = new(8);
    }

    /// <summary>Explicit 60-second capture, bounded files, and no exceptions escaping into the overlay.</summary>
    internal sealed class WispDebugCapture : IDisposable
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            IncludeFields = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly string directory;
        private readonly long maxBytes;
        private readonly int maxFiles;
        private FileStream? stream;
        private long deadline;
        private string session = "";
        public WispDebugCapture(string directory, long maxBytes = 2 * 1024 * 1024, int maxFiles = 3)
        {
            this.directory = directory;
            this.maxBytes = Math.Max(512, maxBytes);
            this.maxFiles = Math.Clamp(maxFiles, 1, 5);
        }

        public bool Active => this.stream != null;
        public string Error { get; private set; } = "";
        public int SecondsRemaining => this.Active ? (int)Math.Max(0, (this.deadline - Environment.TickCount64 + 999) / 1000) : 0;
        public string CurrentFile => Path.Join(this.directory, "capture-0.jsonl");

        public bool Start(long now)
        {
            this.Stop("restarted");
            this.Error = "";
            try
            {
                Directory.CreateDirectory(this.directory);
                this.Rotate();
                this.session = Guid.NewGuid().ToString("N");
                this.deadline = now + 60000;
                this.Write("capture_started", new { DurationSeconds = 60 });
            }
            catch (Exception ex) { this.Fail(ex); }
            return this.Active;
        }

        public bool Expire(long now)
        {
            if (!this.Active || now < this.deadline) return false;
            this.Stop("duration_elapsed");
            return true;
        }

        public void Write(string type, object data)
        {
            if (!this.Active) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Utc = DateTime.UtcNow, Session = this.session, Type = type, Data = data }, JsonOptions) + "\n");
                if (bytes.Length > this.maxBytes)
                    bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Utc = DateTime.UtcNow, Session = this.session, Type = "record_omitted", OriginalType = type, Bytes = bytes.Length }, JsonOptions) + "\n");
                if (this.stream!.Length + bytes.Length > this.maxBytes) this.Rotate();
                this.stream!.Write(bytes);
                this.stream.Flush();
            }
            catch (Exception ex) { this.Fail(ex); }
        }

        public void Stop(string reason)
        {
            if (!this.Active) return;
            this.Write("capture_stopped", new { Reason = reason });
            this.Dispose();
        }

        private void Rotate()
        {
            this.Dispose();
            for (var i = this.maxFiles - 1; i >= 1; i--)
            {
                var previous = Path.Join(this.directory, $"capture-{i - 1}.jsonl");
                if (File.Exists(previous)) File.Move(previous, Path.Join(this.directory, $"capture-{i}.jsonl"), true);
            }
            this.stream = new FileStream(this.CurrentFile, FileMode.Create, FileAccess.Write, FileShare.Read);
        }

        private void Fail(Exception ex)
        {
            this.Error = ex.ToString();
            this.Dispose();
        }

        public void Dispose()
        {
            try { this.stream?.Dispose(); }
            catch (Exception ex) { this.Error = ex.ToString(); }
            finally { this.stream = null; }
        }
    }
}
