namespace Bloodybot2.Navigation;

public class FollowSettings
{
    public string LeaderName = string.Empty;
    public bool LocalCoopFollow;
    public string P1Name = string.Empty;
    public string P2Name = string.Empty;
    public float P2LagDistance = 35;
    public float P2RejoinDistance = 12;
    public float StopDistance = 18;
    public float ResumeDistance = 25;
    public bool AvoidDanger;
    public float DangerRange = 50;
    public int DangerEliteLimit = 3;
    public bool EscapeLowHealth;
    public float EscapeScanRange = 50;
    public float EscapeClusterDistance = 15;
    public float EscapeDistance = 30;
    public int Clearance = 1;
    public int RepathMilliseconds = 350;
    public int StuckMilliseconds = 2500;
    public bool ShowStatus = true;
    public bool ShowRoute = true;

    internal bool IsDangerous(int eliteCount) => this.AvoidDanger && eliteCount > this.DangerEliteLimit;

    public void Normalize()
    {
        this.LeaderName = (this.LeaderName ?? string.Empty).Trim();
        this.P1Name = (this.P1Name ?? string.Empty).Trim();
        this.P2Name = (this.P2Name ?? string.Empty).Trim();
        this.P2LagDistance = float.IsFinite(this.P2LagDistance) ? Math.Clamp(this.P2LagDistance, 6, 150) : 35;
        this.P2RejoinDistance = float.IsFinite(this.P2RejoinDistance) ? Math.Clamp(this.P2RejoinDistance, 3, this.P2LagDistance - 3) : Math.Min(12, this.P2LagDistance - 3);
        this.StopDistance = float.IsFinite(this.StopDistance) ? Math.Clamp(this.StopDistance, 3, 100) : 18;
        this.ResumeDistance = float.IsFinite(this.ResumeDistance) ? Math.Clamp(this.ResumeDistance, this.StopDistance + 3, 150) : this.StopDistance + 7;
        this.DangerRange = float.IsFinite(this.DangerRange) ? Math.Clamp(this.DangerRange, 1, 150) : 50;
        this.DangerEliteLimit = Math.Clamp(this.DangerEliteLimit, 0, 100);
        this.EscapeScanRange = float.IsFinite(this.EscapeScanRange) ? Math.Clamp(this.EscapeScanRange, 5, 150) : 50;
        this.EscapeClusterDistance = float.IsFinite(this.EscapeClusterDistance) ? Math.Clamp(this.EscapeClusterDistance, 3, 50) : 15;
        this.EscapeDistance = float.IsFinite(this.EscapeDistance) ? Math.Clamp(this.EscapeDistance, 3, this.EscapeScanRange) : Math.Min(30, this.EscapeScanRange);
        this.Clearance = Math.Clamp(this.Clearance, 0, 2);
        this.RepathMilliseconds = Math.Clamp(this.RepathMilliseconds, 150, 1000);
        this.StuckMilliseconds = Math.Clamp(this.StuckMilliseconds, 1000, 10000);
    }

}
