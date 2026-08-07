using System.Text.RegularExpressions;

namespace CodexRelay;

public sealed class CodexConfigInspector
{
    private static readonly Regex SectionRegex = new(
        @"^\s*\[(?<name>[^\]]+)\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AssignmentRegex = new(
        "^\\s*(?<key>[A-Za-z0-9_-]+)\\s*=\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CodexCommandRegex = new(
        @"^\s*(?:&\s*)?(?:""[^""]*[\\/])?codex(?:\.exe)?(?:""|\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string GetDefaultConfigPath()
    {
        string? codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return Path.Combine(codexHome, "config.toml");
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".codex", "config.toml");
    }

    public CodexConfigInfo Inspect(string? explicitPath = null)
    {
        string path = explicitPath ?? GetDefaultConfigPath();
        if (!File.Exists(path))
        {
            return new CodexConfigInfo(path, string.Empty, string.Empty, false);
        }

        string provider = string.Empty;
        string currentSection = string.Empty;
        var providerUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string fallbackUrl = string.Empty;

        foreach (string sourceLine in File.ReadLines(path))
        {
            string line = StripComment(sourceLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            Match sectionMatch = SectionRegex.Match(line);
            if (sectionMatch.Success)
            {
                currentSection = sectionMatch.Groups["name"].Value.Trim();
                continue;
            }

            Match assignmentMatch = AssignmentRegex.Match(line);
            if (!assignmentMatch.Success)
            {
                continue;
            }

            string key = assignmentMatch.Groups["key"].Value;
            string value = Regex.Unescape(assignmentMatch.Groups["value"].Value);

            if (currentSection.Length == 0 && key.Equals("model_provider", StringComparison.OrdinalIgnoreCase))
            {
                provider = value;
                continue;
            }

            if (!key.Equals("base_url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            const string prefix = "model_providers.";
            if (currentSection.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string sectionProvider = currentSection[prefix.Length..].Trim().Trim('"');
                providerUrls[sectionProvider] = value;
            }
            else if (fallbackUrl.Length == 0)
            {
                fallbackUrl = value;
            }
        }

        string baseUrl = provider.Length > 0 && providerUrls.TryGetValue(provider, out string? selected)
            ? selected
            : providerUrls.Count == 1
                ? providerUrls.Values.First()
                : fallbackUrl;

        return new CodexConfigInfo(path, provider, baseUrl, baseUrl.Length > 0);
    }

    public bool IsCodexCommand(string command) => CodexCommandRegex.IsMatch(command ?? string.Empty);

    public string? ValidateForCommand(LauncherConfig config)
    {
        if (!IsCodexCommand(config.Command))
        {
            return null;
        }

        CodexConfigInfo info = Inspect();
        if (!info.Found)
        {
            return $"未在 {info.ConfigPath} 读取到当前 Codex base_url。";
        }

        string current = NormalizeUrl(info.BaseUrl);
        string[] allowed = SplitAllowedUrls(config.AllowedBaseUrls);
        if (allowed.Length == 0)
        {
            return "允许的 Base URL 为空，请先填写或点击“同步当前 URL”。";
        }

        bool accepted = allowed.Any(item => IsAllowed(current, NormalizeUrl(item)));
        return accepted
            ? null
            : $"当前 Codex Base URL 不在允许列表中：{info.BaseUrl}";
    }

    public static string[] SplitAllowedUrls(string value) =>
        (value ?? string.Empty)
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsAllowed(string current, string allowed)
    {
        if (current.Equals(allowed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return current.StartsWith(allowed + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUrl(string value) => value.Trim().TrimEnd('/');

    private static string StripComment(string line)
    {
        bool quoted = false;
        bool escaped = false;
        for (int index = 0; index < line.Length; index++)
        {
            char current = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\' && quoted)
            {
                escaped = true;
                continue;
            }

            if (current == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (current == '#' && !quoted)
            {
                return line[..index];
            }
        }

        return line;
    }
}
