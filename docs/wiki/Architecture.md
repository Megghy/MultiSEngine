# Architecture

## Startup sequence

MultiSEngine starts in `Program.Main` and performs three core steps:

1. print the banner
2. discover and invoke static methods marked with `[AutoInit]`
3. enter the console command loop

`[AutoInit]` methods are collected from the main assembly, ordered by `order`, and invoked with optional pre/post log messages.

## Important runtime components

| Area | Responsibility |
| --- | --- |
| `src/Program.cs` | Process startup, console loop, graceful shutdown. |
| `src/Runtime/RuntimeState.cs` | Global runtime state such as players, command registry, custom packet registry, MOTD, and cached packet buffers. |
| `src/Config.cs` | Load, validate, save, and reload `Config.json`. |
| `src/Application/Transfers` | Player transfer orchestration, state restore, session tracking, and teleport flow. |
| `src/Protocol` | Packet handlers, adapters, bridging, and custom packet transport. |
| `src/Application/Extensions` | Runtime registries for commands and custom packets. |
| `src/Plugins/PluginManager.cs` | Plugin discovery, initialization, unload, and reload. |
| `src/Events` | Hook definitions and event argument types exposed to plugins. |

## Runtime extension model

Extension loading is centralized in `ExtensionBootstrap`:

- `PluginManager.Load()` loads plugin assemblies from `Plugins`.
- `CommandRegistry.LoadFromAssemblies(...)` discovers command classes.
- `CustomPacketRegistry.LoadFromAssemblies(...)` discovers custom packet classes.

The registry scan includes:

- the main MultiSEngine assembly
- all plugin assemblies successfully loaded by `PluginManager`

## Plugin loading model

Plugin loading is assembly-based:

- the loader scans `Plugins/*.dll`
- an assembly counts as a plugin assembly if it contains at least one type assignable to `IMSEPlugin`
- each plugin instance gets `Initialize()` called after creation
- `Dispose()` is called during unload and reload

The plugin host uses a collectible `AssemblyLoadContext`, so reload is modeled as unload plus load.

## Transfer model at a high level

The transfer pipeline is split across application and protocol layers:

1. a player joins the proxy
2. the proxy validates version and session state
3. a target backend server is selected
4. player state is synchronized or restored as needed
5. the player is switched to the target backend and teleported to the chosen spawn

The exact packet flow is handled by protocol adapters and handlers under `src/Protocol`.

## Hooks and interception

Plugins can intercept parts of the lifecycle through `Hooks`:

- player join
- player leave
- pre-switch
- post-switch
- chat

Most hook event args expose a `Handled` flag. Setting `Handled = true` short-circuits the default runtime path for that stage.

## Reload behavior

`reloadplugin` performs these steps in order:

1. reset hook subscriptions
2. unload current plugins
3. load plugin assemblies again
4. rebuild command and custom-packet registries

This means plugin reload affects both hook wiring and runtime-discovered command/custom-packet types.

Next page: [Plugin Development](Plugin-Development.md)
