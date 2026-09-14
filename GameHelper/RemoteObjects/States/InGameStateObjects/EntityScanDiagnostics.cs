namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;

    public enum EntityScanSource { Awake, Sleeping }

    public sealed record EntityScanSample(string Stage, uint Id, string Address, string Path, string Detail);

    /// <summary>Per-scan counters and bounded examples. Each scan must use a new instance.</summary>
    public sealed class EntityScanDiagnostics
    {
        private readonly ConcurrentDictionary<string, long> counts = new();
        private readonly ConcurrentQueue<EntityScanSample> samples = new();
        private readonly ConcurrentDictionary<string, int> sampleCounts = new();
        private readonly int samplesPerStage;

        public EntityScanDiagnostics(int samplesPerStage = 4) => this.samplesPerStage = Math.Clamp(samplesPerStage, 0, 16);
        public string Source { get; internal set; } = string.Empty;
        public string AreaAddress { get; internal set; } = string.Empty;
        public string MapHead { get; internal set; } = string.Empty;
        public int? DeclaredCount { get; internal set; }
        public int TraversalIterationCount { get; internal set; }
        public IReadOnlyDictionary<string, long> Counts => new Dictionary<string, long>(this.counts);
        public EntityScanSample[] Samples => this.samples.ToArray();

        public void Record(string stage, uint id = 0, long address = 0, string path = "", string detail = "", bool sample = true)
        {
            this.counts.AddOrUpdate(stage, 1, (_, count) => count + 1);
            if (!sample || this.samplesPerStage == 0) return;
            var slot = this.sampleCounts.AddOrUpdate(stage, 1, (_, count) => count + 1);
            if (slot <= this.samplesPerStage)
                this.samples.Enqueue(new(stage, id, $"0x{address:X}", Limit(path, 512), Limit(detail, 2048)));
        }

        private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    }
}
