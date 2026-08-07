using System.Text.Json.Serialization;

namespace CodexRelay;

public sealed class LauncherConfig
{
    public string Command { get; set; } =
        "codex exec --skip-git-repo-check 你好，请回复连接测试成功";

    public string WorkDir { get; set; } =
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public int Interval { get; set; } = 10;
    public int MaxTries { get; set; }
    public bool Notify { get; set; } = true;
    public bool OpenDashboard { get; set; }
    public string AllowedBaseUrls { get; set; } = "https://api.openai.com/v1";

    public LauncherConfig Clone() => new()
    {
        Command = Command,
        WorkDir = WorkDir,
        Interval = Interval,
        MaxTries = MaxTries,
        Notify = Notify,
        OpenDashboard = OpenDashboard,
        AllowedBaseUrls = AllowedBaseUrls
    };
}

public sealed class LauncherStatus
{
    public string RunId { get; set; } = string.Empty;
    public int? Pid { get; set; }
    public string Status { get; set; } = "idle";
    public string Phase { get; set; } = "idle";
    public string Message { get; set; } = "等待开始";
    public string Command { get; set; } = string.Empty;
    public string WorkDir { get; set; } = string.Empty;
    public string LogFile { get; set; } = string.Empty;
    public string LatestLog { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public int HighDemandCount { get; set; }
    public int MaxTries { get; set; }
    public int IntervalSeconds { get; set; }
    public int ProgressPercent { get; set; }
    public int? LastExitCode { get; set; }
    public string LastErrorSnippet { get; set; } = string.Empty;
    public string ResultPreview { get; set; } = string.Empty;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public long ElapsedSeconds { get; set; }
    public string ElapsedText { get; set; } = "00:00:00";
    public bool IsRunning { get; set; }

    [JsonIgnore]
    public bool IsTerminal => Status is "success" or "failed" or "stopped";

    public LauncherStatus Clone() => (LauncherStatus)MemberwiseClone();
}

public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool ClosingTimedOut);

public sealed record RetryRunResult(
    bool Success,
    bool Stopped,
    int ExitCode,
    LauncherStatus Status);

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Message);

public sealed record CodexConfigInfo(
    string ConfigPath,
    string Provider,
    string BaseUrl,
    bool Found);
