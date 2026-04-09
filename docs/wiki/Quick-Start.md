# Quick Start

## Prerequisites

- Install the .NET 9 runtime or SDK.
- Make sure your backend Terraria servers are already reachable from the machine running MultiSEngine.
- If you plan to build from source, clone this repository with its submodules.

## Run from source

```bash
dotnet run --project src/MultiSEngine.csproj
```

## Run from a published build

On Windows, start the generated executable.

On Linux, run:

```bash
dotnet MultiSEngine.dll
```

## First launch behavior

On first launch, MultiSEngine creates these runtime files in the working directory if they do not already exist:

- `Config.json`
- `MOTD.txt`
- `Plugins/`

The generated `Config.json` contains one example backend server entry.

## Minimal configuration

Example `Config.json`:

```json
{
  "ListenIP": "0.0.0.0",
  "ListenPort": 7778,
  "ServerName": "MultiSEngine",
  "ServerVersion": 319,
  "SwitchTimeOut": 10000,
  "EnableCrossplayFeature": true,
  "EnableChatForward": true,
  "ChatFormat": "[{servername}] {username}: {message}",
  "SwitchToDefaultServerOnJoin": true,
  "RestoreDataWhenJoinNonSSC": true,
  "DisableTcpDelayWhenPipeline": true,
  "UseCrowdControlled": false,
  "DefaultServer": "boss",
  "Servers": [
    {
      "Visible": true,
      "Name": "boss",
      "ShortName": "",
      "IP": "127.0.0.1",
      "Port": 7777,
      "SpawnX": -1,
      "SpawnY": -1,
      "VersionNum": -1
    }
  ]
}
```

## Basic bring-up checklist

1. Set `ListenIP` and `ListenPort` for the proxy endpoint players will join.
2. Define at least one backend server in `Servers`.
3. Set `DefaultServer` to the exact `Name` of one backend server.
4. Start MultiSEngine.
5. In the proxy console, run `test all` or `test <server>`.
6. Join the proxy from a Terraria client.
7. Run `/mse list` in chat to confirm the proxy sees your server list.

## Daily operations

- Use `/mse tp <server>` to switch a player to another backend server.
- Use `/mse back` to return to the default server.
- Use `reload` in the console after editing `Config.json`.
- Use `reloadplugin` in the console after changing plugin DLLs.

## What to check if startup fails

- The backend server names must be unique.
- `DefaultServer` must match an existing backend server name.
- Invalid or empty server names are cleaned from the configuration during load.
- The proxy does not validate every plugin DLL defensively. Keep the `Plugins` folder clean.

Next page: [Configuration](Configuration.md)
