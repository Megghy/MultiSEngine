# Quick Start

## Prerequisites

- Install the **.NET 10** runtime or SDK.
- Make sure your backend Terraria servers (`v1.4.6.1` / protocol `329`, or any crossplay-compatible backend) are reachable from the machine running MultiSEngine.
- If building from source, clone this repository with submodules (`git clone --recursive`).

## Run from source

```bash
dotnet run -c Release --project src/MultiSEngine/MultiSEngine.csproj
```

## Publish a single-file binary

```bash
dotnet publish src/MultiSEngine/MultiSEngine.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```

Replace `win-x64` with `linux-x64` or `linux-arm64` as needed. On Linux without single-file packaging, run `dotnet MultiSEngine.dll`.

## First launch behavior

On first launch, MultiSEngine ensures the following working-directory files exist:

- `Config.json` (generated with a default `lobby` backend entry if missing, written atomically)
- `MOTD.txt`
- `Plugins/`
- `Logs/`

## Minimal configuration

```json
{
  "ListenIP": "0.0.0.0",
  "ListenPort": 7778,
  "ServerName": "MultiSEngine",
  "ServerVersion": 329,
  "SwitchTimeoutMs": 10000,
  "PasswordTimeoutMs": 60000,
  "HandshakeTimeoutMs": 15000,
  "EnableCrossplay": true,
  "EnableChatForward": true,
  "ChatFormat": "[{servername}] {username}: {message}",
  "SwitchToDefaultServerOnJoin": false,
  "RestoreDataWhenJoinNonSsc": true,
  "DisableTcpDelay": true,
  "UseCrowdControlled": false,
  "DefaultServer": "lobby",
  "Limits": {
    "MaxConnections": 256,
    "SendPipePauseThresholdBytes": 524288,
    "SendPipeResumeThresholdBytes": 262144,
    "SendFlushTimeoutMs": 10000,
    "MaxPreConnectBufferedBytes": 8388608,
    "MaxHandshakePacketsPerSecond": 256,
    "SessionMailboxCapacity": 256
  },
  "CustomPackets": {
    "GlobalSharedSecret": "",
    "MaxClockSkewSeconds": 30,
    "AllowUnsignedInDevelopment": false
  },
  "Servers": [
    {
      "Name": "lobby",
      "ShortName": "l",
      "Host": "127.0.0.1",
      "Port": 7777,
      "VersionNum": -1,
      "Visible": true,
      "SpawnX": -1,
      "SpawnY": -1,
      "SharedSecret": null,
      "AllowClusterCommands": false
    }
  ]
}
```

## Basic bring-up checklist

1. Configure `ListenIP`, `ListenPort`, and at least one backend in `Servers`.
2. Set `ServerVersion` to `329` for Terraria `v1.4.6.1`.
3. If using `MSE2` custom packets (e.g. `SyncIP` with `MultiSCore`), set `CustomPackets.GlobalSharedSecret` or per-server `SharedSecret`.
4. Start MultiSEngine and run `test all` in the console to verify backend connectivity concurrently.
5. Join the proxy from a Terraria client and run `/mse list` or `/mse tp <server>`.

Next page: [Configuration](Configuration.md)
