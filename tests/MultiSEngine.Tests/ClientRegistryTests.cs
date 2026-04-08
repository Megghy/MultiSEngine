using MultiSEngine.Application.Sessions;
using MultiSEngine.Models;
using TrProtocol.NetPackets;

namespace MultiSEngine.Tests;

public sealed class ClientRegistryTests
{
    [Fact]
    public void Register_Remove_AndCount_WorkAsExpected()
    {
        var registry = new ClientRegistry();
        var first = CreateClient("Alpha");
        var second = CreateClient("Beta");

        Assert.True(registry.Register(first));
        Assert.True(registry.Register(second));
        Assert.Equal(2, registry.Count);

        Assert.True(registry.Remove(first));
        Assert.Equal(1, registry.Count);
        Assert.False(registry.Remove(first));
    }

    [Fact]
    public void Register_RejectsDuplicateSessionRegistration()
    {
        var registry = new ClientRegistry();
        var client = CreateClient("Alpha");

        Assert.True(registry.Register(client));
        Assert.False(registry.Register(client));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void SnapshotClients_ReturnsDetachedArray()
    {
        var registry = new ClientRegistry();
        var first = CreateClient("Alpha");
        var second = CreateClient("Beta");
        registry.Register(first);
        registry.Register(second);

        var snapshot = registry.SnapshotClients();

        registry.Remove(first);

        Assert.Equal(2, snapshot.Length);
        Assert.Contains(snapshot, client => client.Name == "Alpha");
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Snapshot_ProjectsStableSessionData()
    {
        var registry = new ClientRegistry();
        var alpha = CreateClient("Alpha", "Lobby");
        var beta = CreateClient("Beta");
        beta.State = ClientState.Switching;
        registry.Register(alpha);
        registry.Register(beta);

        var snapshot = registry.Snapshot();

        Assert.Collection(
            snapshot.OrderBy(static item => item.Name),
            item =>
            {
                Assert.Equal("Alpha", item.Name);
                Assert.Equal("Lobby", item.CurrentServerName);
                Assert.Equal(ClientState.NewConnection, item.State);
            },
            item =>
            {
                Assert.Equal("Beta", item.Name);
                Assert.Null(item.CurrentServerName);
                Assert.Equal(ClientState.Switching, item.State);
            });
    }

    [Fact]
    public void Find_And_Where_OperateOnSafeSnapshots()
    {
        var registry = new ClientRegistry();
        var fakeWorld = CreateClient("Alpha");
        var targetWorld = CreateClient("Beta", "Boss");
        registry.Register(fakeWorld);
        registry.Register(targetWorld);

        var found = registry.Find(client => client.Name == "Beta");
        var fakeWorldClients = registry.Where(client => client.CurrentServer is null);

        Assert.Same(targetWorld, found);
        Assert.Single(fakeWorldClients);
        Assert.Same(fakeWorld, fakeWorldClients[0]);
    }

    private static ClientData CreateClient(string name, string? currentServerName = null)
    {
        return new ClientData
        {
            Player = new PlayerInfo
            {
                OriginCharacter =
                {
                    Info = new SyncPlayer
                    {
                        Name = name,
                    }
                }
            },
            CurrentServer = currentServerName is null
                ? default!
                : new ServerInfo
                {
                    Name = currentServerName,
                }
        };
    }
}
