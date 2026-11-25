# MultiSEngine

MultiSEngine v2 is a **.NET 10** standalone proxy layer for **Terraria v1.4.6.1 (protocol `329`)** servers. It accepts player connections, isolates each session inside a single-connection actor, and transfers players across backend servers with zero packet loss and O(1) broadcast fast-path forwarding.

This wiki reflects the v2 architecture: startup, configuration, commands, four-layer architecture, collectible plugin hot-reload, and HMAC-authenticated `MSE2` custom packets.

## Start here

- [Quick Start](Quick-Start.md)
- [Configuration](Configuration.md)
- [Commands](Commands.md)
- [Architecture](Architecture.md)
- [Plugin Development](Plugin-Development.md)
- [Hooks and Custom Packets](Hooks-and-Custom-Packets.md)

## Core ideas

- **Target runtime & protocol**: `.NET 10` (`net10.0`), Terraria `v1.4.6.1` (`ServerVersion = 329`, with crossplay support down to `269`).
- **Four-layer Clean Architecture**: `MultiSEngine.Core` -> `MultiSEngine.Protocol` -> `MultiSEngine.Runtime` -> `MultiSEngine` (CLI).
- **Zero-copy bounded framing**: `FramedConnection` enforces strict frame validation (`length >= 3`), bounded `System.IO.Pipelines` backpressure with flush timeouts, and `PauseReading`/`ResumeReading` gates for lossless server transfers.
- **Single-connection actor**: `PlayerSessionActor` owns a single discriminated-union `SessionState` and serializes lifecycle, transfer, password prompt, and disconnect transitions without locks or re-entrancy deadlocks.
- **Collectible plugin sandbox**: Plugins implement `IMsePlugin`, register managed subscriptions via `IPluginContext`, load from memory streams, and truly unload via collectible `AssemblyLoadContext`.
- **Authenticated cluster packets**: `MSE2` custom packets over `Unused15` use HMAC-SHA256 signatures, clock-skew checks, and a replay-protection window.

## Repository map

- `src/MultiSEngine.Core`: immutable config records, `SessionState` discriminated union, `ISessionHandle`, `IMsePlugin`, `IPluginContext`, `HookEvents`, and command contracts
- `src/MultiSEngine.Protocol`: `FramedConnection`, `PacketFrameCodec`, `FastPathClassifier`, `PlayerStateTracker`, `LobbyProtocol`, `TerrariaSyncEngine`, `TerrariaVersions`, and `CustomPacketCodec` (`MSE2`)
- `src/MultiSEngine.Runtime`: `MultiSEngineHost`, `PlayerSessionActor`, `TransferWorkflow`, `ServerProbeService`, `PluginHost`, `HookBus`, `CommandRouter`, `BuiltInCommands`, `ConfigStore`, and `AsyncProxyLogger`
- `src/MultiSEngine`: thin CLI entrypoint with signal handling and non-interactive daemon support
- `src/MultiSEngine.TShock`: official TShock 6 (`net9.0`) companion plugin sharing the `MSE2` codec for real IP restoration, IP ban enforcement, and backend-initiated server switches/custom packets
- `tests/`: parallel unit tests (`MultiSEngine.Tests`), real-TCP end-to-end tests (`MultiSEngine.IntegrationTests`), reloadable sample plugin (`MultiSEngine.TestPlugin`), and production-path benchmarks (`MultiSEngine.Benchmarks`)
- `external/TrProtocol`: Terraria 1.4.6.1 (`329`) binary protocol library
