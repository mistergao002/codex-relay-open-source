# Codex Relay 3.0

Codex Relay 是一个 Windows 原生 WinForms 工具，用于运行 Codex 命令、捕获实时日志，并在命令失败时按配置自动重试。

> This is an unofficial community tool. It is not affiliated with or endorsed by OpenAI.

## 功能

- 原生 Windows GUI，不依赖 Tauri、WebView2 或本地前端服务器。
- 运行配置与日志两个页面。
- 启动时自动读取当前 Codex `config.toml` 的 provider `base_url`，同步到允许 URL 并保存配置。
- 实时读取 stdout/stderr，区分普通警告和真正失败。
- 支持停止整个子进程树、历史日志和本地状态页面。

## 环境要求

- Windows 10/11 x64
- .NET SDK 7.0 或更高版本
- 已安装并可从命令行调用的 Codex CLI

## 构建与测试

```powershell
cd native-source
dotnet build .\CodexRelay.csproj -c Release
dotnet run --project .\tests\CodexRelay.Tests.csproj -c Release
```

## 发布 Windows 单文件程序

```powershell
cd native-source
dotnet publish .\CodexRelay.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

发布输出包含 `codex-relay.exe` 和运行所需的 `_cor3.dll` 文件。请把它们放在同一目录，并将该目录作为 GitHub Release 附件，而不是提交到源码历史。

## 配置与隐私

程序会在运行目录生成 `launcher-config.json` 和 `logs/`。这些文件可能包含工作目录、命令、提示词、URL、时间和执行结果，已通过 `.gitignore` 排除，提交前不要手动添加。

Codex 的当前 URL 从以下位置读取：

1. `CODEX_HOME/config.toml`
2. `%USERPROFILE%\.codex\config.toml`

## 目录结构

```text
native-source/   C# WinForms 源码和测试
docs/            版本验证记录
```

## 许可证

本项目采用 MIT License，详见 [LICENSE](LICENSE)。
