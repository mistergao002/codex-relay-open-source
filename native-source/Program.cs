using System.Text;

namespace CodexRelay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var store = new ConfigStore(baseDirectory);
        InstallCrashHandlers(store);

        if (args.Any(argument => argument.Equals("--headless", StringComparison.OrdinalIgnoreCase)))
        {
            NativeMethods.AttachToParentConsole();
            TryInitializeConsole();
            return RunHeadlessAsync(store).GetAwaiter().GetResult();
        }

        ApplicationConfiguration.Initialize();
        var inspector = new CodexConfigInspector();
        var engine = new RetryEngine(store, inspector);
        Application.Run(new MainForm(store, engine, inspector));
        return 0;
    }

    private static async Task<int> RunHeadlessAsync(ConfigStore store)
    {
        var inspector = new CodexConfigInspector();
        var engine = new RetryEngine(store, inspector);
        engine.LogEmitted += entry =>
        {
            TextWriter writer = entry.Level == LogLevel.Error ? Console.Error : Console.Out;
            writer.WriteLine($"[{entry.Timestamp:HH:mm:ss}] {entry.Message}");
        };

        try
        {
            LauncherConfig config = store.LoadConfig();
            RetryRunResult result = await engine.RunAsync(config).ConfigureAwait(false);
            if (result.Success)
            {
                return 0;
            }

            return result.ExitCode == 0 ? 1 : result.ExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void InstallCrashHandlers(ConfigStore store)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => WriteCrash(store, eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                WriteCrash(store, exception);
            }
        };
    }

    private static void WriteCrash(ConfigStore store, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(store.LogsDirectory);
            File.AppendAllText(
                Path.Combine(store.LogsDirectory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {exception}\r\n",
                new UTF8Encoding(false));
        }
        catch
        {
            // The crash handler must not throw another exception.
        }
    }

    private static void TryInitializeConsole()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            var error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            Console.SetOut(output);
            Console.SetError(error);
        }
        catch (IOException)
        {
            // Scheduled tasks may not have a parent console.
        }
    }
}
