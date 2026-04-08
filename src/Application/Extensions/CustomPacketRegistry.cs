using System.Collections.Frozen;
using System.Reflection;
using MultiSEngine.Protocol.CustomData;

namespace MultiSEngine.Application.Extensions;

public sealed class CustomPacketRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Type> _customPackets = [];
    private FrozenDictionary<string, Type> _snapshot = FrozenDictionary<string, Type>.Empty;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _customPackets.Count;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _customPackets.Clear();
            _snapshot = FrozenDictionary<string, Type>.Empty;
        }
    }

    public void Register<T>() where T : BaseCustomData
        => Register(typeof(T));

    public void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.BaseType != typeof(BaseCustomData))
            throw new ArgumentException($"Invalid custom packet type: {type.FullName}", nameof(type));

        var packet = (BaseCustomData)Activator.CreateInstance(type)!;
        lock (_lock)
        {
            if (!_customPackets.TryAdd(packet.Name, type))
            {
                Logs.Warn($"CustomPacket: [{packet.Name}] already exist.");
                return;
            }

            _snapshot = _customPackets.ToFrozenDictionary();
        }
    }

    public void LoadFromAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var discoveredTypes = assemblies
            .Distinct()
            .SelectMany(static assembly =>
            {
                try
                {
                    return assembly
                        .GetTypes()
                        .Where(static type =>
                            type.BaseType == typeof(BaseCustomData) &&
                            !type.IsAbstract &&
                            type.GetConstructor(Type.EmptyTypes) is not null)
                        .OrderBy(static type => type.FullName, StringComparer.Ordinal);
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
            _customPackets.Clear();
            _snapshot = FrozenDictionary<string, Type>.Empty;
        }

        foreach (var type in discoveredTypes)
            Register(type);
    }

    public FrozenDictionary<string, Type> Snapshot()
    {
        lock (_lock)
        {
            return _snapshot;
        }
    }

    public bool TryGetValue(string name, out Type type)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Snapshot().TryGetValue(name, out type!);
    }
}
