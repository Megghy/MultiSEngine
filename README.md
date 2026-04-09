# MultiSEngine
一个独立于泰拉瑞亚服务器的代理服务, 用于进行跨服传送, 基于.Net 9

A Terraria server-independent proxy service for cross-server transfers, based on .Net 9

## Notice

> 我已经不再推荐使用这个项目. 如果你的技术力足够, 我更推荐使用 [CedaryCat/UnifierTSL](https://github.com/CedaryCat/UnifierTSL) 来获得更接近 Minecraft Dimensions 的游戏体验.
> 它使用了比较 hacky 的方式实现单进程内的多世界系统, 不再需要启动多个实例, 并且自带了一个魔改版本的 TShock.
> 唯一明显的缺点是不再兼容现有的 TShock 插件, 如果想实现额外功能, 通常需要自行编写插件.

> I no longer recommend this project for new setups. If you have enough technical ability, I recommend [CedaryCat/UnifierTSL](https://github.com/CedaryCat/UnifierTSL) instead for a gameplay model that is much closer to Minecraft Dimensions.
> It uses a fairly hacky single-process multi-world design, so you no longer need to run multiple server instances, and it ships with a heavily modified TShock build.
> The main downside is that it is no longer compatible with existing TShock plugins, so any extra functionality usually needs to be implemented through custom plugins.

[![CI](https://github.com/Megghy/MultiSEngine/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Megghy/MultiSEngine/actions/workflows/dotnet.yml)
## 独特功能 Unique features
* 引用豚豚的协议库
* 自带的插件系统, 配合协议库能够极为简单快速地修改玩家数据包以及进行拓展和开发
* 可自行修改的语言文件
* 开箱即用的部署方式(装个.Net 9也没什么麻烦的!)和实用的配置项

---

* Reference to CedaryCat's protocol library
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
* 装[.Net 9](https://dotnet.microsoft.com/download/dotnet/9.0)
* 双击启动, 完事. 对于Linux系统则使用 ``dotnet MultiSEngine.dll``

稍微具体点的部署方式和配置文件说明:https://www.yuque.com/megghy/multisengine

---

* Install [Net 9](https://dotnet.microsoft.com/download/dotnet/9.0)
* Double-click to start, and you're done. For Linux systems use ``dotnet MultiSEngine.dll``.

Slightly more specific deployment methods and configuration file instructions:https://www.yuque.com/megghy/multisengine

## 发布地址 Release address
[BBSTR](https://www.bbstr.net/r/93/)

[Github Action](https://github.com/Megghy/MultiSEngine/actions)

## 引用项目 Reference project
[TrProtocol](https://github.com/CedaryCat/TrProtocol)

## Benchmark
This repository includes a simple BenchmarkDotNet benchmark project:

```bash
dotnet run -c Release --project tests/MultiSEngine.Benchmarks/MultiSEngine.Benchmarks.csproj -- --filter *
dotnet run -c Release --project tests/MultiSEngine.Benchmarks/MultiSEngine.Benchmarks.csproj -- --load-report
```

Environment:
* CPU: `AMD Ryzen 9 9950X`
* Runtime: `.NET 9.0.14`
* OS: `Windows 11 25H2`

`player-sync` means one full synchronization pass for a single player, combining multiple low-level Terraria protocol packets, including:
- `WorldData`
- `PlayerInfo`
- `PlayerHealth`
- `PlayerMana`
- `Loadout`
- a sequence of `Equipment` packets
- one more `WorldData`

| Scenario | Result | Approx. throughput | Allocated |
|---|---:|---:|---:|
| One-way forwarding: Server -> Client (`512` packets/batch) | `115.0 us/batch` | `~4.4511 M packets/s` | `48.8 KB/batch` |
| One-way forwarding: Client -> Server (`512` packets/batch) | `114.3 us/batch` | `~4.4802 M packets/s` | `48.8 KB/batch` |

| Sustained multi-player / multi-server sync load (`10s/scenario`) | Player syncs/s | Low-level Terraria packets/s | Avg CPU | Peak working set | Peak managed heap |
|---|---:|---:|---:|---:|---:|
| `16` players / `1` server | `31198` | `11106568` | `2.22 cores (7.0%)` | `58.9 MiB` | `4.2 MiB` |
| `16` players / `4` servers | `32394` | `11532378` | `2.28 cores (7.1%)` | `62.2 MiB` | `4.5 MiB` |
| `64` players / `1` server | `32781` | `11670166` | `2.23 cores (7.0%)` | `73.2 MiB` | `12.0 MiB` |
| `64` players / `4` servers | `32955` | `11732143` | `2.27 cores (7.1%)` | `71.2 MiB` | `12.1 MiB` |
