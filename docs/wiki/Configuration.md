# Configuration

MultiSEngine v2 stores configuration as an immutable `ProxyConfig` snapshot (`MultiSEngine.Core.Configuration.ProxyConfig`) loaded and atomically saved by `ConfigStore`.

## Runtime files

- `Config.json`: proxy, limits, security, and backend server definitions
- `MOTD.txt`: lobby MOTD template
- `Plugins/`: plugin DLL drop folder
- `Logs/`: daily async log files

## Root settings

| Setting | Type | Default | Meaning |
| --- | --- | --- | --- |
| `ListenIP` | `string` | `"0.0.0.0"` | Bind IP address (`localhost` binds strictly to `127.0.0.1`). |
| `ListenPort` | `int` | `7778` | TCP listener port. |
| `ServerName` | `string` | `"MultiSEngine"` | Proxy display name used in Lobby world and MOTD. |
| `ServerVersion` | `int` | `329` | Default Terraria protocol number (`329` = `v1.4.6.1`, `326` = `v1.4.5.8`, `319` = `v1.4.5.6`). |
| `SwitchTimeoutMs` | `int` | `10000` | Backend pre-connect timeout in milliseconds. |
| `PasswordTimeoutMs` | `int` | `60000` | Timeout for entering a backend password via `/mse pass`. |
| `HandshakeTimeoutMs` | `int` | `15000` | Maximum duration allowed for initial client lobby handshake. |
| `EnableCrossplay` | `bool` | `true` | Allows clients whose protocol version differs from `ServerVersion` (above minimum `269`). |
| `EnableChatForward` | `bool` | `true` | Forwards chat across backend servers using `ChatFormat`. |
| `ChatFormat` | `string` | `"[{servername}] {username}: {message}"` | Cross-server chat template. |
| `SwitchToDefaultServerOnJoin` | `bool` | `false` | Automatically transfers newly joined players to `DefaultServer`. |
| `RestoreDataWhenJoinNonSsc` | `bool` | `true` | Restores origin character data when joining a non-SSC backend server. |
| `DisableTcpDelay` | `bool` | `true` | Enables `TCP_NODELAY` on client and upstream sockets. |
| `UseCrowdControlled` | `bool` | `false` | Applies a brief crowd-control buff during server transfer handshakes. |
| `DefaultServer` | `string` | `"lobby"` | Logical `Name` of the default fallback backend server. |
| `Limits` | `object` | see below | Connection, backpressure, and buffer safety limits. |
| `CustomPackets` | `object` | see below | `MSE2` HMAC-SHA256 authentication settings. |
| `Servers` | `array` | `[lobby]` | Backend Terraria server descriptors. |

## `Limits` settings

| Field | Default | Meaning |
| --- | --- | --- |
| `MaxConnections` | `256` | Maximum concurrent client connections accepted by the listener. |
| `SendPipePauseThresholdBytes` | `524288` | Outbound pipe backpressure pause threshold (512 KiB). |
| `SendPipeResumeThresholdBytes` | `262144` | Outbound pipe resume threshold (256 KiB). |
| `SendFlushTimeoutMs` | `10000` | Slow-peer flush timeout before disconnecting the stalled connection. |
| `MaxPreConnectBufferedBytes` | `8388608` | Maximum world/chunk bytes buffered during a single pre-connect handshake (8 MiB). |
| `MaxHandshakePacketsPerSecond` | `256` | Per-connection rate limit during pre-lobby handshake. |
| `SessionMailboxCapacity` | `256` | Bounded actor mailbox capacity per session. |

## `CustomPackets` settings

| Field | Default | Meaning |
| --- | --- | --- |
| `GlobalSharedSecret` | `""` | Default HMAC-SHA256 secret shared with trusted backend servers. |
| `MaxClockSkewSeconds` | `30` | Allowed timestamp drift for `MSE2` packets before replay rejection. |
| `AllowUnsignedInDevelopment` | `false` | Permits unsigned `MSE2` packets for local development only (keep `false` in production). |

## Backend server entry (`Servers[]`)

| Field | Type | Meaning |
| --- | --- | --- |
| `Name` | `string` | Unique canonical backend name. |
| `ShortName` | `string` | Optional short alias for `/mse tp` lookup. |
| `Host` | `string` | Backend hostname or IP address. |
| `Port` | `int` | Backend TCP port. |
| `VersionNum` | `int` | Optional per-server Terraria protocol override (`-1` inherits client/global version). |
| `Visible` | `bool` | Controls visibility in `{servers}` MOTD placeholder and `/mse list`. |
| `SpawnX` / `SpawnY` | `short` | Optional forced spawn tile coordinates (`-1` uses backend world spawn). |
| `SharedSecret` | `string?` | Optional per-server HMAC secret overriding `CustomPackets.GlobalSharedSecret`. |
| `AllowClusterCommands` | `bool` | Must be `true` to allow `mse.execute_command` from this backend to control other players' sessions. |

## Lookup priority & validation

- Duplicate or empty server names are normalized out on load.
- `ProxyConfig.FindServer(query)` resolves in strict order: **exact `Name`** -> **exact `ShortName`** -> **prefix match** -> **substring match**.
- `MOTD.txt` supports `{online}`, `{name}`, `{players}`, and `{servers}` (visible servers only).
- Run `reload` in the console to atomically swap `Config.json` and localization; run `reloadplugin` to hot-reload plugins.

Next page: [Commands](Commands.md)
