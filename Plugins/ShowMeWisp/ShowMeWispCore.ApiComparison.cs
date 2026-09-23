namespace ShowMeWisp
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using GameHelper;
    using GameHelper.RemoteObjects.States.InGameStateObjects;

    public sealed partial class ShowMeWispCore
    {
        private bool apiComparisonRequested;
        private long nextApiComparison;
        private WispApiComparisonReport? lastApiComparison;

        private WispApiSnapshot NewApiSnapshot(string source, AreaInstance area, EntityScanDiagnostics? stages = null) => new()
        {
            Source = source, AreaAddress = area.Address.ToInt64(), AreaHash = area.AreaHash,
            Generation = this.generation, Stages = stages ?? new(),
        };

        private static WispApiEntity ApiEntity(Entity entity) => new()
        {
            Identity = new(entity.Id, entity.Address.ToInt64(), entity.Path), IsValid = entity.IsValid,
            EntityType = entity.EntityType.ToString(), EntityState = entity.EntityState.ToString(),
        };

        private bool ApiAreaChanged(WispApiSnapshot snapshot, AreaInstance area) =>
            snapshot.Generation != this.generation || snapshot.AreaAddress != area.Address.ToInt64() || snapshot.AreaHash != area.AreaHash;

        private WispApiSnapshot ReadPublicApi(AreaInstance area, bool recreateComponents)
        {
            var snapshot = this.NewApiSnapshot(recreateComponents ? "public_component_recreate" : "public_lookup", area);
            var started = Stopwatch.GetTimestamp();
            try
            {
                var entries = area.AwakeEntities.ToArray();
                snapshot.CollectionCount = entries.Length;
                var candidates = entries.Where(x => WispClassifier.IsCandidate(x.Value.Path)).OrderBy(x => x.Key.id).ToArray();
                snapshot.CandidateCount = candidates.Length;
                foreach (var pair in candidates)
                {
                    if (!snapshot.Reserve()) break;
                    if (this.ApiAreaChanged(snapshot, area)) { snapshot.Discarded = true; break; }
                    var entity = pair.Value;
                    var evidence = ApiEntity(entity);
                    snapshot.Entities[evidence.Identity] = evidence;
                    try
                    {
                        if (!entity.IsValid || pair.Key.id != entity.Id)
                        {
                            evidence.ReadStatus = !entity.IsValid ? "public_entity_invalid" : "public_key_id_mismatch";
                            snapshot.Stages.Record(evidence.ReadStatus, pair.Key.id, entity.Address.ToInt64(), entity.Path);
                            continue;
                        }
                        ReadObservation(entity, snapshot.Stages, evidence, recreateComponents);
                        if (evidence.Identity != new WispApiIdentity(entity.Id, entity.Address.ToInt64(), entity.Path))
                        {
                            evidence.ReadStatus = "public_identity_changed";
                            evidence.Observation = null;
                            snapshot.Discarded = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        evidence.ReadStatus = "public_read_exception";
                        evidence.Error = ex.ToString()[..Math.Min(ex.ToString().Length, 2048)];
                        snapshot.Stages.Record(evidence.ReadStatus, entity.Id, entity.Address.ToInt64(), entity.Path, evidence.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                snapshot.Discarded = true;
                snapshot.Stages.Record("public_snapshot_exception", detail: ex.ToString());
            }
            snapshot.Discarded |= this.ApiAreaChanged(snapshot, area);
            snapshot.CompletedUtc = DateTime.UtcNow;
            snapshot.Milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return snapshot;
        }

        private void CompleteApiComparison(AreaInstance area, WispApiSnapshot[] publicSnapshots, WispApiSnapshot fresh, WispScanReport scan)
        {
            fresh.CompletedUtc = DateTime.UtcNow;
            fresh.Milliseconds = (fresh.CompletedUtc.Value - fresh.StartedUtc).TotalMilliseconds;
            fresh.CollectionCount = scan.Stages.DeclaredCount ?? 0;
            fresh.CandidateCount = (int)scan.Stages.Counts.GetValueOrDefault("candidate");
            fresh.Discarded |= scan.Discarded || this.ApiAreaChanged(fresh, area) ||
                scan.Stages.DeclaredCount == null || scan.Stages.Counts.GetValueOrDefault("entry_seen") != scan.Stages.DeclaredCount;
            foreach (var snapshot in publicSnapshots) snapshot.Discarded |= this.ApiAreaChanged(snapshot, area);
            this.lastApiComparison = new()
            {
                ProcessAllRenderableEntities = Core.GHSettings.ProcessAllRenderableEntities,
                Snapshots = [publicSnapshots[0], publicSnapshots[1], fresh],
                Pairs = [WispApiComparer.Compare(publicSnapshots[0], fresh), WispApiComparer.Compare(publicSnapshots[1], fresh),
                    WispApiComparer.Compare(publicSnapshots[0], publicSnapshots[1])],
            };
            this.capture?.Write("api_comparison", this.lastApiComparison);
            this.nextApiComparison = Environment.TickCount64 + 2000;
        }
    }
}
