# Commands

MultiSEngine has two command surfaces:

- player chat commands
- proxy console commands

The command runtime discovers command classes from the main assembly and all loaded plugin assemblies.

## Player commands

Player commands are routed through the built-in `/mse` command.

| Command | Aliases | Description |
| --- | --- | --- |
| `/mse tp <server>` | `to`, `t` | Switch to another backend server. |
| `/mse back` | `b` | Return to the default backend server. |
| `/mse list` | `l` | List available backend servers. |
| `/mse password <password>` | `pass`, `p` | Send a password to the current backend when the player is in password-request state. |

## Console commands

The proxy console accepts commands with or without a leading slash. Internally, the console loop normalizes input to command format before dispatch.

| Command | Aliases | Description |
| --- | --- | --- |
| `list` | `l` | List configured backend servers. |
| `online` | `ol`, `playing` | List online players and their current backend. |
| `kick <player> [reason]` | `k` | Disconnect a player. |
| `reload` | `rl` | Reload configuration and localization. |
| `reloadplugin` | `rp` | Reset hooks, unload plugins, load plugins again, then rebuild command and custom-packet registries. |
| `broadcast [server] <message>` | `bc` | Broadcast to all players or only players on a named backend server. |
| `test <server>` | `t` | Test connectivity to one backend server. |
| `test all` | `t` | Test all backend servers. |
| `stop` | `exit` | Shut down the proxy process. |

## Command matching rules

- Player commands are exact-name matches against non-console commands.
- Console commands are matched against commands where `ServerCommand` is `true`.
- Plugin commands can participate in the same dispatch model by inheriting `CommandDispatcher.CmdBase`.

## Error handling

- Command exceptions are logged.
- Players receive a generic failure message when command execution throws.
- The console receives the failure message through logs.

## Adding commands in a plugin

A plugin command is any non-abstract type in the plugin assembly that:

- directly inherits `CommandDispatcher.CmdBase`
- has a public parameterless constructor

The extension registry instantiates these command classes automatically during startup and plugin reload.

Example:

```csharp
using MultiSEngine.Commands;

namespace ExamplePlugin;

public sealed class HelloCommand : CommandDispatcher.CmdBase
{
    public override string Name => "hello";

    public override async ValueTask<bool> Execute(ClientData client, string cmdName, string[] parma)
    {
        if (client is null)
            return false;

        await client.SendSuccessMessageAsync("Hello from plugin.").ConfigureAwait(false);
        return false;
    }
}
```

This command becomes available to players as `/hello`. If you want a console-only command, override `ServerCommand` and design it for console dispatch.

Next page: [Architecture](Architecture.md)
