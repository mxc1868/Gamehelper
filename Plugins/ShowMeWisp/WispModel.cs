namespace ShowMeWisp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;

    public enum WispKind
    {
        UnknownResource, Blue, Yellow, Purple, Sacred, Chest, LightBomb, Well,
        FuelRefill, Altar, DustConverter, Dealer, Encounter,
    }

    public enum WispSize { Unknown, Small, Medium, Big }

    public readonly record struct WispClassification(WispKind Kind, string LabelKey = "", string Label = "", WispSize Size = WispSize.Unknown);

    public sealed record WispObservation(
        uint Id, long Address, string Metadata, string AnimatedPath, string ModelPath,
        WispClassification Classification, Vector2 GridPosition, Vector3 WorldPosition,
        float TerrainHeight, Vector3 Bounds, bool Consumed, bool StateKnown);

    public static class WispClassifier
    {
        private const string ResourcePrefix = "Metadata/MiscellaneousObjects/Azmeri/AzmeriResource";
        private const string ResourceModelPrefix = "Metadata/Effects/Spells/monsters_effects/League_Azmeri/resources/wisp_doodads/wisp_";

        public static bool IsCandidate(string path) =>
            path.Contains("/Azmeri/", StringComparison.Ordinal) ||
            path.StartsWith("Metadata/Chests/LeagueAzmeri/", StringComparison.Ordinal) ||
            path.StartsWith("Metadata/NPC/League/Affliction/Glyphs", StringComparison.Ordinal) ||
            path.StartsWith("Metadata/Monsters/LeagueAzmeri/VoodooKingBoss/", StringComparison.Ordinal) ||
            path == "Metadata/NPC/Ghostrider";

        public static WispClassification? Classify(string metadata, string animatedPath, string modelPath = "")
        {
            if (metadata.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                // The saved 2026-09-14 implementation identifies shared AzmeriResourceBase
                // entities by their resource model. Preserve known Animated.Path colours.
                var kind = animatedPath.Contains("_primal", StringComparison.OrdinalIgnoreCase) ? WispKind.Blue :
                    animatedPath.Contains("_warden", StringComparison.OrdinalIgnoreCase) ? WispKind.Yellow :
                    animatedPath.Contains("_vodoo", StringComparison.OrdinalIgnoreCase) ? WispKind.Purple :
                    animatedPath.Contains("_sacred", StringComparison.OrdinalIgnoreCase) ? WispKind.Sacred :
                    WispKind.UnknownResource;
                var model = ClassifyResourceModel(modelPath);
                if (kind == WispKind.UnknownResource) kind = model.Kind;
                return new(kind, Size: model.Size);
            }

            if (metadata.Contains("Azmeri/SacrificeAltarObjects", StringComparison.Ordinal))
                return new(WispKind.Altar, "marker.altar", "Altar");
            if (metadata.Contains("Azmeri/AzmeriDustConverter", StringComparison.Ordinal))
                return new(WispKind.DustConverter, "marker.converter", "Dust converter");
            if (metadata.Contains("Azmeri/UniqueDealer", StringComparison.Ordinal))
                return new(WispKind.Dealer, "marker.dealer", "Trader");
            if (metadata == "Metadata/Chests/LeagueAzmeri/OmenChest")
                return new(WispKind.Chest, "marker.omen", "Omen chest");
            if (metadata.StartsWith("Metadata/Chests/LeagueAzmeri/", StringComparison.Ordinal))
                return new(WispKind.Chest);

            return metadata switch
            {
                "Metadata/MiscellaneousObjects/Azmeri/AzmeriLightBomb" => new(WispKind.LightBomb, "marker.light", "Light bomb"),
                "Metadata/MiscellaneousObjects/Azmeri/AzmeriFuelResupply" => new(WispKind.FuelRefill, "marker.fuel", "Fuel refill"),
                "Metadata/MiscellaneousObjects/Azmeri/AzmeriFlaskRefill" => new(WispKind.Well, "marker.well", "Well"),
                "Metadata/NPC/League/Affliction/GlyphsHarvestTree" => new(WispKind.Encounter, "marker.harvest", "Harvest"),
                "Metadata/MiscellaneousObjects/Azmeri/AzmeriBuffEffigySmall" or
                "Metadata/MiscellaneousObjects/Azmeri/AzmeriBuffEffigyMedium" or
                "Metadata/MiscellaneousObjects/Azmeri/AzmeriBuffEffigyLarge" => new(WispKind.Encounter, "marker.buff", "Buff"),
                _ when metadata.StartsWith("Metadata/NPC/League/Affliction/Glyphs", StringComparison.Ordinal) ||
                    metadata.StartsWith("Metadata/Monsters/LeagueAzmeri/VoodooKingBoss/", StringComparison.Ordinal) ||
                    metadata == "Metadata/NPC/Ghostrider" =>
                    new(WispKind.Encounter, "", metadata[(metadata.LastIndexOf('/') + 1)..]),
                _ => null,
            };
        }

        private static WispClassification ClassifyResourceModel(string modelPath)
        {
            if (!modelPath.StartsWith(ResourceModelPrefix, StringComparison.OrdinalIgnoreCase) ||
                !modelPath.EndsWith(".ao", StringComparison.OrdinalIgnoreCase)) return new(WispKind.UnknownResource);
            var parts = modelPath[ResourceModelPrefix.Length..^3].ToLowerInvariant().Split('_');
            if (parts.Length is < 1 or > 2 ||
                (parts.Length == 2 && parts[1] is not ("sml" or "med" or "big"))) return new(WispKind.UnknownResource);
            var kind = parts[0] switch
            {
                "primal" => WispKind.Blue,
                "warden" => WispKind.Yellow,
                "vodoo" => WispKind.Purple,
                "sacred" => WispKind.Sacred,
                _ => WispKind.UnknownResource,
            };
            var size = kind == WispKind.UnknownResource || parts.Length == 1 ? WispSize.Unknown : parts[1] switch
            {
                "sml" => WispSize.Small,
                "med" => WispSize.Medium,
                "big" => WispSize.Big,
                _ => WispSize.Unknown,
            };
            return new(kind, Size: size);
        }

        public static bool IsResource(WispKind kind) => kind is WispKind.Blue or WispKind.Yellow or
            WispKind.Purple or WispKind.Sacred or WispKind.UnknownResource;

        public static bool UsesActivation(WispKind kind) => kind is WispKind.Well or WispKind.Altar or WispKind.DustConverter;
    }

    public static class WispTrails
    {
        /// <summary>Only connect consecutive IDs of a known color within the configured grid distance.</summary>
        public static IEnumerable<(WispObservation From, WispObservation To)> Build(
            IEnumerable<WispObservation> observations, float maxGridDistance)
        {
            if (!float.IsFinite(maxGridDistance) || maxGridDistance <= 0) yield break;
            foreach (var group in observations.Where(x => !x.Consumed &&
                         WispClassifier.IsResource(x.Classification.Kind) &&
                         x.Classification.Kind != WispKind.UnknownResource).GroupBy(x => x.Classification.Kind))
            {
                WispObservation? previous = null;
                foreach (var current in group.OrderBy(x => x.Id))
                {
                    if (previous != null && previous.Id < uint.MaxValue && current.Id == previous.Id + 1 &&
                        Vector2.Distance(previous.GridPosition, current.GridPosition) < maxGridDistance)
                        yield return (previous, current);
                    previous = current;
                }
            }
        }
    }
}
