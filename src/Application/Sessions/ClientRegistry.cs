namespace MultiSEngine.Application.Sessions;

public sealed class ClientRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<long, ClientData> _clients = [];

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _clients.Count;
            }
        }
    }

    public bool Register(ClientData client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_lock)
        {
            return _clients.TryAdd(client.SessionId, client);
        }
    }

    public bool Remove(ClientData client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_lock)
        {
            return _clients.Remove(client.SessionId);
        }
    }

    public ClientData[] SnapshotClients()
    {
        lock (_lock)
        {
            return [.. _clients.Values];
        }
    }

    public SessionSnapshot[] Snapshot()
    {
        lock (_lock)
        {
            return
            [
                .. _clients.Values.Select(static client => new SessionSnapshot(
                    client.SessionId,
                    client.Name,
                    client.Address,
                    client.CurrentServer?.Name,
                    client.State))
            ];
        }
    }

    public ClientData[] Where(Func<ClientData, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return SnapshotClients().Where(predicate).ToArray();
    }

    public ClientData? Find(Func<ClientData, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return SnapshotClients().FirstOrDefault(predicate);
    }
}
