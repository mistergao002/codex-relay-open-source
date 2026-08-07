using System.Text;
using System.Text.Json;

namespace CodexRelay.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        string root = Path.Combine(Path.GetTempPath(), "codex-relay-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            RunUiShapeTest(root);
            SynchronizationContext.SetSynchronizationContext(null);
            RunMigrationAndPersistenceTest(root);
            RunInspectorTest(root);
            RunRedactionAndCollapseTest();
            RunRetryTestsAsync(root).GetAwaiter().GetResult();
            Console.WriteLine("SELF_TEST_OK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SELF_TEST_FAILED");
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Test cleanup is best effort.
            }
        }
    }

    private static void RunUiShapeTest(string root)
    {
        string codexHome = Path.Combine(root, "ui-codex-home");
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), """
        model_provider = "startup-test"

        [model_providers.startup-test]
        name = "Startup Test"
        base_url = "https://startup-sync.example/v1"
        """, new UTF8Encoding(false));

        string? previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", codexHome);
            var store = new ConfigStore(Path.Combine(root, "ui"));
            var engine = new RetryEngine(store);
            using var form = new MainForm(store, engine, new CodexConfigInspector());
            Assert(form.NavigationTabCount == 2, "主界面应只有运行配置和日志两个标签页");
            Assert(form.Text.Contains("Codex Relay", StringComparison.Ordinal), "窗口标题应包含 Codex Relay");
            Assert(form.LogWordWrapEnabled, "日志框应开启自动换行");
            Assert(form.LogScrollBars == RichTextBoxScrollBars.Vertical, "日志框应只保留纵向滚动条");
            Assert(
                form.AllowedBaseUrlsText == "https://startup-sync.example/v1",
                "程序启动时应自动同步当前 Codex base_url");
            Assert(
                store.LoadConfig().AllowedBaseUrls == "https://startup-sync.example/v1",
                "启动时同步的 URL 应自动保存到启动器配置");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
        }
    }

    private static void RunMigrationAndPersistenceTest(string root)
    {
        string directory = Path.Combine(root, "migration");
        Directory.CreateDirectory(directory);
        string oldJson = """
        {
          "Command": "echo migrated",
          "NewSessionWorkDir": ".",
          "Interval": 3,
          "MaxTries": 2,
          "Notify": false,
          "OpenDashboard": true,
          "AllowedBaseUrls": "https://example.test/v1",
          "EmailEnabled": true,
          "SessionMode": "resume",
          "TaskName": "old-task"
        }
        """;
        File.WriteAllText(Path.Combine(directory, "launcher-config.json"), oldJson, new UTF8Encoding(false));

        var store = new ConfigStore(directory);
        LauncherConfig config = store.LoadConfig();
        Assert(config.Command == "echo migrated", "旧配置命令应保留");
        Assert(config.WorkDir == ".", "空 WorkDir 应兼容迁移 NewSessionWorkDir");
        Assert(config.Interval == 3 && config.MaxTries == 2, "重试参数应保留");

        store.SaveConfigAsync(config).GetAwaiter().GetResult();
        string saved = File.ReadAllText(store.ConfigPath, Encoding.UTF8);
        Assert(!saved.Contains("EmailEnabled", StringComparison.OrdinalIgnoreCase), "保存后不应写入邮件字段");
        Assert(!saved.Contains("SessionMode", StringComparison.OrdinalIgnoreCase), "保存后不应写入会话字段");
    }

    private static void RunInspectorTest(string root)
    {
        string configPath = Path.Combine(root, "codex-config.toml");
        File.WriteAllText(configPath, """
        model_provider = "custom"

        [model_providers.custom]
        name = "Custom"
        base_url = "https://example.test/v1"
        """, new UTF8Encoding(false));

        var inspector = new CodexConfigInspector();
        CodexConfigInfo info = inspector.Inspect(configPath);
        Assert(info.Found, "应读取到 provider base_url");
        Assert(info.Provider == "custom", "应读取当前 provider");
        Assert(info.BaseUrl == "https://example.test/v1", "base_url 值不正确");
        Assert(inspector.IsCodexCommand("codex exec hello"), "应识别 codex 命令");
        Assert(!inspector.IsCodexCommand("echo codex"), "普通命令不应被识别为 codex 命令");
    }

    private static void RunRedactionAndCollapseTest()
    {
        string bearerSecret = new('b', 16);
        string apiKeySecret = "sk-" + new string('k', 26);
        string redacted = ConfigStore.RedactSensitiveData(
            $"Authorization: Bearer {bearerSecret} {apiKeySecret}");
        Assert(!redacted.Contains(bearerSecret, StringComparison.Ordinal), "Bearer token 应脱敏");
        Assert(!redacted.Contains(apiKeySecret, StringComparison.Ordinal), "API key 应脱敏");
        Assert(redacted.Contains("REDACTED", StringComparison.Ordinal), "日志应包含脱敏标记");

        var collapser = new UiLogCollapser();
        Assert(collapser.Transform("HTTP 403 Forbidden") is not null, "403 行本身应显示");
        string? firstHtml = collapser.Transform("<html><body>large response</body></html>");
        string? secondHtml = collapser.Transform("<div>more html</div>");
        Assert(firstHtml is not null && firstHtml.Contains("折叠", StringComparison.Ordinal), "403 HTML 应显示折叠提示");
        Assert(secondHtml is null, "连续 HTML 行应在界面中隐藏");
    }

    private static async Task RunRetryTestsAsync(string root)
    {
        string directory = Path.Combine(root, "retry");
        Directory.CreateDirectory(directory);
        var store = new ConfigStore(directory);
        var engine = new RetryEngine(store);
        var logLines = new List<LogEntry>();
        engine.LogEmitted += logLines.Add;

        var successConfig = new LauncherConfig
        {
            Command = "echo CORE_OK",
            WorkDir = directory,
            Interval = 1,
            MaxTries = 1,
            Notify = false,
            OpenDashboard = false,
            AllowedBaseUrls = string.Empty
        };
        RetryRunResult success = await engine.RunAsync(successConfig);
        Assert(success.Success, "echo 命令应成功");
        Assert(success.Status.Attempt == 1 && success.Status.LastExitCode == 0, "成功状态字段不正确");
        Assert(File.Exists(store.StatusJsonPath) && File.Exists(store.StatusHtmlPath), "状态文件应生成");
        Assert(logLines.Any(item => item.Message.Contains("CORE_OK", StringComparison.Ordinal)), "应捕获 stdout");

        logLines.Clear();
        string warningScript = Path.Combine(directory, "warning-fixture.cmd");
        File.WriteAllText(
            warningScript,
            "@echo off\r\necho WARN plugin sync failed status 401 Unauthorized 1>&2\r\necho WARNING_SUCCESS\r\n",
            new UTF8Encoding(false));
        LauncherConfig warningConfig = successConfig.Clone();
        warningConfig.Command = "warning-fixture.cmd";
        RetryRunResult warningSuccess = await engine.RunAsync(warningConfig);
        Assert(
            warningSuccess.Success,
            $"stderr 中的非致命 WARN 不应覆盖退出码 0 和有效 stdout；exit={warningSuccess.ExitCode}, preview={warningSuccess.Status.ResultPreview}, error={warningSuccess.Status.LastErrorSnippet}");
        Assert(string.IsNullOrEmpty(warningSuccess.Status.LastErrorSnippet), "成功任务不应保留 WARN 为最后错误");
        LogEntry? warningLine = logLines.FirstOrDefault(item =>
            item.Message.Contains("401 Unauthorized", StringComparison.OrdinalIgnoreCase));
        Assert(warningLine is not null && warningLine.Level == LogLevel.Warning, "普通 WARN/stderr 应显示为黄色警告");

        LauncherConfig failureConfig = successConfig.Clone();
        failureConfig.Command = "exit /b 7";
        failureConfig.MaxTries = 2;
        RetryRunResult failure = await engine.RunAsync(failureConfig);
        Assert(!failure.Success && !failure.Stopped, "失败命令不应标记成功");
        Assert(failure.Status.Attempt == 2 && failure.Status.LastExitCode == 7, "失败重试次数或退出码不正确");

        LauncherConfig forbiddenConfig = successConfig.Clone();
        forbiddenConfig.Command = "echo 403 Forbidden";
        RetryRunResult forbidden = await engine.RunAsync(forbiddenConfig);
        Assert(!forbidden.Success && forbidden.Status.Status == "failed", "403 HTML/错误响应不应被标记为成功");

        var stopEngine = new RetryEngine(store);
        LauncherConfig longConfig = successConfig.Clone();
        longConfig.Command = "ping 127.0.0.1 -n 30 > nul";
        longConfig.MaxTries = 0;
        Task<RetryRunResult> running = stopEngine.RunAsync(longConfig);
        await Task.Delay(500);
        await stopEngine.StopAsync();
        RetryRunResult stopped = await running;
        Assert(stopped.Stopped && stopped.Status.Status == "stopped", "停止操作应结束进程树并标记 stopped");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
