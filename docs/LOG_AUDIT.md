# Codex Relay 日志审查

## 本次样本结论

这次 `codex exec` 请求实际成功：

- 进程最后退出码为 `0`。
- stdout 返回了有效回复“连接测试成功”。
- `tokens used` 已输出。
- 最终的“本次失败，10 秒后重试”来自启动器旧版判定逻辑。
- 随后的“任务已停止”是用户手动停止重试后的状态。

## 日志中的 WARN 分类

以下内容属于 Codex 或插件生态的 stderr 警告，不代表主命令失败：

1. 远程插件目录需要 ChatGPT 登录。
2. PowerShell 暂不支持 shell snapshot。
3. featured plugin 请求返回 401。
4. 本地插件的 `defaultPrompt` 超过长度限制。
5. MCP 在进程关闭阶段初始化失败。

这些警告会继续以黄色显示，并写入原始日志；只要退出码为 0 且 stdout 有效回复，重试引擎就标记成功。

## 已修正的判定规则

旧规则把 stderr 中的 `Unauthorized`/`failed` 误当作命令失败。新规则为：

```text
退出码 == 0
且 stdout 有有效回复
且 stdout 不是 403/429/高负载错误页面
=> 成功
```

stderr 中的普通 WARN 不再覆盖成功结果。明确的 ERROR/FATAL 行仍会以红色显示；普通 stderr 以黄色显示。

## 界面修正

- 点击“开始重试”后立即切换到“日志”页。
- 日志框启用自动换行，仅保留纵向滚动条。
- 长路径、URL 和 Codex 诊断行不需要左右拖动查看。
