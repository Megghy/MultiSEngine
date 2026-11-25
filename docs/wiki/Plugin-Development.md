# Plugin Development

MultiSEngine v2 uses an explicit, managed plugin SDK designed for clean **collectible `AssemblyLoadContext` hot-reload** with zero leaked event handlers or locked DLL files.

## Plugin contract

Every plugin implements `IMsePlugin` (`MultiSEngine.Core.Extensions`):

```csharp
public interface IMsePlugin : IAsyncDisposable
{
    string Name { get; }
    string Description { get; }
    string Author { get; }
    Version Version { get; }
    ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
}
```

## Project setup

Target `net10.0` and reference `MultiSEngine.Core` (plus `TrProtocol` if inspecting typed packets):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MultiSEngine.Core\MultiSEngine.Core.csproj" />
    <ProjectReference Include="..\..\external\TrProtocol\src\TrProtocol\TrProtocol.csproj" />
  </ItemGroup>
</Project>
```

## Minimal plugin example

All subscriptions registered through `IPluginContext` are tracked automatically and revoked before `DisposeAsync()` and `ALC.Unload()`:

```csharp
using MultiSEngine.Core.Extensions;
using MultiSEngine.Core.Security;
using TrProtocol;

namespace ExamplePlugin;

public sealed class SamplePlugin : IMsePlugin
{
    public string Name => "Sample Plugin";
    public string Description => "Demonstrates managed hooks, packet middleware, and commands.";
    public string Author => "YourName";
    public Version Version => new(2, 0, 0);

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        context.OnPlayerJoin(ev =>
        {
            context.Logger.Info($"{ev.Session.PlayerName} joined ({ev.RemoteEndPoint}).", Name);
            return ValueTask.CompletedTask;
        });

        context.OnPreSwitch(ev =>
        {
            context.Logger.Info($"{ev.Session.PlayerName} -> {ev.TargetServer.Name}", Name);
            return ValueTask.CompletedTask;
        });

        context.RegisterPacketMiddleware(
            PacketDirection.ClientToServer,
            [MessageID.PlayerControls],
            static (ctx, ct) => ValueTask.FromResult(PacketMiddlewareResult.Continue));

        context.RegisterCommand(new StatusCommand());
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class StatusCommand : IMseCommand
    {
        public string Name => "status";
        public IReadOnlyList<string> Aliases => [];
        public bool AllowPlayer => true;
        public bool AllowConsole => true;

        public async ValueTask ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            if (context.Session is not null)
                await context.Session.SendChatAsync("Proxy is healthy.", 120, 230, 140, true, cancellationToken);
        }
    }
}
```

## Hot-reload lifecycle

1. `PluginHost` reads each `Plugins/*.dll` (and optional `.pdb`) into memory so files on disk are never locked on Windows.
2. Each plugin loads into its own collectible `AssemblyLoadContext`.
3. On `reloadplugin` or host shutdown, `PluginHost`:
   - cancels `context.UnloadToken`,
   - automatically revokes all hooks, middleware, commands, and custom packet handlers registered through `IPluginContext`,
   - invokes `DisposeAsync()` with exception isolation,
   - unloads the collectible `AssemblyLoadContext`.

Avoid storing references to plugin types in host statics or uncancelled background tasks so GC can reclaim the old ALC immediately.

See also: [Hooks and Custom Packets](Hooks-and-Custom-Packets.md)
