# Hooks and Custom Packets

## Lifecycle hooks

Plugins subscribe to async hooks via `IPluginContext`. Subscriber exceptions are isolated so one faulty plugin cannot break other subscribers or crash the proxy.

| Registration | Event type | When it fires |
| --- | --- | --- |
| `context.OnPlayerJoin` | `PlayerJoinEvent` | When a client completes initial `ClientHello` validation. Set `Handled = true` to reject the join. |
| `context.OnPlayerLeave` | `PlayerLeaveEvent` | When a session closes and cleans up. |
| `context.OnPreSwitch` | `PreSwitchEvent` | Before an upstream pre-connect starts. Set `Handled = true` to cancel the transfer. |
| `context.OnPostSwitch` | `PostSwitchEvent` | Only **after** the sync batch succeeds and upstream ownership has atomically switched. |
| `context.OnChat` | `ChatEvent` | When player chat arrives. Set `Handled = true` to suppress forwarding. |

## Packet middleware

Use `context.RegisterPacketMiddleware(direction, messageIds, middleware)` to intercept specific `MessageID` frames on `ClientToServer`, `ServerToClient`, or `Both`.

- Subscribing only to the exact `MessageID`s you need keeps all other packets on the zero-copy O(1) fast path.
- Middleware returns:
  - `PacketMiddlewareResult.Continue` — forward unmodified
  - `PacketMiddlewareResult.Drop` — suppress the frame
  - `PacketMiddlewareResult.Replace(ReadOnlyMemory<byte>)` — replace the wire frame

## `MSE2` authenticated custom packets

MultiSEngine v2 replaces the unauthenticated legacy custom-data format with **`MSE2`**, transported inside Terraria `MessageID.Unused15` (`15`).

### Security rules
- **Client -> Server `Unused15` is dropped by default** so clients can never forge cluster packets.
- Every `MSE2` packet carries a **32-byte HMAC-SHA256** tag verified in constant time, a UTC millisecond timestamp checked against `CustomPackets.MaxClockSkewSeconds` (default `30s`), and an 8-byte random nonce tracked in a replay-protection sliding window.
- Secret resolution uses `ServerDescriptor.SharedSecret` if configured, falling back to `CustomPackets.GlobalSharedSecret`.

### Wire layout (`Unused15` payload)

| Field | Size | Description |
| --- | --- | --- |
| Terraria header | 3 bytes | `ushort Length` (LE) + `byte 15` (`MessageID.Unused15`) |
| Magic | 4 bytes | ASCII `"MSE2"` (`0x4D 0x53 0x45 0x32`) |
| Version | 1 byte | `0x02` |
| Type length + UTF-8 Type | 1 + N bytes | Packet type name (e.g. `mse.sync_ip`) |
| TimestampMs | 8 bytes | Int64 little-endian UTC Unix milliseconds |
| Nonce | 8 bytes | Cryptographically random 64-bit nonce |
| Payload length + Payload | 4 + M bytes | Int32 little-endian length + binary payload |
| Signature | 32 bytes | `HMAC-SHA256(secret, Magic..Payload)` |

### Built-in `MSE2` packet types

| Type | Direction | Purpose |
| --- | --- | --- |
| `mse.sync_ip` | Proxy -> Backend | Sends the player's real client IP address (`string playerName`, `string clientIp`) during handshake. |
| `mse.execute_command` | Backend -> Proxy | Requests a proxy command on a session (`string targetPlayerName`, `string commandLine`). Cross-player execution is denied unless the source server has `AllowClusterCommands = true`. |
| `mse.switch_server` | Backend -> Proxy | Requests transferring the player to a named target server (`string targetServerName`). |

### Handling custom packet types in a plugin

```csharp
context.RegisterCustomPacketHandler("myplugin.ping", async (packetCtx, ct) =>
{
    context.Logger.Info(
        $"Verified packet from {packetCtx.SourceServer.Name} for {packetCtx.Session.PlayerName} ({packetCtx.Payload.Length} bytes).",
        "MyPlugin");
    await ValueTask.CompletedTask;
});
```
