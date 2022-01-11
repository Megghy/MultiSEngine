# MultiSEngine
一个独立于泰拉瑞亚服务器的代理服务, 用于进行跨服传送, 基于.Net 6

A Telarea server-independent proxy service for cross-server transfers, based on .Net6

[![CI](https://github.com/Megghy/MultiSEngine/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Megghy/MultiSEngine/actions/workflows/dotnet.yml)
## 独特功能 Unique features
* 引用豚豚的协议库
* 自带的插件系统, 配合协议库能够极为简单快速地修改玩家数据包以及进行拓展和开发
* 可自行修改的语言文件
* 开箱即用的部署方式(装个.Net 6也没什么麻烦的!)和实用的配置项

---

* Reference to Dolphin's protocol library
* Self-contained plug-in system, with protocol library for easy and fast modification of player packets and expansion and development
* Customizable language files
* Out-of-the-box deployment and useful configuration items

## 关于单文件和CI About single file and CI
* 自动化构建出的为单文件程序, 即整个项目只有一个文件, Release则为普通项目, 如果需要的话可以只从[CI](https://github.com/Megghy/MultiSEngine/actions)下载
* 目前只会构建Windows, Linux64, Arm64平台的文件, 如要在其他平台运行请自行构建 (dotnet publish -c Release -r ([目标平台](https://docs.microsoft.com/zh-cn/dotnet/core/rid-catalog)) -p:PublishSingleFile=true --self-contained false

---
* Automated builds are single-file programs, i.e. the entire project has only one file, and releases are normal projects, which can be downloaded from [Action](https://github.com/Megghy/MultiSEngine/actions) only if needed.
* If you want to run on other platforms, please build it yourself (dotnet publish -c Release -r ([Target platform](https://docs.microsoft.com/zh-cn/dotnet/core/rid-catalog)) -p:PublishSingleFile=true --self-contained false

## 简易使用说明 Use instructions
* 装[.Net 6](https://dotnet.microsoft.com/download/dotnet/6.0)
* 双击启动, 完事. 对于Linux系统则使用 ``dotnet MultiSEngine.dll``

稍微具体点的部署方式和配置文件说明:https://www.yuque.com/minato-qbli7/multisengine

---

* Install [Net 6](https://dotnet.microsoft.com/download/dotnet/6.0)
* Double-click to start, and you're done. For Linux systems use ``dotnet MultiSEngine.dll``.

Slightly more specific deployment methods and configuration file instructions:https://www.yuque.com/minato-qbli7/multisengine

## 发布地址 Release address
[BBSTR](https://www.bbstr.net/r/93/)

[Github Action](https://github.com/Megghy/MultiSEngine/actions)

## 引用项目 Reference project
[TrProtocol](https://github.com/chi-rei-den/TrProtocol/tree/dev)
