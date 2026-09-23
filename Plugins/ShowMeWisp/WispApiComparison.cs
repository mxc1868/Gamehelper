namespace ShowMeWisp
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Text.Json.Serialization;
    using System.Threading;
    using GameHelper.RemoteObjects.States.InGameStateObjects;

    internal readonly record struct WispApiIdentity(uint Id, long Address, string Metadata);
    internal sealed record WispComponentEvidence(long Address, bool ParentValid);

    internal sealed class WispApiEntity
    {
        public required WispApiIdentity Identity { get; init; }
        public bool IsValid { get; init; }
        public string EntityType { get; init; } = "";
        public string EntityState { get; init; } = "";
        public string ReadStatus { get; set; } = "reading";
        public string Error { get; set; } = "";
        public Dictionary<string, WispComponentEvidence> Components { get; } = new();
        public string AnimatedPath { get; set; } = "";
        public string ModelPath { get; set; } = "";
        public WispKind? Kind { get; set; }
        public WispSize? Size { get; set; }
        public Vector2? Grid { get; set; }
        public Vector3? World { get; set; }
        public Vector3? Bounds { get; set; }
        public float? TerrainHeight { get; set; }
        public bool? ChestOpened { get; set; }
        public long? ActivatedValue { get; set; }
        public string[] States { get; set; } = [];
        [JsonIgnore] public WispObservation? Observation { get; set; }
        public bool ObservationAvailable => this.Observation != null;
        public bool? Consumed => this.Observation?.Consumed;
        public bool? StateKnown => this.Observation?.StateKnown;
    }

    internal sealed class WispApiSnapshot
    {
        public const int MaxEntities = 2000;
        private int reserved;
        public required string Source { get; init; }
        public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
        public DateTime? CompletedUtc { get; set; }
        public double Milliseconds { get; set; }
        public string AreaHash { get; init; } = "";
        public long AreaAddress { get; init; }
        public int Generation { get; init; }
        public bool Discarded { get; set; }
        public bool Truncated => this.reserved > MaxEntities;
        public int CollectionCount { get; set; }
        public int CandidateCount { get; set; }
        public EntityScanDiagnostics Stages { get; init; } = new();
        [JsonIgnore] public ConcurrentDictionary<WispApiIdentity, WispApiEntity> Entities { get; } = new();
        public int CapturedCount => this.Entities.Count;
        public int ObservationCount => this.Entities.Values.Count(x => x.ObservationAvailable);
        public int UnknownKindCount => this.Entities.Values.Count(x => x.Kind == WispKind.UnknownResource);
        public int UnknownStateCount => this.Entities.Values.Count(x => x.ObservationAvailable && x.StateKnown == false);
        public WispApiEntity[] Samples => this.Entities.Values.OrderBy(x => x.Identity.Id).Take(8).ToArray();
        public bool Reserve() => Interlocked.Increment(ref this.reserved) <= MaxEntities;
    }

    internal sealed record WispApiDifference(string[] Fields, WispApiEntity? Left, WispApiEntity? Right);

    internal sealed class WispApiPairComparison
    {
        public required string Left { get; init; }
        public required string Right { get; init; }
        public bool CompleteWindow { get; init; }
        public int OnlyLeft { get; set; }
        public int OnlyRight { get; set; }
        public int Common { get; set; }
        public int BothUsable { get; set; }
        public int NeitherUsable { get; set; }
        public int OnlyLeftUsable { get; set; }
        public int OnlyRightUsable { get; set; }
        public int EqualObservations { get; set; }
        public Dictionary<string, int> FieldDifferences { get; } = new();
        public List<WispApiDifference> MissingSamples { get; } = new();
        public List<WispApiDifference> ValueSamples { get; } = new();
        public List<WispApiDifference> UnusableSamples { get; } = new();
        public List<WispApiDifference> EqualSamples { get; } = new();
    }

    internal sealed class WispApiComparisonReport
    {
        public int SchemaVersion { get; } = 1;
        public DateTime Utc { get; } = DateTime.UtcNow;
        public bool ProcessAllRenderableEntities { get; init; }
        public required WispApiSnapshot[] Snapshots { get; init; }
        public required WispApiPairComparison[] Pairs { get; init; }
        public string Interpretation { get; } = "Sequential reads, not an atomic snapshot. No route is ground truth. Empty/unusable matches and matching unknown colors/states do not establish API sufficiency. CompleteWindow only checks area, read limits and reported failures. Repeated differences in stable scenes need live review. Public lookup uses shouldCache:false, which still returns existing cached components. Recreated components use public constructors and the public entity's component addresses; they cannot discover entities missing from AwakeEntities.";
        public object Tolerances { get; } = new { World = 0.5f, Grid = 0.05f, TerrainHeight = 0.1f, Bounds = 0.1f };
    }

    internal static class WispApiComparer
    {
        public static WispApiPairComparison Compare(WispApiSnapshot left, WispApiSnapshot right)
        {
            var result = new WispApiPairComparison
            {
                Left = left.Source, Right = right.Source,
                CompleteWindow = !left.Discarded && !right.Discarded && !left.Truncated && !right.Truncated &&
                    left.AreaHash == right.AreaHash && left.AreaAddress == right.AreaAddress && left.Generation == right.Generation,
            };
            foreach (var key in left.Entities.Keys.Union(right.Entities.Keys).OrderBy(x => x.Id).ThenBy(x => x.Address))
            {
                left.Entities.TryGetValue(key, out var a);
                right.Entities.TryGetValue(key, out var b);
                if (a == null || b == null)
                {
                    if (a == null) result.OnlyRight++; else result.OnlyLeft++;
                    Add(result.MissingSamples, new([a == null ? "missing_left" : "missing_right"], a, b));
                    continue;
                }
                result.Common++;
                var fields = new List<string>();
                void Diff(string name, bool different) { if (different) fields.Add(name); }
                Diff("validity", a.IsValid != b.IsValid);
                Diff("entity_type", a.EntityType != b.EntityType);
                Diff("entity_state", a.EntityState != b.EntityState);
                Diff("read_status", a.ReadStatus != b.ReadStatus);
                Diff("animated_path", a.AnimatedPath != b.AnimatedPath);
                Diff("model_path", a.ModelPath != b.ModelPath);
                Diff("kind", a.Kind != b.Kind);
                Diff("wisp_size", a.Size != b.Size);
                Diff("grid", !Near(a.Grid, b.Grid, 0.05f));
                Diff("world", !Near(a.World, b.World, 0.5f));
                Diff("terrain_height", !Near(a.TerrainHeight, b.TerrainHeight, 0.1f));
                Diff("bounds", !Near(a.Bounds, b.Bounds, 0.1f));
                Diff("chest_opened", a.ChestOpened != b.ChestOpened);
                Diff("activated_value", a.ActivatedValue != b.ActivatedValue);
                Diff("states", !a.States.SequenceEqual(b.States));
                Diff("components", a.Components.Count != b.Components.Count || a.Components.Any(x => !b.Components.TryGetValue(x.Key, out var value) || value != x.Value));
                Diff("observation_available", a.ObservationAvailable != b.ObservationAvailable);
                Diff("consumed", a.Consumed != b.Consumed);
                Diff("state_known", a.StateKnown != b.StateKnown);
                foreach (var field in fields) result.FieldDifferences[field] = result.FieldDifferences.GetValueOrDefault(field) + 1;
                if (fields.Count > 0) Add(result.ValueSamples, new(fields.ToArray(), a, b));
                if (a.ObservationAvailable && b.ObservationAvailable)
                {
                    result.BothUsable++;
                    if (fields.All(x => x is "entity_type" or "entity_state"))
                    {
                        result.EqualObservations++;
                        Add(result.EqualSamples, new([], a, b));
                    }
                }
                else
                {
                    if (!a.ObservationAvailable && !b.ObservationAvailable) result.NeitherUsable++;
                    else if (a.ObservationAvailable) result.OnlyLeftUsable++;
                    else result.OnlyRightUsable++;
                    Add(result.UnusableSamples, new(["observation_unavailable"], a, b));
                }
            }
            return result;
        }

        private static void Add(List<WispApiDifference> list, WispApiDifference sample)
        { if (list.Count < 8) list.Add(sample); }
        private static bool Near(float? a, float? b, float tolerance) =>
            a == null || b == null ? a == null && b == null : float.IsFinite(a.Value) && float.IsFinite(b.Value) && Math.Abs(a.Value - b.Value) <= tolerance;
        private static bool Near(Vector2? a, Vector2? b, float tolerance) =>
            a == null || b == null ? a == null && b == null : Near(a.Value.X, b.Value.X, tolerance) && Near(a.Value.Y, b.Value.Y, tolerance);
        private static bool Near(Vector3? a, Vector3? b, float tolerance) =>
            a == null || b == null ? a == null && b == null : Near(a.Value.X, b.Value.X, tolerance) && Near(a.Value.Y, b.Value.Y, tolerance) && Near(a.Value.Z, b.Value.Z, tolerance);
    }
}
