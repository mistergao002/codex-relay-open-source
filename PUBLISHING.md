# GitHub 发布指南

以下流程适合第一次使用 GitHub 的维护者。

## 1. 安装并登录 GitHub Desktop

1. 从 <https://desktop.github.com/> 安装 GitHub Desktop。
2. 打开 GitHub Desktop，登录自己的 GitHub 账号。
3. 在 GitHub 的 `Settings → Emails` 中启用隐私邮箱，避免提交记录公开常用邮箱。

## 2. 添加本地仓库

1. 在 GitHub Desktop 点击 `File → Add local repository`。
2. 选择本项目所在文件夹。
3. 点击 `Add repository`。

本目录已经初始化为 `main` 分支，并配置了 `.gitignore`。编译缓存、配置、日志和发布程序不会进入提交列表。

## 3. 创建第一次提交

1. 在左侧 `Changes` 检查准备上传的文件。
2. 确认列表中没有 `bin`、`obj`、`logs`、`launcher-config.json`、EXE 或 DLL。
3. 在左下角 Summary 输入 `Initial open-source release`。
4. 点击 `Commit to main`。

## 4. 发布为公开仓库

1. 点击 GitHub Desktop 顶部的 `Publish repository`。
2. Repository name 填写 `codex-relay`。
3. 取消勾选 `Keep this code private`。
4. 点击 `Publish repository`。

## 5. 发布可下载程序

在源码目录执行：

```powershell
dotnet publish .\native-source\CodexRelay.csproj -c Release -r win-x64 --self-contained true `
  -o .\artifacts\codex-relay-3.0.0-win-x64

Compress-Archive `
  -Path .\artifacts\codex-relay-3.0.0-win-x64\* `
  -DestinationPath .\codex-relay-3.0.0-win-x64.zip
```

随后打开 GitHub 仓库网页：

1. 点击 `Releases → Create a new release`。
2. 创建标签 `v3.0.0`。
3. 标题填写 `Codex Relay 3.0.0`。
4. 上传 `codex-relay-3.0.0-win-x64.zip`。
5. 点击 `Publish release`。

`artifacts/` 和 ZIP 已被 `.gitignore` 排除，只作为 Release 附件上传。
