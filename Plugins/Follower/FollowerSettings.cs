namespace Follower;

using GameHelper.Plugin;

public sealed class FollowerSettings : IPSettings
{
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
        this.LeaderName = (this.LeaderName ?? string.Empty).Trim();
        this.StopDistance = float.IsFinite(this.StopDistance) ? Math.Clamp(this.StopDistance, 3, 100) : 18;
        this.ResumeDistance = float.IsFinite(this.ResumeDistance) ? Math.Clamp(this.ResumeDistance, this.StopDistance + 3, 150) : this.StopDistance + 7;
        this.Clearance = Math.Clamp(this.Clearance, 0, 2);
        this.RepathMilliseconds = Math.Clamp(this.RepathMilliseconds, 150, 1000);
        this.StuckMilliseconds = Math.Clamp(this.StuckMilliseconds, 1000, 10000);
    }
}
