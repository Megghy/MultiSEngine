# MultiSEngine

MultiSEngine is a .NET 9 proxy layer for Terraria servers. It accepts player connections, keeps a central runtime state, and moves players between backend servers without turning the proxy itself into a full game server.

This wiki is written from the current repository state. It focuses on how the project works today: startup, configuration, runtime behavior, commands, and the built-in plugin model.

## Start here

- [Quick Start](Quick-Start.md)
- [Configuration](Configuration.md)
- [Commands](Commands.md)
- [Architecture](Architecture.md)
- [Plugin Development](Plugin-Development.md)
- [Hooks and Custom Packets](Hooks-and-Custom-Packets.md)

## Core ideas

- MultiSEngine is a standalone proxy process targeting `net9.0`.
- Backend servers are defined in `Config.json` and addressed by logical names.
- Player commands and console commands are runtime-discovered command classes.
- Plugins are loaded from the `Plugins` folder and can add hooks, commands, and custom packet types.
- Runtime extension discovery scans both the main assembly and loaded plugin assemblies.

## Typical workflow

1. Start MultiSEngine once to generate `Config.json`, `MOTD.txt`, and the `Plugins` folder if they do not exist.
2. Edit `Config.json` to point at your backend Terraria servers.
3. Start the proxy and verify connectivity with the console `test` command.
4. Let players join the proxy instead of joining a backend server directly.
5. Use `/mse` commands for server switching.
6. Drop plugin DLLs into `Plugins` and reload them from the console when needed.

## Repository map

- `src/Program.cs`: process startup and auto-initialization
- `src/Config.cs`: configuration load, validation, save, and reload
- `src/Application/Transfers`: cross-server transfer flow
- `src/Application/Extensions`: command and custom-packet registries
- `src/Plugins/PluginManager.cs`: plugin loading and unloading
- `src/Events`: hook definitions and event arguments
- `external/TrProtocol`: Terraria protocol dependency

## Notes

- The main README is bilingual. This wiki is English only.
- These pages are stored inside the repository under `docs/wiki` so they can be versioned with code changes.
- The file names are GitHub Wiki-compatible, so they can be mirrored into a separate `.wiki.git` repository later if needed.
