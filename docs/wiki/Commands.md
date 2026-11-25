# Commands

MultiSEngine v2 separates player chat commands and proxy console commands cleanly inside `CommandRouter`. Both built-in commands and plugin commands implement `IMseCommand` (`MultiSEngine.Core.Extensions`).

## Player commands

Player commands are invoked in chat via `/mse` (or custom plugin commands registered with `AllowPlayer = true`).

| Command | Aliases | Description |
| --- | --- | --- |
| `/mse tp <server>` | `to`, `t` | Transfer to a configured backend server. |
| `/mse back` | `b` | Fall back to the previous server, default server, or Lobby. |
| `/mse list` | `l` | List visible backend servers. |
| `/mse password <password>` | `pass`, `p` | Submit a password when a backend server requests one (supports spaces and quoted strings). |

## Console commands

The proxy console accepts commands with or without a leading `/`, and supports quoted arguments and escaped characters.

| Command | Aliases | Description |
| --- | --- | --- |
| `list` | `l` | List configured backend servers. |
| `online` | `ol`, `playing` | List active sessions, states, and current backends. |
| `kick <player> [reason]` | `k` | Disconnect an online player cleanly. |
| `reload` | `rl` | Reload `Config.json` and localization. |
| `reloadplugin` | `rp` | Cancel unload tokens, revoke plugin registrations, unload collectible ALCs, and reload `Plugins/*.dll`. |
| `broadcast [server] <message>` | `bc` | Broadcast a chat message to all players or players on a specific backend server. |
| `test <server>` | `t` | Probe connectivity to a single backend server. |
| `test all` | `t` | Concurrently probe all configured backend servers. |
| `stop` | `exit` | Gracefully drain sessions, unload plugins, and shut down the host. |

## Security & error handling

- **No password leakage**: When any player command throws an exception, `CommandRouter` logs the failure and still marks the chat command as intercepted so sensitive arguments (such as `/mse password ...`) are **never** forwarded to the backend server.
- **No console command hijacking**: Console commands are dispatched exclusively against commands with `AllowConsole = true` using `CommandSource.Console`.

## Registering a plugin command

Plugins register commands explicitly through `IPluginContext.RegisterCommand(...)` — no reflection scanning is required, and registrations are automatically revoked on plugin unload.

```csharp
using MultiSEngine.Core.Extensions;
using MultiSEngine.Core.Security;

public sealed class HelloCommand : IMseCommand
{
    public string Name => "hello";
    public IReadOnlyList<string> Aliases => ["hi"];
    public bool AllowPlayer => true;
    public bool AllowConsole => true;

    public async ValueTask ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Source == CommandSource.Player && context.Session is not null)
        {
            await context.Session.SendChatAsync("Hello from plugin!", 120, 230, 140, withPrefix: true, cancellationToken);
            return;
        }

        context.Logger.Info("Hello from console!", "ExamplePlugin");
    }
}
```

Next page: [Architecture](Architecture.md)
