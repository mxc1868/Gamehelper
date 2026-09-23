namespace ShowMeWisp
{
    using System.Numerics;

    internal static class WispBoxSizing
    {
        internal static float Scale(WispClassification classification, ShowMeWispSettings settings) =>
            !settings.ScaleWispBoxes || !WispClassifier.IsResource(classification.Kind) ? 1 : classification.Size switch
            {
                WispSize.Small => settings.SmallWispScale,
                WispSize.Medium => settings.MediumWispScale,
                WispSize.Big => settings.BigWispScale,
                _ => 1,
            };

        internal static float MapSize(WispClassification classification, ShowMeWispSettings settings) =>
            settings.MarkerSize * Scale(classification, settings);

        internal static Vector3 GroundSize(WispObservation item, ShowMeWispSettings settings) =>
            item.Classification.Kind == WispKind.Chest ? item.Bounds :
                new Vector3(settings.GroundWidth, settings.GroundWidth, settings.GroundHeight) * Scale(item.Classification, settings);
    }
}
