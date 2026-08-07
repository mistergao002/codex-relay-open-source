using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexRelay;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly Regex BearerRegex = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{8,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ApiKeyRegex = new(
        @"\b(?:sk|rk|pk)-[A-Za-z0-9_-]{12,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HeaderRegex = new(
        @"(?i)(Authorization\s*[:=]\s*)([^\s,;]+(?:\s+[^\s,;]+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AssignmentSecretRegex = new(
        @"(?i)((?:api[_-]?key|token|secret)\s*[:=]\s*[""']?)([^\s""',;}{]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SemaphoreSlim _logGate = new(1, 1);
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    public ConfigStore(string baseDirectory)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
        LogsDirectory = Path.Combine(BaseDirectory, "logs");
        ConfigPath = Path.Combine(BaseDirectory, "launcher-config.json");
        StatusJsonPath = Path.Combine(LogsDirectory, "status.json");
        StatusHtmlPath = Path.Combine(LogsDirectory, "status.html");
        LatestLogPath = Path.Combine(LogsDirectory, "latest.log");
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public string BaseDirectory { get; }
    public string LogsDirectory { get; }
    public string ConfigPath { get; }
    public string StatusJsonPath { get; }
    public string StatusHtmlPath { get; }
    public string LatestLogPath { get; }

    public LauncherConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            return NormalizeConfig(new LauncherConfig());
        }

        try
        {
            string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            LauncherConfig config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions)
                ?? new LauncherConfig();

            using JsonDocument document = JsonDocument.Parse(json);
            bool hasCurrentWorkDir = TryGetPropertyIgnoreCase(document.RootElement, "WorkDir", out _);
            if ((!hasCurrentWorkDir || string.IsNullOrWhiteSpace(config.WorkDir)) &&
                TryGetPropertyIgnoreCase(document.RootElement, "NewSessionWorkDir", out JsonElement legacyWorkDir) &&
                legacyWorkDir.ValueKind == JsonValueKind.String)
            {
                config.WorkDir = legacyWorkDir.GetString() ?? string.Empty;
            }

            return NormalizeConfig(config);
        }
        catch (JsonException exception)
        {
            WriteDiagnostic("配置解析失败", exception);
            return NormalizeConfig(new LauncherConfig());
        }
        catch (IOException exception)
        {
            WriteDiagnostic("配置读取失败", exception);
            return NormalizeConfig(new LauncherConfig());
        }
    }

    public Task SaveConfigAsync(LauncherConfig config, CancellationToken cancellationToken = default)
    {
        LauncherConfig normalized = NormalizeConfig(config.Clone());
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        return AtomicWriteTextAsync(ConfigPath, json, cancellationToken);
    }

    public async Task<string> PrepareRunLogAsync(string runId, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LogsDirectory);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string logPath = Path.Combine(LogsDirectory, $"codex-retry-{stamp}-{runId}.log");

        await _logGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(logPath, string.Empty, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(LatestLogPath, string.Empty, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _logGate.Release();
        }

        return logPath;
    }

    public async Task AppendRawLogAsync(
        string logPath,
        string message,
        CancellationToken cancellationToken = default)
    {
        string safeMessage = RedactSensitiveData(message);
        string line = safeMessage.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? safeMessage
            : safeMessage + Environment.NewLine;

        await _logGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(logPath, line, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            await File.AppendAllTextAsync(LatestLogPath, line, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _logGate.Release();
        }
    }

    public async Task SaveStatusAsync(LauncherStatus status, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(status, JsonOptions);
        string html = BuildStatusHtml(status);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicWriteTextAsync(StatusJsonPath, json, cancellationToken).ConfigureAwait(false);
            await AtomicWriteTextAsync(StatusHtmlPath, html, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public int ClearHistoryLogs()
    {
        Directory.CreateDirectory(LogsDirectory);
        int count = 0;
        foreach (string path in Directory.EnumerateFiles(LogsDirectory, "codex-retry-*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(path);
                count++;
            }
            catch (IOException)
            {
                // A currently running log remains in place.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve files that Windows currently protects.
            }
        }

        try
        {
            File.WriteAllText(LatestLogPath, string.Empty, new UTF8Encoding(false));
        }
        catch (IOException)
        {
            // The active run still owns the latest log; leave it intact.
        }

        return count;
    }

    public void OpenLogDirectory()
    {
        Directory.CreateDirectory(LogsDirectory);
        OpenPath(LogsDirectory);
    }

    public void OpenDashboard()
    {
        if (File.Exists(StatusHtmlPath))
        {
            OpenPath(StatusHtmlPath);
        }
    }

    public static string RedactSensitiveData(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        string redacted = BearerRegex.Replace(value, "Bearer ***REDACTED***");
        redacted = ApiKeyRegex.Replace(redacted, "sk-***REDACTED***");
        redacted = HeaderRegex.Replace(redacted, "$1***REDACTED***");
        redacted = AssignmentSecretRegex.Replace(redacted, "$1***REDACTED***");
        return redacted;
    }

    private static LauncherConfig NormalizeConfig(LauncherConfig config)
    {
        config.Command = config.Command?.Trim() ?? string.Empty;
        config.WorkDir = string.IsNullOrWhiteSpace(config.WorkDir)
            ? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
            : config.WorkDir.Trim();
        config.Interval = config.Interval <= 0 ? 10 : config.Interval;
        config.MaxTries = Math.Max(0, config.MaxTries);
        config.AllowedBaseUrls = config.AllowedBaseUrls?.Trim() ?? string.Empty;
        return config;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static async Task AtomicWriteTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(tempPath, content, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        try
        {
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private string BuildStatusHtml(LauncherStatus status)
    {
        static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
        string color = status.Status switch
        {
            "success" => "#16a34a",
            "failed" => "#dc2626",
            "running" => "#2563eb",
            "stopped" => "#d97706",
            _ => "#64748b"
        };

        return $$"""
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta http-equiv="refresh" content="2">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>Codex Relay 状态</title>
          <style>
            :root { color-scheme: light dark; font-family: "Segoe UI", sans-serif; }
            body { margin: 0; background: #0f172a; color: #e2e8f0; }
            main { max-width: 920px; margin: 36px auto; padding: 0 20px; }
            .header { display:flex; justify-content:space-between; align-items:center; gap:16px; }
            .state { color:white; background:{{color}}; border-radius:999px; padding:7px 14px; font-weight:700; }
            .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(170px,1fr)); gap:12px; margin:22px 0; }
            .card { background:#1e293b; border:1px solid #334155; border-radius:12px; padding:16px; }
            .label { color:#94a3b8; font-size:12px; text-transform:uppercase; letter-spacing:.08em; }
            .value { margin-top:8px; font-size:22px; font-weight:700; overflow-wrap:anywhere; }
            pre { white-space:pre-wrap; overflow-wrap:anywhere; background:#020617; border:1px solid #334155; border-radius:12px; padding:16px; }
          </style>
        </head>
        <body><main>
          <div class="header"><div><h1>Codex Relay</h1><div>{{E(status.Message)}}</div></div><div class="state">{{E(status.Status)}}</div></div>
          <section class="grid">
            <div class="card"><div class="label">尝试次数</div><div class="value">{{status.Attempt}}</div></div>
            <div class="card"><div class="label">高负载次数</div><div class="value">{{status.HighDemandCount}}</div></div>
            <div class="card"><div class="label">运行时间</div><div class="value">{{E(status.ElapsedText)}}</div></div>
            <div class="card"><div class="label">最后退出码</div><div class="value">{{status.LastExitCode?.ToString() ?? "-"}}</div></div>
          </section>
          <h2>执行命令</h2><pre>{{E(status.Command)}}</pre>
          <h2>工作目录</h2><pre>{{E(status.WorkDir)}}</pre>
          <h2>结果预览</h2><pre>{{E(status.ResultPreview)}}</pre>
          <h2>最后错误</h2><pre>{{E(status.LastErrorSnippet)}}</pre>
        </main></body></html>
        """;
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void WriteDiagnostic(string title, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            File.AppendAllText(
                Path.Combine(LogsDirectory, "diagnostics.log"),
                $"[{DateTimeOffset.Now:O}] {title}: {exception}\r\n",
                new UTF8Encoding(false));
        }
        catch
        {
            // Diagnostics must never prevent startup.
        }
    }
}
