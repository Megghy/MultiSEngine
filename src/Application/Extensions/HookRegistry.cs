using MultiSEngine.Events;

namespace MultiSEngine.Application.Extensions;

public static class HookRegistry
{
    public static void Reset()
        => Hooks.Reset();
}
