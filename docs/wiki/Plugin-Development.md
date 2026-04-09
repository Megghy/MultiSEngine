# Plugin Development

## Plugin model

The built-in plugin system is intentionally simple:

- MultiSEngine loads DLLs from the `Plugins` folder in the current working directory.
- A DLL is treated as a plugin assembly when it contains at least one type assignable to `IMSEPlugin`.
- The plugin instance receives `Initialize()` on load and `Dispose()` on unload.
- The same assembly can also contribute commands and custom packet types.

The core contract is:

```csharp
public interface IMSEPlugin
{
    string Name { get; }
    string Description { get; }
    string Author { get; }
    Version Version { get; }
    void Initialize();
    void Dispose();
}
```

## Minimum project setup

Create a class library targeting the same runtime as MultiSEngine:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MultiSEngine.csproj" />
  </ItemGroup>
</Project>
```

If your plugin project lives outside this repository, reference the built `MultiSEngine.dll` instead of using a project reference.

## Minimal plugin example

```csharp
using MultiSEngine.Events;
using MultiSEngine.Plugins;

namespace ExamplePlugin;

public sealed class SamplePlugin : IMSEPlugin
{
    public string Name => "Sample Plugin";
    public string Description => "Logs lifecycle events.";
    public string Author => "YourName";
    public Version Version => new(1, 0, 0);

    public void Initialize()
    {
        Hooks.PlayerJoin += OnPlayerJoin;
        Hooks.PreSwitch += OnPreSwitch;
        Logs.Info("Sample Plugin initialized.");
    }

    public void Dispose()
    {
        Hooks.PlayerJoin -= OnPlayerJoin;
        Hooks.PreSwitch -= OnPreSwitch;
        Logs.Info("Sample Plugin disposed.");
    }

    private static void OnPlayerJoin(PlayerJoinEventArgs args)
        => Logs.Info($"[Sample Plugin] {args.Client.Name} joined from {args.IP}:{args.Port}");

    private static void OnPreSwitch(SwitchEventArgs args)
        => Logs.Info($"[Sample Plugin] {args.Client.Name} -> {args.TargetServer.Name}");
}
```

## Packaging and deployment

1. Build the plugin DLL.
2. Copy the plugin DLL into the proxy's `Plugins` folder.
3. Restart the proxy or run `reloadplugin` in the console.

If your plugin depends on extra assemblies, keep the current loader limitations in mind:

- the loader only scans `Plugins/*.dll` at the top level
- plugin discovery does not isolate or resolve complex dependency graphs
- a broken or incompatible DLL in `Plugins` can break the load pass

In practice, simple plugins that only depend on MultiSEngine and its shared runtime surface are the safest fit.

## How commands from plugins work

Any public, non-abstract, parameterless type in the same plugin assembly that directly inherits `CommandDispatcher.CmdBase` is auto-registered when extensions are rebuilt.

Example:

```csharp
using MultiSEngine.Commands;

namespace ExamplePlugin;

public sealed class WhoAmICommand : CommandDispatcher.CmdBase
{
    public override string Name => "whoami";

    public override async ValueTask<bool> Execute(ClientData client, string cmdName, string[] parma)
    {
        if (client is null)
            return false;

        await client.SendInfoMessageAsync($"You are {client.Name}.").ConfigureAwait(false);
        return false;
    }
}
```

## Lifecycle expectations

Write plugins as if reload is normal, because it is:

- `Initialize()` should subscribe hooks and start only the resources you actually need.
- `Dispose()` should stop timers, release files, cancel background work, and unsubscribe hooks.
- Do not rely on static process-lifetime state unless you own its cleanup.

## Type requirements for discovery

To be discovered reliably, extension types should be:

- public
- non-abstract
- parameterless

This applies to:

- `IMSEPlugin` implementations
- command classes
- custom packet classes

## Recommended plugin boundaries

- Put hook wiring in the plugin class.
- Put command logic in dedicated command classes.
- Put packet wire format in dedicated `BaseCustomData` classes.
- Keep each plugin focused on one job instead of building a large framework inside the plugin.

See also: [Hooks and Custom Packets](Hooks-and-Custom-Packets.md)
