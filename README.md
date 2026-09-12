# PeekShield 窥屿盾

[![GitHub](https://img.shields.io/badge/GitHub-yty16%2FPeekShield-blue?logo=github)](https://github.com/yty16/PeekShield)
[![Stars](https://img.shields.io/github/stars/yty16/PeekShield)](https://github.com/yty16/PeekShield/stargazers)
[![Downloads](https://img.shields.io/github/downloads/yty16/PeekShield/total)](https://github.com/yty16/PeekShield/releases)
[![Latest Release](https://img.shields.io/github/v/release/yty16/PeekShield)](https://github.com/yty16/PeekShield/releases)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](https://github.com/yty16/PeekShield/blob/main/LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/yty16/PeekShield)

完全独立的本地离线隐私防窥工具，支持 Windows / macOS / Linux。双击即可启动，自带完整设置窗口、系统托盘、开机自启与全部核心能力。

## 核心能力
- 本地人脸录入（摄像头实时录入 / 上传照片录入），离线 dlib ResNet-34 128 维特征比对，数据不出本机。
- 摄像头实时侦测"陌生人窥视"：检测人脸、判断是否属于机主、是否正对屏幕。
- 屏幕雾化遮罩 / 全屏置顶保护 / 弹窗提示 / 提醒音 / 最小化受保护程序。
- 受保护程序与窗口列表（仅当这些程序前台时触发防护）。
- 系统托盘、开机自启、快捷键一键暂停、手动防窥模式。

## 密码保护
PeekShield 内置可选的密码保护，防止他人在您离开时擅自打开或更改设置、退出或卸载应用。
- **自定义密码**：您可设置自己的密码。
- **保密问题（可选）**：可设置保密问题与答案。忘记密码时，验证保密问题即可自助清除密码保护并重新设置。
- **保护范围可单独开关**：退出应用、卸载应用、打开主页面、打开安全设置，四项均可独立启用或关闭。
- **自动重新锁定**：关闭主页面或安全设置页后会立即重新锁定，再次打开需重新验证。
- **卸载保护**：若启用"卸载应用"保护范围，卸载时安装程序会调起本应用进行密码验证。
- 软件内置一个备用恢复口令机制，作为密码保护的应急通道：当您忘记自己设置的密码时，备用恢复口令可用于解锁、关闭密码保护或重置密码；其权限高于您设置的用户密码，始终可用。出于安全考虑此密码不会公开，请务必牢记您自己设置的密码，并建议设置保密问题；备用恢复口令仅作最后兜底使用。

## 技术栈
- .NET 8 + Avalonia 11（跨平台 UI）
- OpenCvSharp4（摄像头）
- DlibDotNet（dlib 离线人脸识别）
- 模型文件：`shape_predictor_68_face_landmarks.dat`、`dlib_face_recognition_resnet_model_v1.dat`，放在 `PeekShield/Assets/Models/`

## 构建与发布
模型文件较大（约 120MB），请先放入 `PeekShield/Assets/Models/`，再构建。

```bash
dotnet restore
dotnet build -c Release
```

### 按平台发布（自包含，双击即用）
```bash
# Windows
dotnet publish -c Release -r win-x64 -o dist/win-x64 --self-contained true -p:PublishReadyToRun=false
# macOS (Intel)
dotnet publish -c Release -r osx-x64 -o dist/osx-x64 --self-contained true -p:PublishReadyToRun=false
# macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 -o dist/osx-arm64 --self-contained true -p:PublishReadyToRun=false
# Linux
dotnet publish -c Release -r linux-x64 -o dist/linux-x64 --self-contained true -p:PublishReadyToRun=false
```

## 平台说明
- **Windows**：所有功能完整可用（前台窗口检测、最小化受保护程序、全局快捷键、托盘气球、开机自启注册表）。
- **macOS / Linux**：核心防护（人脸录入、侦测、雾化/全屏遮罩、托盘、弹窗、提醒音）可用；前台窗口/进程级联动（受保护程序最小化、全局快捷键）依赖系统辅助功能/窗口管理接口，当前为后台常驻监控模式，后续版本补齐。

## 相关链接
- **介绍视频**：https://www.bilibili.com/video/BV1ePbj61ECS?vd_source=c8aad3a4da99b45eee78953ce384ce1d
- **GitHub 仓库**：https://github.com/yty16/PeekShield
- **gitee 仓库**：https://gitee.com/yty16/PeekShield
- **网盘下载链接**：https://1847400086.share.123pan.cn/123pan/OE2vvd-rGFIh?pwd=53Bj# 提取码：53Bj

## 许可
本软件以 GNU 通用公共许可证 v3.0（GPL-3.0）发布，详见 `LICENSE`。您可以自由使用、研究、修改与再分发本软件（含商业用途）；衍生作品须以相同许可证（GPL-3.0）开源。隐私政策见 `PRIVACY.md`（全本地处理，数据不出本机）。
使用前请先在应用内同意隐私告知
