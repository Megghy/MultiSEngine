# Configuration

## Runtime files

MultiSEngine uses a small set of files in its working directory:

- `Config.json`: runtime configuration
- `MOTD.txt`: text template for the proxy MOTD
- `Plugins/`: plugin DLL drop folder

## Root settings

| Setting | Type | Meaning |
| --- | --- | --- |
| `ListenIP` | `string` | Bind address for the proxy listener. |
| `ListenPort` | `int` | Bind port for the proxy listener. |
| `ServerName` | `string` | Public-facing proxy name shown to players and logs. |
| `ServerVersion` | `int` | Default Terraria protocol version number used by the proxy. `319` maps to `v1.4.5.6`. |
| `SwitchTimeOut` | `int` | Transfer timeout in milliseconds. |
| `EnableCrossplayFeature` | `bool` | Allows joining even when the player version differs from `ServerVersion`, instead of hard-rejecting version mismatch. |
| `EnableChatForward` | `bool` | Forwards normal chat across the proxy flow. |
| `ChatFormat` | `string` | Chat forwarding template. Supported placeholders are `{servername}`, `{username}`, and `{message}`. |
| `SwitchToDefaultServerOnJoin` | `bool` | Sends newly joined players to the configured default server automatically. |
| `RestoreDataWhenJoinNonSSC` | `bool` | Restores player data when joining a non-SSC backend server. |
| `DisableTcpDelayWhenPipeline` | `bool` | Disables Nagle-style delay in the send pipeline. Useful for lower-latency packet forwarding. |
| `UseCrowdControlled` | `bool` | Enables the alternate pre-connect branch used by the current transfer pipeline. |
| `DefaultServer` | `string` | Logical name of the backend server treated as the default target. |
| `Servers` | `array` | List of backend Terraria servers. |

## Backend server entry

Each item in `Servers` has this shape:

| Field | Type | Meaning |
| --- | --- | --- |
| `Visible` | `bool` | If `false`, the server is hidden from MOTD placeholders and `/mse list`, but it still exists as a target. |
| `Name` | `string` | Canonical backend name. This must be unique and non-empty. |
| `ShortName` | `string` | Optional alias accepted by the server lookup logic. |
| `IP` | `string` | Target host name or IP address. |
| `Port` | `int` | Target Terraria server port. |
| `SpawnX` | `short` | Optional forced spawn X coordinate. Use `-1` to use the backend world spawn. |
| `SpawnY` | `short` | Optional forced spawn Y coordinate. Use `-1` to use the backend world spawn. |
| `VersionNum` | `int` | Optional per-server Terraria version override. Use `-1` to fall back to the global version logic. |

## Validation rules on load

When `Config.json` is loaded:

- Duplicate backend names are reduced to a single entry.
- Backend entries with empty names are removed.
- The file is rewritten in normalized JSON format on first successful load.
- Unknown version numbers are logged as warnings.

## Configuration behavior notes

- `DefaultServer` is resolved by exact backend `Name`.
- Server matching for commands also accepts prefix matches, substring matches, and `ShortName`.
- Forced spawn coordinates are validated against backend world bounds during transfer. Invalid coordinates are adjusted and logged.
- If `VersionNum` is set on a backend server, adapters prefer it over the global `ServerVersion`.

## MOTD placeholders

`MOTD.txt` supports these placeholders:

- `{online}`
- `{name}`
- `{players}`
- `{servers}`

`{servers}` only includes backends where `Visible` is `true`.

## Reload behavior

Use the console command `reload` to reload:

- `Config.json`
- localization resources

Plugin state is not reloaded by `reload`. Use `reloadplugin` for extension reloads.

Next page: [Commands](Commands.md)
