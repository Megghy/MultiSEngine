# MultiSEngine

一个独立于泰拉瑞亚服务器的跨服代理服务，基于 **.NET 10**，适配 **Terraria v1.4.6.1 (协议号 `329`)**。

A Terraria server-independent cross-server proxy service built on **.NET 10**, targeting **Terraria v1.4.6.1 (protocol `329`)**.

## Notice

> 我已经不再推荐使用这个项目. 如果你的技术力足够, 我更推荐使用 [CedaryCat/UnifierTSL](https://github.com/CedaryCat/UnifierTSL) 来获得更接近 Minecraft Dimensions 的游戏体验.
> 它使用了比较 hacky 的方式实现单进程内的多世界系统, 不再需要启动多个实例, 并且自带了一个魔改版本的 TShock.
> 唯一明显的缺点是不再兼容现有的 TShock 插件, 如果想实现额外功能, 通常需要自行编写插件.

> I no longer recommend this project for new setups. If you have enough technical ability, I recommend [CedaryCat/UnifierTSL](https://github.com/CedaryCat/UnifierTSL) instead for a gameplay model that is much closer to Minecraft Dimensions.
> It uses a fairly hacky single-process multi-world design, so you no longer need to run multiple server instances, and it ships with a heavily modified TShock build.
> The main downside is that it is no longer compatible with existing TShock plugins, so any extra functionality usually needs to be implemented through custom plugins.

[![CI](https://github.com/Megghy/MultiSEngine/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Megghy/MultiSEngine/actions/workflows/dotnet.yml)

## 核心特性 Key Features (v2)

* 适配 **Terraria v1.4.6.1 (`329`)**（引用 [TrProtocol](https://github.com/CedaryCat/TrProtocol) 协议库），支持跨版本兼容
* 四层清晰架构（`Core / Protocol / Runtime / CLI`），单连接 Actor 会话引擎与零丢包闸门切服
* 基于 `System.IO.Pipelines` 的零拷贝有界帧管线 + `O(1)` 他人广播包快路径直通
* 可回收 `AssemblyLoadContext` 插件热重载（`IMsePlugin` + `IPluginContext` 受管注册，零文件锁与真卸载）
* 升级版 `MSE2` 集群自定义包协议（HMAC-SHA256 签名、时钟偏斜校验与防重放滑动窗口）
* 内置配套 **TShock 6 插件**（`src/MultiSEngine.TShock`，随 Release 同步发布单文件 `MultiSEngine.TShock-v*.dll`）：自动验签还原玩家真实 IP、复检 IP 封禁，并提供 `/mseswitch` 命令与 `MultiSEngineBridge` C# API
* 开箱即用的单文件部署与完整本地化支持（详情见 [Wiki](docs/wiki/Home.md)）

---

* Supports **Terraria v1.4.6.1 (`329`)** via [TrProtocol](https://github.com/CedaryCat/TrProtocol) with crossplay support
* Four-layer Clean Architecture (`Core / Protocol / Runtime / CLI`), single-connection actor sessions, and lossless gated server transfers
* Zero-copy bounded `System.IO.Pipelines` framing with O(1) broadcast fast-path forwarding
* Collectible `AssemblyLoadContext` plugin hot-reload (`IMsePlugin` + `IPluginContext` managed subscriptions, zero file locks)
* Authenticated `MSE2` cluster custom packets (HMAC-SHA256, clock-skew enforcement, and replay window)
* Official **TShock 6 companion plugin** (`src/MultiSEngine.TShock`, shipped alongside every GitHub Release as `MultiSEngine.TShock-v*.dll`) for verified real-IP restoration, IP-ban enforcement, and cross-server transfer bridging
* Single-file deployment and customizable localization (see [Wiki](docs/wiki/Home.md))

## 单文件发布与构建 Single-File Publish & Build

* 自动化构建产出 Windows (`win-x64`)、Linux (`linux-x64`, `linux-arm64`) 单文件程序，可在 [GitHub Actions](https://github.com/Megghy/MultiSEngine/actions) 下载。
* 手动构建单文件程序：

```bash
dotnet publish src/MultiSEngine/MultiSEngine.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```

* 如需其他平台，将 `win-x64` 替换为目标 [RID](https://learn.microsoft.com/dotnet/core/rid-catalog)（如 `linux-x64`、`linux-arm64`）。

## 快速启动 Quick Start

* 安装 [.NET 10 Runtime / SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
* Windows 直接运行 `MultiSEngine.exe`；Linux 运行 `./MultiSEngine` 或 `dotnet MultiSEngine.dll`
* 详细配置、命令、插件开发与 `MSE2` 协议说明见 [docs/wiki/Home.md](docs/wiki/Home.md)

## 发布地址 & 引用项目 Links & References

* [BBSTR](https://www.bbstr.net/r/93/) | [GitHub Actions](https://github.com/Megghy/MultiSEngine/actions)
* Protocol library: [TrProtocol](https://github.com/CedaryCat/TrProtocol)

## Benchmark

```bash
dotnet run -c Release --project tests/MultiSEngine.Benchmarks/MultiSEngine.Benchmarks.csproj -- --filter *
dotnet run -c Release --project tests/MultiSEngine.Benchmarks/MultiSEngine.Benchmarks.csproj -- --load-report
```

Environment:
* CPU: `AMD Ryzen 9 9950X`
* Runtime: `.NET 10.0.12` (X64 RyuJIT x86-64-v4)
* OS: `Windows 11 25H2`

### Loopback TCP Forwarding (`512` packets/batch, with `FastPathClassifier` & bounded backpressure)

| Scenario | Mean | Approx. throughput | Gen0 | Allocated |
|---|---:|---:|---:|---:|
| Server -> Client (`512` × 21B `PlayerControls`) | `165.2 us/batch` | `~3.10 M packets/s` | `0` | `521 B/batch` (~1 B/pkt) |
| Client -> Server (`512` × 12B `TileChange`) | `130.4 us/batch` | `~3.93 M packets/s` | `0` | `520 B/batch` (~1 B/pkt) |

### Production Path Microbenchmarks

| Method | Mean | Allocated | Notes |
|---|---:|---:|---|
| `ClassifyOtherPlayerBroadcast` | `10.52 ns` | `0 B` | O(1) raw-byte slot filtering; bypasses deserialization |
| `ClassifyOwnPlayerPacket` | `10.28 ns` | `0 B` | O(1) raw-byte slot inspection |
| `BuildRealTargetSwitchSyncBatch` | `4.31 us` | `18.96 KB` | Full 321-packet non-SSC switch batch (~232k switches/s per core) |
| `EncodeAndVerifyCustomPacket` | `1.57 us` | `520 B` | `MSE2` HMAC-SHA256 sign + verify + replay check |

### Realistic Mixed Load (`--load-report`, 90% other-player broadcast + 10% own-slot state tracking)

```text
[MultiSEngine v2 Load Report] Realistic 90/10 broadcast mix: 102,400 frames in 29.5 ms => 3,466,780 packets/sec | Managed Heap: 2.01 MiB
```
