using System.Diagnostics;
using System.Media;
using System.Text;

namespace CodexRelay;

public sealed class RetryEngine
{
    private static readonly string[] HighDemandMarkers =
    {
        "429",
        "high demand",
        "overloaded",
        "too many requests",
        "capacity",
        "高负载",
        "拥堵",
        "请求过多",
        "繁忙"
    };

    private static readonly string[] ErrorMarkers =
    {
        "error",
        "failed",
        "exception",
        "fatal",
        "403",
        "429",
        "错误",
        "失败",
        "异常"
    };

    private static readonly string[] SuccessMarkers =
    {
        "success",
        "succeeded",
        "completed",
        "连接测试成功",
        "成功"
    };

    private static readonly string[] HardFailureMarkers =
    {
        "403",
        "forbidden",
        "unauthorized",
        "rate limit",
        "rate_limit"
    };

    private static readonly string[] CodexCompletionMarkers =
    {
        "tokens used",
        "连接测试成功",
        "task completed",
        "response completed"
    };

    private readonly ConfigStore _store;
    private readonly CodexConfigInspector _inspector;
    private readonly object _sync = new();
    private CancellationTokenSource? _activeCancellation;
    private TaskCompletionSource<bool>? _activeCompletion;
    private Process? _currentProcess;
    private LauncherStatus _status = new();

    public RetryEngine(ConfigStore store, CodexConfigInspector? inspector = null)
    {
        _store = store;
        _inspector = inspector ?? new CodexConfigInspector();
    }

    public event Action<LogEntry>? LogEmitted;
    public event Action<LauncherStatus>? StatusChanged;
    public event Action? Succeeded;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _activeCancellation is not null;
            }
        }
    }

    public LauncherStatus Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _status.Clone();
            }
        }
    }

    public async Task<RetryRunResult> RunAsync(
        LauncherConfig sourceConfig,
        CancellationToken cancellationToken = default)
    {
        LauncherConfig config = sourceConfig.Clone();
        ValidateConfig(config);

        CancellationTokenSource runCancellation;
        TaskCompletionSource<bool> completion;
        lock (_sync)
        {
            if (_activeCancellation is not null)
            {
                throw new InvalidOperationException("重试引擎已经在运行。");
            }

            runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeCancellation = runCancellation;
            _activeCompletion = completion;
        }

        CancellationToken token = runCancellation.Token;
        using var tickerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task tickerTask = Task.CompletedTask;
        string logPath = string.Empty;
        int finalExitCode = 1;
        bool success = false;
        bool stopped = false;

        try
        {
            string runId = Guid.NewGuid().ToString("N")[..10];
            logPath = await _store.PrepareRunLogAsync(runId, token).ConfigureAwait(false);
            LauncherStatus initialStatus = new()
            {
                RunId = runId,
                Status = "running",
                Phase = "starting",
                Message = "准备第一次尝试",
                Command = config.Command,
                WorkDir = config.WorkDir,
                LogFile = logPath,
                LatestLog = _store.LatestLogPath,
                MaxTries = config.MaxTries,
                IntervalSeconds = config.Interval,
                StartedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
                IsRunning = true
            };
            ReplaceStatus(initialStatus);
            await PersistStatusAsync(initialStatus).ConfigureAwait(false);
            tickerTask = RunTickerAsync(tickerCancellation.Token);

            await WriteEngineLogAsync(logPath, LogLevel.Info, $"运行开始，工作目录：{config.WorkDir}")
                .ConfigureAwait(false);
            await WriteEngineLogAsync(logPath, LogLevel.Info, $"执行命令：{config.Command}")
                .ConfigureAwait(false);

            while (true)
            {
                token.ThrowIfCancellationRequested();
                LauncherStatus beforeAttempt = MutateStatus(status =>
                {
                    status.Attempt++;
                    status.Status = "running";
                    status.Phase = "executing";
                    status.Message = $"正在执行第 {status.Attempt} 次尝试";
                    status.Pid = null;
                    status.LastExitCode = null;
                    status.LastErrorSnippet = string.Empty;
                    status.ResultPreview = string.Empty;
                    status.ProgressPercent = config.MaxTries == 0
                        ? 0
                        : Math.Min(100, status.Attempt * 100 / config.MaxTries);
                });
                await PersistStatusAsync(beforeAttempt).ConfigureAwait(false);
                await WriteEngineLogAsync(
                        logPath,
                        LogLevel.Info,
                        $"──────── 第 {beforeAttempt.Attempt} 次尝试 ────────")
                    .ConfigureAwait(false);

                CommandResult result = await RunSingleAttemptAsync(config, logPath, token)
                    .ConfigureAwait(false);
                finalExitCode = result.ExitCode;

                bool attemptSucceeded = result.ExitCode == 0 && HasValidReply(result.StandardOutput);

                LauncherStatus afterAttempt = MutateStatus(status =>
                {
                    status.Pid = null;
                    status.LastExitCode = result.ExitCode;
                    status.ResultPreview = Tail(result.StandardOutput, 4000);
                    status.LastErrorSnippet = attemptSucceeded
                        ? string.Empty
                        : Tail(
                            string.IsNullOrWhiteSpace(result.StandardError)
                                ? result.StandardOutput
                                : result.StandardError,
                            1600);
                });
                await PersistStatusAsync(afterAttempt).ConfigureAwait(false);

                if (attemptSucceeded)
                {
                    success = true;
                    LauncherStatus completed = MutateStatus(status =>
                    {
                        status.Status = "success";
                        status.Phase = "completed";
                        status.Message = $"第 {status.Attempt} 次尝试成功";
                        status.ProgressPercent = 100;
                        status.IsRunning = false;
                    });
                    await WriteEngineLogAsync(logPath, LogLevel.Success, completed.Message)
                        .ConfigureAwait(false);
                    await PersistStatusAsync(completed).ConfigureAwait(false);
                    PerformSuccessActions(config);
                    break;
                }

                bool reachedLimit = config.MaxTries > 0 && afterAttempt.Attempt >= config.MaxTries;
                if (reachedLimit)
                {
                    LauncherStatus failed = MutateStatus(status =>
                    {
                        status.Status = "failed";
                        status.Phase = "completed";
                        status.Message = $"已达到最大尝试次数 {config.MaxTries}";
                        status.ProgressPercent = 100;
                        status.IsRunning = false;
                    });
                    await WriteEngineLogAsync(logPath, LogLevel.Error, failed.Message)
                        .ConfigureAwait(false);
                    await PersistStatusAsync(failed).ConfigureAwait(false);
                    break;
                }

                LauncherStatus waiting = MutateStatus(status =>
                {
                    status.Phase = "waiting";
                    status.Message = $"本次失败，{config.Interval} 秒后重试";
                });
                await WriteEngineLogAsync(logPath, LogLevel.Warning, waiting.Message)
                    .ConfigureAwait(false);
                await PersistStatusAsync(waiting).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(config.Interval), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
            finalExitCode = 130;
            LauncherStatus stoppedStatus = MutateStatus(status =>
            {
                status.Status = "stopped";
                status.Phase = "completed";
                status.Message = "任务已停止";
                status.Pid = null;
                status.IsRunning = false;
            });
            if (logPath.Length > 0)
            {
                await WriteEngineLogAsync(logPath, LogLevel.Warning, stoppedStatus.Message)
                    .ConfigureAwait(false);
            }
            await PersistStatusAsync(stoppedStatus).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            finalExitCode = 1;
            LauncherStatus failedStatus = MutateStatus(status =>
            {
                status.Status = "failed";
                status.Phase = "completed";
                status.Message = exception.Message;
                status.LastErrorSnippet = Tail(exception.ToString(), 1600);
                status.Pid = null;
                status.IsRunning = false;
            });
            if (logPath.Length > 0)
            {
                await WriteEngineLogAsync(logPath, LogLevel.Error, exception.ToString())
                    .ConfigureAwait(false);
            }
            await PersistStatusAsync(failedStatus).ConfigureAwait(false);
        }
        finally
        {
            tickerCancellation.Cancel();
            try
            {
                await tickerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the run reaches a terminal state.
            }
            catch (TimeoutException)
            {
                // A timer callback must never hold the command result open.
            }

            lock (_sync)
            {
                _currentProcess = null;
                _activeCancellation = null;
                _activeCompletion = null;
            }

            runCancellation.Dispose();
            completion.TrySetResult(true);
        }

        return new RetryRunResult(success, stopped, finalExitCode, Snapshot);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        TaskCompletionSource<bool>? completion;
        Process? process;
        lock (_sync)
        {
            cancellation = _activeCancellation;
            completion = _activeCompletion;
            process = _currentProcess;
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        KillProcessTree(process);
        MutateStatus(status =>
        {
            status.Phase = "stopping";
            status.Message = "正在停止当前进程树";
        });

        if (completion is not null)
        {
            try
            {
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                KillProcessTree(process);
            }
        }
    }

    private async Task<CommandResult> RunSingleAttemptAsync(
        LauncherConfig config,
        string logPath,
        CancellationToken token)
    {
        string commandProcessor = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        var startInfo = new ProcessStartInfo
        {
            FileName = commandProcessor,
            WorkingDirectory = config.WorkDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(config.Command);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("命令进程启动失败。");
        }

        lock (_sync)
        {
            _currentProcess = process;
        }

        LauncherStatus started = MutateStatus(status =>
        {
            status.Pid = process.Id;
            status.Message = $"进程 {process.Id} 正在运行";
        });
        await PersistStatusAsync(started).ConfigureAwait(false);

        var output = new StringBuilder();
        var error = new StringBuilder();
        var collapser = new UiLogCollapser();
        var completionMarker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration registration = token.Register(() => KillProcessTree(process));
        Task stdoutTask = PumpStreamAsync(
            process.StandardOutput,
            isErrorStream: false,
            output,
            collapser,
            completionMarker,
            logPath,
            token);
        Task stderrTask = PumpStreamAsync(
            process.StandardError,
            isErrorStream: true,
            error,
            collapser,
            completionMarker,
            logPath,
            token);

        Task exitTask = process.WaitForExitAsync(token);
        bool closingTimedOut = false;

        if (_inspector.IsCodexCommand(config.Command))
        {
            Task first = await Task.WhenAny(exitTask, completionMarker.Task).ConfigureAwait(false);
            if (first == completionMarker.Task && !exitTask.IsCompleted)
            {
                Task closeGrace = Task.Delay(TimeSpan.FromSeconds(20), token);
                Task closed = await Task.WhenAny(exitTask, closeGrace).ConfigureAwait(false);
                if (closed != exitTask)
                {
                    closingTimedOut = true;
                    await WriteEngineLogAsync(
                            logPath,
                            LogLevel.Warning,
                            "Codex 已输出完成标记但进程未退出，已终止进程树并继续判定本次结果。")
                        .ConfigureAwait(false);
                    KillProcessTree(process);
                }
            }
        }

        await exitTask.ConfigureAwait(false);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        int exitCode = closingTimedOut ? -2 : process.ExitCode;
        lock (_sync)
        {
            if (ReferenceEquals(_currentProcess, process))
            {
                _currentProcess = null;
            }
        }

        return new CommandResult(exitCode, output.ToString(), error.ToString(), closingTimedOut);
    }

    private async Task PumpStreamAsync(
        StreamReader reader,
        bool isErrorStream,
        StringBuilder aggregate,
        UiLogCollapser collapser,
        TaskCompletionSource<bool> completionMarker,
        string logPath,
        CancellationToken token)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            AppendBounded(aggregate, line, 64_000);
            if (!isErrorStream && IsUsefulOutput(line))
            {
                MutateStatus(status => status.ResultPreview = Tail(aggregate.ToString(), 4000));
            }
            else if (isErrorStream && !string.IsNullOrWhiteSpace(line))
            {
                MutateStatus(status => status.LastErrorSnippet = Tail(aggregate.ToString(), 1600));
            }

            if (ContainsHighDemandMarker(line))
            {
                MutateStatus(status => status.HighDemandCount++);
            }

            if (CodexCompletionMarkers.Any(marker =>
                    line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                completionMarker.TrySetResult(true);
            }

            string streamName = isErrorStream ? "stderr" : "stdout";
            string rawLine = $"[{DateTimeOffset.Now:O}] [{streamName}] {line}";
            await _store.AppendRawLogAsync(logPath, rawLine, CancellationToken.None).ConfigureAwait(false);

            string? displayLine = collapser.Transform(line);
            if (displayLine is null)
            {
                continue;
            }

            LogLevel level = ClassifyLog(displayLine, isErrorStream);
            RaiseLog(new LogEntry(DateTimeOffset.Now, level, ConfigStore.RedactSensitiveData(displayLine)));
        }
    }

    private async Task RunTickerAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            LauncherStatus snapshot = MutateStatus(status =>
            {
                if (status.StartedAt is null)
                {
                    return;
                }

                TimeSpan elapsed = DateTimeOffset.Now - status.StartedAt.Value;
                status.ElapsedSeconds = Math.Max(0, (long)elapsed.TotalSeconds);
                status.ElapsedText = FormatElapsed(elapsed);
            });
            await PersistStatusAsync(snapshot).ConfigureAwait(false);
        }
    }

    private async Task WriteEngineLogAsync(string logPath, LogLevel level, string message)
    {
        string rawLine = $"[{DateTimeOffset.Now:O}] [engine] {message}";
        await _store.AppendRawLogAsync(logPath, rawLine, CancellationToken.None).ConfigureAwait(false);
        RaiseLog(new LogEntry(DateTimeOffset.Now, level, ConfigStore.RedactSensitiveData(message)));
    }

    private async Task PersistStatusAsync(LauncherStatus snapshot)
    {
        try
        {
            await _store.SaveStatusAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            RaiseLog(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, $"状态文件写入失败：{exception.Message}"));
        }
        catch (UnauthorizedAccessException exception)
        {
            RaiseLog(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, $"状态文件无写入权限：{exception.Message}"));
        }
    }

    private void ValidateConfig(LauncherConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Command))
        {
            throw new InvalidOperationException("执行命令为空。");
        }

        if (string.IsNullOrWhiteSpace(config.WorkDir) || !Directory.Exists(config.WorkDir))
        {
            throw new DirectoryNotFoundException($"工作目录不存在：{config.WorkDir}");
        }

        if (config.Interval <= 0 || config.Interval > 86_400)
        {
            throw new InvalidOperationException("重试间隔必须在 1 到 86400 秒之间。");
        }

        if (config.MaxTries < 0)
        {
            throw new InvalidOperationException("最大尝试次数必须大于或等于 0。");
        }

        string? baseUrlError = _inspector.ValidateForCommand(config);
        if (baseUrlError is not null)
        {
            throw new InvalidOperationException(baseUrlError);
        }
    }

    private void PerformSuccessActions(LauncherConfig config)
    {
        if (config.Notify)
        {
            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch (InvalidOperationException)
            {
                // Audio may be unavailable in a headless Windows session.
            }
        }

        if (config.OpenDashboard)
        {
            try
            {
                _store.OpenDashboard();
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                RaiseLog(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, $"状态面板打开失败：{exception.Message}"));
            }
        }

        try
        {
            Succeeded?.Invoke();
        }
        catch
        {
            // A UI notification failure does not change the command result.
        }
    }

    private void ReplaceStatus(LauncherStatus status)
    {
        lock (_sync)
        {
            _status = status;
        }
        RaiseStatus(status.Clone());
    }

    private LauncherStatus MutateStatus(Action<LauncherStatus> change)
    {
        LauncherStatus snapshot;
        lock (_sync)
        {
            change(_status);
            _status.UpdatedAt = DateTimeOffset.Now;
            snapshot = _status.Clone();
        }
        RaiseStatus(snapshot);
        return snapshot;
    }

    private void RaiseLog(LogEntry entry)
    {
        try
        {
            LogEmitted?.Invoke(entry);
        }
        catch
        {
            // Log observers must not terminate the engine.
        }
    }

    private void RaiseStatus(LauncherStatus status)
    {
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch
        {
            // Status observers must not terminate the engine.
        }
    }

    private static void KillProcessTree(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the checks.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Windows may report access denied after the process already exited.
        }
    }

    private static void AppendBounded(StringBuilder builder, string line, int maximumCharacters)
    {
        builder.AppendLine(line);
        if (builder.Length <= maximumCharacters)
        {
            return;
        }

        builder.Remove(0, builder.Length - maximumCharacters);
    }

    private static bool IsUsefulOutput(string line)
    {
        string value = line.Trim();
        return value.Length > 0 && value.Any(char.IsLetterOrDigit);
    }

    private static bool ContainsHighDemandMarker(string value) =>
        HighDemandMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsHardFailureMarker(string value) =>
        HardFailureMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool HasValidReply(string standardOutput) =>
        !string.IsNullOrWhiteSpace(standardOutput) &&
        standardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(IsUsefulOutput) &&
        !ContainsHighDemandMarker(standardOutput) &&
        !ContainsHardFailureMarker(standardOutput);

    private static LogLevel ClassifyLog(string line, bool isErrorStream)
    {
        if (ContainsExplicitErrorSeverity(line))
        {
            return LogLevel.Error;
        }

        if (SuccessMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return LogLevel.Success;
        }

        if (isErrorStream)
        {
            return LogLevel.Warning;
        }

        return ErrorMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? LogLevel.Error
            : LogLevel.Info;
    }

    private static bool ContainsExplicitErrorSeverity(string line)
    {
        string value = line.Trim();
        return value.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("FATAL", StringComparison.OrdinalIgnoreCase) ||
               value.Contains(" ERROR ", StringComparison.OrdinalIgnoreCase) ||
               value.Contains(" FATAL ", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("错误", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("异常", StringComparison.OrdinalIgnoreCase);
    }

    private static string Tail(string value, int maximumCharacters)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumCharacters)
        {
            return value;
        }

        return value[^maximumCharacters..];
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        int totalHours = (int)elapsed.TotalHours;
        return $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}

public sealed class UiLogCollapser
{
    private readonly object _sync = new();
    private int _forbiddenWindow;
    private bool _summaryShown;

    public string? Transform(string line)
    {
        lock (_sync)
        {
            bool forbidden = line.Contains("403", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
            if (forbidden)
            {
                _forbiddenWindow = 160;
                _summaryShown = false;
            }

            bool html = LooksLikeHtml(line);
            if ((_forbiddenWindow > 0 && html) || (html && line.Length > 1200))
            {
                _forbiddenWindow = Math.Max(0, _forbiddenWindow - 1);
                if (_summaryShown)
                {
                    return null;
                }

                _summaryShown = true;
                return "[已折叠 403 HTML 响应，完整内容已写入原始日志]";
            }

            if (_forbiddenWindow > 0)
            {
                _forbiddenWindow--;
            }

            return line;
        }
    }

    private static bool LooksLikeHtml(string line)
    {
        string value = line.TrimStart();
        return value.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("<head", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("<body", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("<div", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("<script", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("<style", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("</html>", StringComparison.OrdinalIgnoreCase);
    }
}
