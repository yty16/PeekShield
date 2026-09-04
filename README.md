# PeekShield（窥屿盾）

[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](./LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)]()

我自己的防偷窥小工具。摄像头盯着，发现屏幕前出现了陌生人就自动雾化 + 弹窗。所有识别都在本地跑（dlib ResNet + 128 维特征），脸不传到任何地方。

## 怎么用

下载 Release 里对应平台的 zip，解压双击就起来了。第一次打开会让你录入一下人脸（对着摄像头拍一张就行），然后就一直后台挂着。

设置界面能调：
- 触发动作：屏幕雾化 / 弹窗 / 声音 / 把前台那个隐私软件最小化
- 受保护进程/窗口（默认空，自己加）
- 灵敏度三档（从激进到保守）

## 实际情况

目前只在我自己 Windows 笔记本上测过，macOS / Linux 理论上能跑但没仔细调。有问题提 Issue，有空就改。

依赖两个 dlib 模型（`shape_predictor_68_face_landmarks.dat` + `dlib_face_recognition_resnet_model_v1.dat`），大概 100MB+，仓库 .gitignore 里写了不提交。Release 资产里有下，丢到 `Models/` 文件夹里。

## 自己编译

```bash
dotnet restore
dotnet build -c Release
```

发布自包含包：

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o dist/win
dotnet publish -c Release -r linux-x64 --self-contained true -o dist/linux
dotnet publish -c Release -r osx-arm64 --self-contained true -o dist/osx-arm
```

## 隐私

全本地，详情见 [PRIVACY.md](./PRIVACY.md)。简单说：人脸特征存本地文件夹（`%LocalAppData%/PeekShield/enrollment/`），相机帧不落盘除非你自己开"截图存证"。可以随时撤回同意。

## 许可

GPL-3.0。
