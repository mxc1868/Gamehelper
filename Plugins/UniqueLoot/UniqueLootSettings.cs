namespace UniqueLoot;

using GameHelper.Plugin;

public sealed class UniqueLootSettings : IPSettings
{
    public bool ShowGroundNames = true;
    public bool OnlyShowHighlightedItemNames = true;
    public bool ShowList = true;
    public bool ShowUnknown = true;
    public bool HighlightPriorityDrops = true;
    public bool ShowItemIcons = true;
    public float HighlightFontScale = 1.6f;
    public bool HideWhenUnfocused = true;
    public bool HideWhenPanelsOpen = true;
    public int ScanIntervalMs = 500;
    public int MaxLabels = 30;
    public int ListX = 30;
    public int ListY = 180;
    public int GroundOffsetY = -20;

    public void Normalize()
    {
        ScanIntervalMs = Math.Clamp(ScanIntervalMs, 200, 5000);
        MaxLabels = Math.Clamp(MaxLabels, 1, 100);
        ListX = Math.Clamp(ListX, 0, 10000);
        ListY = Math.Clamp(ListY, 0, 10000);
        GroundOffsetY = Math.Clamp(GroundOffsetY, -300, 300);
        HighlightFontScale = float.IsFinite(HighlightFontScale) ? Math.Clamp(HighlightFontScale, 1.3f, 2.5f) : 1.6f;
    }
}
