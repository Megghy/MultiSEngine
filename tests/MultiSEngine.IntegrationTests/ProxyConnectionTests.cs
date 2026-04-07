using System.Net;
using System.Net.Sockets;
using System.Reflection;
using MultiSEngine.IntegrationTests.Support;
using MultiSEngine.Models;
using MultiSEngine.Networking;
using TestSupport;
using TrProtocol.NetPackets;

namespace MultiSEngine.IntegrationTests;

public sealed class ProxyConnectionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly MethodInfo TestConnectMethod = typeof(ProxyServer).GetMethod(
        "TestConnectAsync",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(ProxyServer).FullName, "TestConnectAsync");

    [Fact]
    public async Task TestConnectAsync_ReturnsTrue_WhenFakeServerCompletesHandshake()
    {
        using var workspace = new TemporaryWorkspace("integration-direct-success");
        await using var fakeServer = new FakeTerrariaServer();
        var targetServer = CreateTargetServer("direct-success", fakeServer.Port);

        workspace.WriteConfig(CreateConfig(targetServer));

        var handshakeTask = RunSuccessfulHandshakeAsync(fakeServer);
        var result = await InvokeTestConnectAsync(targetServer);

        Assert.True(result);
        await handshakeTask;
    }

    [Fact]
    public async Task TestConnectAsync_ReturnsFalse_WhenServerRequestsPassword()
    {
        using var workspace = new TemporaryWorkspace("integration-password-failure");
        await using var fakeServer = new FakeTerrariaServer();
        var targetServer = CreateTargetServer("password-failure", fakeServer.Port);

        workspace.WriteConfig(CreateConfig(targetServer));

        var handshakeTask = RunPasswordFailureHandshakeAsync(fakeServer);
        var result = await InvokeTestConnectAsync(targetServer);

        Assert.False(result);
        await handshakeTask;
    }

    [Fact]
    public async Task TestConnectAsync_ReturnsTrue_WhenRoutedThroughTrProtocolTestAgent()
    {
        using var workspace = new TemporaryWorkspace("integration-test-agent");
        await using var fakeServer = new FakeTerrariaServer();

        var testAgentPort = GetAvailableTcpPort();
        workspace.WriteJsonFile(
            "testagent.json",
            new
            {
                ListenPort = testAgentPort,
                TargetHost = "127.0.0.1",
                TargetPort = fakeServer.Port,
                RoundTrip = new
                {
                    Enabled = false,
                    Dump = "window",
                    ContextLines = 3,
                    FullDumpThresholdBytes = 256,
                },
                Filters = new
                {
                    Direction = "all",
                    ShowOk = false,
                    ShowParseIssues = true,
                    ShowRoundTripIssues = true,
                },
            });

        await using var testAgent = new TestAgentProcess(workspace.RootPath, workspace.GetPath("testagent.json"));
        await testAgent.WaitUntilReadyAsync(Timeout);

        var targetServer = CreateTargetServer("through-test-agent", testAgentPort);
        workspace.WriteConfig(CreateConfig(targetServer));

        var handshakeTask = RunSuccessfulHandshakeAsync(fakeServer);
        var result = await InvokeTestConnectAsync(targetServer);

        Assert.True(result);
        await handshakeTask;
        await testAgent.WaitForClientConnectionAsync(Timeout);
        Assert.Contains("[Proxy] Connected to remote server.", testAgent.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectAsync_ReturnsTrue_WhenTargetIsLiveTShockServer()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_TSHOCK_LIVE_TEST"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var repositoryPath = Environment.GetEnvironmentVariable("TSHOCK_REPO_PATH") ?? @"G:\Temp\TShock";
        if (!Directory.Exists(repositoryPath))
        {
            return;
        }

        using var workspace = new TemporaryWorkspace("integration-live-tshock");
        var targetPort = GetAvailableTcpPort();

        await using var tshock = new TShockServerProcess(repositoryPath, workspace.RootPath, targetPort);
        await tshock.WaitUntilReadyAsync(TimeSpan.FromMinutes(2));

        var targetServer = CreateTargetServer("live-tshock", targetPort, versionNum: 319);
        workspace.WriteConfig(CreateConfig(targetServer));

        var result = await InvokeTestConnectAsync(targetServer);

        Assert.True(result, tshock.Output);
    }

    private static async Task RunSuccessfulHandshakeAsync(FakeTerrariaServer fakeServer)
    {
        await fakeServer.WaitForConnectionAsync(Timeout);

        var hello = await fakeServer.WaitForPacketAsync<ClientHello>(Timeout);
        Assert.StartsWith("Terraria", hello.Version, StringComparison.Ordinal);

        await fakeServer.SendAsync(new LoadPlayer
        {
            PlayerSlot = 7,
        });

        await fakeServer.WaitForPacketAsync<ClientUUID>(Timeout);
        await fakeServer.WaitForPacketAsync<SyncPlayer>(Timeout);
        await fakeServer.WaitForPacketAsync<RequestWorldInfo>(Timeout);

        await fakeServer.SendAsync(CreateWorldData());

        await fakeServer.WaitForPacketAsync<RequestTileData>(Timeout);
        await fakeServer.WaitForPacketAsync<SpawnPlayer>(Timeout);

        await fakeServer.SendAsync(new StartPlaying());
    }

    private static async Task RunPasswordFailureHandshakeAsync(FakeTerrariaServer fakeServer)
    {
        await fakeServer.WaitForConnectionAsync(Timeout);
        await fakeServer.WaitForPacketAsync<ClientHello>(Timeout);
        await fakeServer.SendAsync(new RequestPassword());
    }

    private static ServerInfo CreateTargetServer(string name, int port, int versionNum = 318)
        => new()
        {
            Name = name,
            IP = IPAddress.Loopback.ToString(),
            Port = port,
            VersionNum = versionNum,
        };

    private static Config CreateConfig(ServerInfo defaultServer)
        => new()
        {
            ServerVersion = 318,
            SwitchTimeOut = (int)Timeout.TotalMilliseconds,
            DefaultServer = defaultServer.Name,
            Servers = [defaultServer],
        };

    private static WorldData CreateWorldData()
        => new()
        {
            MaxTileX = 8400,
            MaxTileY = 2400,
            SpawnX = 120,
            SpawnY = 240,
            WorldName = "IntegrationTestWorld",
            WorldUniqueID = Guid.NewGuid(),
            ExtraSpawnPointCount = 0,
            ExtraSpawnPoints = [],
        };

    private static async Task<bool> InvokeTestConnectAsync(ServerInfo server)
    {
        var task = Assert.IsAssignableFrom<Task<bool>>(TestConnectMethod.Invoke(null, [server, false]));
        return await task;
    }

    private static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
