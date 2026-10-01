namespace Bloodybot2.Navigation;

// Temporary inability to move does not revoke the user's start command.
internal sealed class FollowRunState
{
    public bool IsEnabled { get; private set; }
    public string Status { get; set; } = "stopped";
    public long RetryAt { get; private set; }

    public void Start() { this.IsEnabled = true; this.Status = "searching"; this.RetryAt = 0; }
    public void Stop(string reason) { this.IsEnabled = false; this.Status = reason; this.RetryAt = 0; }
    public bool Ready(long now) => this.IsEnabled && now >= this.RetryAt;

    public void Fail(string reason, long now, int retryMilliseconds = 250)
    {
        if (!this.IsEnabled) return;
        if (IsTerminal(reason)) { this.Stop(reason); return; }
        this.Status = reason;
        this.RetryAt = now + retryMilliseconds;
    }

    public static bool IsTerminal(string reason) => reason is
        "target_missing" or "leader_name" or "primary_name" or "follower_name" or "same_player" or "ambiguous_player";
}
