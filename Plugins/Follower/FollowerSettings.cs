namespace Follower;

using GameHelper.Plugin;

public sealed class FollowerSettings : IPSettings
{
    public const int DefaultToggleKey = 0x75; // F6; F8 is PoE2's screenshot key.
    public int ToggleKey = DefaultToggleKey;
    public string LeaderName = string.Empty;
    public float StopDistance = 18;
    public float ResumeDistance = 25;
    public int Clearance = 1;
    public int RepathMilliseconds = 350;
    public int StuckMilliseconds = 2500;
    public bool PreviewOnly = true;
    public bool ShowStatus = true;
    public bool ShowRoute = true;

    public void Normalize()
    {
        if (!IsToggleKeyAllowed(this.ToggleKey)) this.ToggleKey = DefaultToggleKey;
        this.LeaderName = (this.LeaderName ?? string.Empty).Trim();
        this.StopDistance = float.IsFinite(this.StopDistance) ? Math.Clamp(this.StopDistance, 3, 100) : 18;
        this.ResumeDistance = float.IsFinite(this.ResumeDistance) ? Math.Clamp(this.ResumeDistance, this.StopDistance + 3, 150) : this.StopDistance + 7;
        this.Clearance = Math.Clamp(this.Clearance, 0, 2);
        this.RepathMilliseconds = Math.Clamp(this.RepathMilliseconds, 150, 1000);
        this.StuckMilliseconds = Math.Clamp(this.StuckMilliseconds, 1000, 10000);
    }

    // Exclude mouse buttons, movement keys and keys used by the stop/manual-input guards.
    public static bool IsToggleKeyAllowed(int key) => key is >= 0x08 and <= 0xFE &&
        key is not (0x0D or 0x10 or 0x11 or 0x12 or 0x1B or 0x41 or 0x44 or 0x53 or 0x57 or
                    0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5);
}
