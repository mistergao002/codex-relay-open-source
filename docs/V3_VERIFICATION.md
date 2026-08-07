# Codex Relay 3.0.0 验证记录

验证日期：2026-08-07

## 构建

- `dotnet build -c Release`：通过。
- 编译结果：0 个警告，0 个错误。
- 发布方式：Windows x64、自包含、单文件压缩、未裁剪。

## 自动测试

测试入口：`native-source/tests/CodexRelay.Tests.csproj`

结果：`SELF_TEST_OK`

覆盖内容：

1. 主界面只有“运行配置”和“日志”两个标签页。
2. 旧配置在缺少 `WorkDir` 时迁移 `NewSessionWorkDir`，并保留命令和重试参数；保存后移除旧字段。
3. Codex `config.toml` provider 与 `base_url` 解析。
4. GUI 启动时自动同步当前 provider 的 `base_url`，并保存到 `launcher-config.json`。
5. Bearer Token、API Key 和敏感赋值脱敏。
6. 403 HTML 在 UI 中折叠，退出码为 0 的 403 响应仍判定失败。
7. 成功命令退出码、stdout 和状态更新。
8. 失败命令按最大次数重试。
9. 停止命令会结束当前进程树并标记 `stopped`。
10. `status.json`、`status.html`、`latest.log` 和原始运行日志生成。
11. stderr 中含 `WARN`、`failed`、`401 Unauthorized` 时，只要退出码为 0 且 stdout 有效，仍正确判定成功。
12. 普通 stderr/WARN 为黄色，成功任务不会把 WARN 保存为最后错误。

## Headless 实测

执行命令：`echo HEADLESS_V3_OK`

- 进程退出码：0。
- 尝试次数：1。
- 最后退出码：0。
- 状态：`success`。
- stdout 已写入 `latest.log` 和原始日志。

## GUI 实测

- 窗口标题：`Codex Relay 3.0`。
- UI Automation 检测到两个原生 TabItem：`运行配置`、`日志`。
- 检测到原生 Group：`执行命令`、`工作目录`、`重试参数`、`成功动作`。
- 默认 1160×820 窗口下，四个配置区连续显示，`成功动作`及两个复选框均可见，配置页未生成滚动条。
- 命令框高度 122、工作目录区高度 78、重试参数区高度 140、成功动作区高度 70，各区域间距为 8。
- 点击“开始重试”会立即进入日志页。
- 日志框自动换行，仅启用纵向滚动条。
- 程序打开时自动读取并同步当前 Codex provider 的 `base_url`，无需先点击“同步当前 URL”。
- 空闲启动时没有子进程。
- 程序不读取 localhost，也不需要前端开发服务器。

## 发布产物

- 目录：`artifacts/codex-relay-3.0.0-win-x64/`
- 主程序：`codex-relay.exe`
- 大小：67,554,700 字节。
- SHA-256：`4E2623978DFF391B66352A96C61F482F5D5016D9E2B6AEC5152061B3FE40A2BF`
- 发布目录不包含 `detector/`、Python 文件或邮件/会话/计划任务模块。
