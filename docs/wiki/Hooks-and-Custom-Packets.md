# Hooks and Custom Packets

## Available hooks

The public hook surface is defined in `src/Events/Hooks.cs`.

| Hook | Event args | When it fires |
| --- | --- | --- |
| `Hooks.PlayerJoin` | `PlayerJoinEventArgs` | When a client sends `ClientHello` and enters the join flow. |
| `Hooks.PlayerLeave` | `PlayerLeaveEventArgs` | When a client is leaving through the transfer/runtime path. |
| `Hooks.PreSwitch` | `SwitchEventArgs` | Before a server switch starts. |
| `Hooks.PostSwitch` | `SwitchEventArgs` | After the target backend is selected for the switch path. |
| `Hooks.Chat` | `ChatEventArgs` | When chat is received and before normal chat handling continues. |

`Hooks.RecieveCustomData` exists in the hook class, but it is not currently wired by the active custom packet handler path. Treat it as dormant unless the runtime implementation changes.

## The `Handled` flag

Most event args implement `IEventArgs` and expose:

```csharp
bool Handled { get; set; }
```

When a hook handler sets `Handled = true`, the runtime uses that to skip the default logic for that stage. This is the main interception mechanism for join, switch, and chat behavior.

## Event args overview

| Type | Useful properties |
| --- | --- |
| `PlayerJoinEventArgs` | `Client`, `IP`, `Port`, `Version`, `Handled` |
| `PlayerLeaveEventArgs` | `Client`, `Handled` |
| `SwitchEventArgs` | `Client`, `TargetServer`, `PreSwitch`, `Handled` |
| `ChatEventArgs` | `Client`, `Message`, `Handled` |

## Example hook usage

```csharp
using MultiSEngine.Events;

namespace ExamplePlugin;

public static class ChatHooks
{
    public static void OnChat(ChatEventArgs args)
    {
        if (!args.Message.Equals("!proxy", StringComparison.OrdinalIgnoreCase))
            return;

        Logs.Info($"Proxy trigger from {args.Client.Name}");
        args.Handled = true;
    }
}
```

## Custom packet model

Custom packet types are discovered from all extension assemblies. A valid custom packet type:

- directly inherits `BaseCustomData`
- is non-abstract
- has a public parameterless constructor

The base contract is:

```csharp
public abstract class BaseCustomData
{
    public abstract string Name { get; }
    public abstract void InternalWrite(BinaryWriter writer);
    public abstract void InternalRead(BinaryReader reader);
    public virtual ValueTask OnRecievedData(ClientData client);
}
```

The runtime serializes these packets over Terraria message ID `Unused15`.

## Example custom packet

```csharp
using MultiSEngine.Protocol.CustomData;

namespace ExamplePlugin;

public sealed class EchoPacket : BaseCustomData
{
    public override string Name => "ExamplePlugin.Echo";

    public string Message { get; set; } = string.Empty;

    public override void InternalWrite(BinaryWriter writer)
        => writer.Write(Message);

    public override void InternalRead(BinaryReader reader)
        => Message = reader.ReadString();

    public override ValueTask OnRecievedData(ClientData client)
    {
        Logs.Info($"Echo packet from {client.Name}: {Message}");
        return ValueTask.CompletedTask;
    }
}
```

## Registration behavior

Custom packet registration is automatic during extension rebuild:

- MultiSEngine scans the main assembly and loaded plugin assemblies.
- Types are indexed by `packet.Name`.
- Duplicate packet names are rejected with a warning.

## Practical guidance

- Keep packet names globally unique, for example `YourPlugin.FeatureName`.
- Use packets for explicit extension protocol, not for general state dumping.
- Keep payloads simple and versionable.
- If you change packet shape, update both sender and receiver together.

## Reload behavior

When `reloadplugin` runs:

- hook subscriptions are reset
- plugins are unloaded
- plugins are loaded again
- custom packet registration is rebuilt from scratch

That means packet types should be treated as runtime-discovered state, not as permanent registrations.
