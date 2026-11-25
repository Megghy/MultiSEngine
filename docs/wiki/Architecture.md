# Architecture

MultiSEngine v2 is organized into four strict, acyclic layers targeting `.NET 10` (`net10.0`) with zero global static runtime singletons.

## Layer dependency graph

```text
MultiSEngine (CLI)
  └── MultiSEngine.Runtime (Host, Actor sessions, TransferWorkflow, PluginHost, Commands, Probing)
        ├── MultiSEngine.Protocol (FramedConnection, FastPathClassifier, SyncEngine, Lobby, MSE2)
        │     ├── MultiSEngine.Core (Contracts, Immutable Config, SessionState union, Hooks)
        │     └── TrProtocol (Terraria 1.4.6.1 / protocol 329 codec)
        └── MultiSEngine.Core
```

## Core runtime subsystems

### 1. Bounded zero-copy framing (`FramedConnection`)
- Built on `System.IO.Pipelines` with a single-writer outbound pipe (`SendPipePauseThresholdBytes` / `SendPipeResumeThresholdBytes`) and `SendFlushTimeout`.
- Uses a zero-allocation synchronous fast path for uncontended, non-backpressured writes (`~1 B/packet`, `0 Gen0 GC` over 512-packet TCP bursts), only allocating a linked timeout `CancellationTokenSource` when write contention or slow-peer backpressure actually occurs.
- Validates every incoming frame header (`length >= 3`) before dispatch to prevent out-of-bounds DoS crashes.
- Uses `AsyncLocal<FramedConnection?>` to detect self-disposal inside the read loop and prevent shutdown deadlocks.
- Exposes `PauseReading()` / `ResumeReading()` at exact Terraria frame boundaries (with a lock-free fast check when unpaused) for zero-packet-loss handoff during server switches.

### 2. O(1) fast-path classification (`FastPathClassifier`)
- Inspects only raw frame bytes (`Span[2]` message ID and `Span[3]` player slot).
- High-frequency broadcast packets for **other** players (`PlayerControls`, `PlayerHealth`, `SyncEquipment`, `PlayerBuffs`, etc.) bypass deserialization and stream directly to the peer unless a plugin registers middleware for that `MessageID`.
- Strictly isolates `PlayerStateTracker` updates to the session's own active `PlayerSlot` and permanently locks `Name` and `ClientUUID` after handshake.

### 3. Single-connection actor (`PlayerSessionActor`)
- Each client socket is owned by one `PlayerSessionActor` with a bounded `Channel<SessionMessage>` mailbox.
- Replaces split state flags with a single discriminated union (`SessionState`):
  - `Connecting` -> `Lobby` -> `Switching` -> `InServer` -> `ReturningToLobby` -> `Closed`
- Uses `AsyncLocal<PlayerSessionActor?>` so hook callbacks or `/mse` commands running inline on the actor never deadlock when interacting with `ISessionHandle`.
- Explicitly tracks disconnect origin (`Client` vs `Upstream(linkId)`) so abrupt client disconnects always tear down the session with zero leaked entries.

### 4. Atomic transfer workflow (`TransferWorkflow`)
- Opens an independent upstream `FramedConnection` to the target backend without detaching the client from its current server/lobby.
- Handles `RequestPassword` asynchronously without blocking the client read loop.
- Replays `SyncPlayer`, `ClientUUID`, `PlayerPlatformInfo` (on Terraria `>= 329` backends), and signed `MSE2` `SyncIP` packets.
- Buffers post-`WorldData` world/chunk packets up to `MaxPreConnectBufferedBytes`, pauses upstream reading at `FinishedConnectingToServer`, emits a minimal `TerrariaSyncEngine` batch (1 `WorldData` for SSC targets, 2 for non-SSC restore; all slots rewritten), swaps active upstream ownership, and resumes reading with zero packet loss.
- Fallback returns clear `PreviousServer` to prevent `A <-> B` ping-pong kick loops.

### 5. Concurrent backend probing & host lifecycle
- `ServerProbeService` runs independent lightweight TCP handshakes in parallel (`test all`).
- `MultiSEngineHost` is fully instantiable (enabling parallel real-TCP integration tests) and shuts down gracefully on `SIGINT`/`SIGTERM` or EOF in headless container environments.

Next page: [Plugin Development](Plugin-Development.md)
