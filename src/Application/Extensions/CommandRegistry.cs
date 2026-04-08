using System.Reflection;
using MultiSEngine.Commands;

namespace MultiSEngine.Application.Extensions;

public sealed class CommandRegistry
{
    private readonly Lock _lock = new();
    private readonly List<CommandDispatcher.CmdBase> _commands = [];

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _commands.Count;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _commands.Clear();
        }
    }

    public void Register(CommandDispatcher.CmdBase command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_lock)
        {
            _commands.Add(command);
        }
    }

    public void LoadFromAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var discoveredCommands = assemblies
            .Distinct()
            .SelectMany(static assembly =>
            {
                try
                {
                    return assembly
                        .GetTypes()
                        .Where(static type =>
                            type.BaseType == typeof(CommandDispatcher.CmdBase) &&
                            !type.IsAbstract &&
                            type.GetConstructor(Type.EmptyTypes) is not null)
                        .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                        .Select(static type => (CommandDispatcher.CmdBase)Activator.CreateInstance(type)!);
                }
                catch (Exception ex)
                {
                    Logs.Error(ex);
                    return [];
                }
            })
            .ToArray();

        lock (_lock)
        {
            _commands.Clear();
            _commands.AddRange(discoveredCommands);
        }
    }

    public CommandDispatcher.CmdBase[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _commands];
        }
    }

    public CommandDispatcher.CmdBase? Find(Func<CommandDispatcher.CmdBase, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Snapshot().FirstOrDefault(predicate);
    }

    public List<CommandDispatcher.CmdBase> FindAll(Func<CommandDispatcher.CmdBase, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Snapshot().Where(predicate).ToList();
    }
}
