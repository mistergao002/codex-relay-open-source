# Codex Relay 3.0 源码

这是 Codex Relay 3.0 的 C# WinForms 实现。

## 核心模块

- `MainForm.cs`：运行配置、日志页面和启动时 URL 同步。
- `RetryEngine.cs`：子进程执行、实时 stdout/stderr、重试和进程树停止。
- `ConfigStore.cs`：配置、状态 JSON/HTML 和原始日志持久化。
- `CodexConfigInspector.cs`：读取当前 Codex `config.toml` 并解析 provider 的 `base_url`。
- `DirectoryPickerForm.cs`：工作目录选择器。
- `tests/`：自测程序。

## 构建

```powershell
dotnet build .\CodexRelay.csproj -c Release
dotnet run --project .\tests\CodexRelay.Tests.csproj -c Release
```

程序启动时会自动读取当前 Codex provider 的 `base_url`，更新允许 URL 文本框并保存到运行目录的 `launcher-config.json`。
