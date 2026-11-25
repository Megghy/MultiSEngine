using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Sessions;
using MultiSEngine.Core.Transfers;
using MultiSEngine.IntegrationTests.Support;
using MultiSEngine.Protocol.CustomPackets;
using MultiSEngine.Protocol.Framing;
using MultiSEngine.Runtime.Hosting;
using Terraria;
using Terraria.DataStructures;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.IntegrationTests;

public sealed class ProxyEndToEndTests
{
    private static async Task<MultiSEngineHost> StartHostAsync(ProxyConfig config)
    {
        var host = new MultiSEngineHost(new MultiSEngineHostOptions
        {
            WorkingDirectory = Path.GetTempPath(),
            InitialConfig = config with { ListenIP = "127.0.0.1", ListenPort = 0 },
            EnableConsoleLogging = false,
            EnableFileLogging = false,
            EnableTcpListener = true,
            EnablePlugins = false,
        });
        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task Handshake_RejectsPrematureSpawnPlayerAndCleansUpSession()
    {
        await using var host = await StartHostAsync(new ProxyConfig());
        await using var client = await FakeTerrariaClient.ConnectAsync(host.BoundEndPoint!.Port);

        // Skip ClientHello/WorldInfo/TileData and jump straight to SpawnPlayer.
        await client.SendPacketAsync(new SpawnPlayer
        {
            PlayerSlot = 0,
            Position = new Point16(100, 100),
            Context = PlayerSpawnContext.SpawningIntoWorld,
        });

        var kick = await client.WaitForPacketAsync<Kick>(TimeSpan.FromSeconds(5));
        Assert.Contains("Protocol violation", kick.Reason.ToString(), StringComparison.OrdinalIgnoreCase);
        await client.Closed.WaitAsync(TimeSpan.FromSeconds(5));

        await WaitUntilAsync(() => host.Sessions.Count == 0, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FullTransferLifecycle_ZeroPacketLoss_SlotRewrite_AndCleanLobbyReturn()
    {
        var postConnectPacket = new SyncItem
        {
            ItemSlot = 42,
            ItemType = 75,
            Stack = 99,
        };

        await using var serverA = new FakeTerrariaServer(new FakeServerOptions
        {
            WorldName = "WorldA",
            AssignedPlayerSlot = 7,
            IsServerSideCharacter = false,
            PostConnectPackets = [postConnectPacket],
        });

        await using var serverB = new FakeTerrariaServer(new FakeServerOptions
        {
            WorldName = "WorldB",
            AssignedPlayerSlot = 12,
            IsServerSideCharacter = true,
        });

        var config = new ProxyConfig
        {
            ServerVersion = 329,
            RestoreDataWhenJoinNonSsc = true,
            CustomPackets = new CustomPacketSecurityConfig { GlobalSharedSecret = "shared-key-1" },
            Servers =
            [
                new ServerDescriptor { Name = "srvA", Host = "127.0.0.1", Port = serverA.Port, Visible = true },
                new ServerDescriptor { Name = "srvB", Host = "127.0.0.1", Port = serverB.Port, Visible = true },
            ],
        };

        await using var host = await StartHostAsync(config);
        await using var client = await FakeTerrariaClient.ConnectAsync(host.BoundEndPoint!.Port);
        await client.CompleteLobbyHandshakeAsync("Alice", "uuid-alice");

        var session = host.Sessions.FindByName("Alice");
        Assert.NotNull(session);
        Assert.Equal(SessionPhase.Lobby, session!.CurrentState.Phase);

        // Lobby chat must work (was broken in v1 due to stale ClientState.NewConnection).
        await client.SendChatAsync("hello lobby");
        var lobbyEcho = await client.WaitForChatContainingAsync("hello lobby", TimeSpan.FromSeconds(5));
        Assert.Contains("Alice", lobbyEcho);

        // 1. Transfer Lobby -> srvA (slot=7, non-SSC).
        var resA = await session.TransferToAsync(config.Servers[0]);
        Assert.True(resA.IsSuccess, resA.Message);
        Assert.Equal(SessionPhase.InServer, session.CurrentState.Phase);
        Assert.Equal((byte)7, session.CurrentState.ActivePlayerSlot);

        // Verify signed SyncIP reached serverA.
        var syncIpFrame = await serverA.WaitForFrameAsync(MessageID.Unused15, TimeSpan.FromSeconds(5));
        var decodedSyncIp = host.CustomPacketCodec.Decode(syncIpFrame, "shared-key-1", TimeSpan.FromSeconds(30), allowUnsigned: false);
        Assert.True(decodedSyncIp.IsSuccess);
        Assert.Equal(BuiltInCustomPacketTypes.SyncIp, decodedSyncIp.Envelope!.PacketType);

        // Verify post-connect SyncItem sent immediately after FinishedConnectingToServer arrived with ZERO LOSS!
        var syncedItem = await client.WaitForPacketAsync<SyncItem>(TimeSpan.FromSeconds(5), i => i.ItemSlot == 42);
        Assert.Equal((short)75, syncedItem.ItemType);

        // 2. Transfer srvA -> srvB (slot=12, SSC).
        var resB = await session.TransferToAsync(config.Servers[1]);
        Assert.True(resB.IsSuccess, resB.Message);
        Assert.Equal("srvB", session.CurrentServer?.Name);
        Assert.Equal((byte)12, session.CurrentState.ActivePlayerSlot);

        // 3. Kick from srvB -> must automatically fall back to LastServer (srvA), NOT Lobby!
        await serverB.SendToLatestAsync(new Kick { Reason = new Terraria.Localization.NetworkText("maintenance", Terraria.Localization.NetworkText.Mode.Literal) });
        await WaitUntilAsync(() => session.CurrentServer?.Name == "srvA" && session.CurrentState.Phase == SessionPhase.InServer, TimeSpan.FromSeconds(5));

        // 4. Kick from srvA when no other fallback -> returns cleanly to Lobby with slot=0!
        await serverA.SendToLatestAsync(new Kick { Reason = new Terraria.Localization.NetworkText("shutdown", Terraria.Localization.NetworkText.Mode.Literal) });
        await WaitUntilAsync(() => session.CurrentState.Phase == SessionPhase.Lobby && session.CurrentState.ActivePlayerSlot == 0, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PasswordProtectedTransfer_NeverDeadlocksClientReadLoopAndSupportsSpaces()
    {
        await using var secureServer = new FakeTerrariaServer(new FakeServerOptions
        {
            WorldName = "SecretVault",
            AssignedPlayerSlot = 3,
            RequiredPassword = "open sesame 42",
        });

        var config = new ProxyConfig
        {
            ServerVersion = 329,
            Servers = [new ServerDescriptor { Name = "vault", Host = "127.0.0.1", Port = secureServer.Port }],
        };

        await using var host = await StartHostAsync(config);
        await using var client = await FakeTerrariaClient.ConnectAsync(host.BoundEndPoint!.Port);
        await client.CompleteLobbyHandshakeAsync("Bob", "uuid-bob");

        // Trigger transfer via player chat command (the exact path that deadlocked in v1!).
        await client.SendChatAsync("/mse tp vault");
        await client.WaitForChatContainingAsync("密码", TimeSpan.FromSeconds(5));

        var session = host.Sessions.FindByName("Bob")!;
        await WaitUntilAsync(() => session.CurrentState.Phase == SessionPhase.TransferAwaitingPassword, TimeSpan.FromSeconds(5));

        // Wrong password first.
        await client.SendChatAsync("/mse pass wrong");
        await client.WaitForChatContainingAsync("错误", TimeSpan.FromSeconds(5));

        // Correct password containing spaces.
        await client.SendChatAsync("/mse pass open sesame 42");
        await WaitUntilAsync(() => session.CurrentState.Phase == SessionPhase.InServer && session.CurrentServer?.Name == "vault", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task InServerClientDisconnect_AndConcurrentTransferRequests_NeverLeakSessionsOrDeadlock()
    {
        await using var serverA = new FakeTerrariaServer(new FakeServerOptions { WorldName = "A", AssignedPlayerSlot = 2 });
        await using var serverB = new FakeTerrariaServer(new FakeServerOptions { WorldName = "B", AssignedPlayerSlot = 4 });

        var config = new ProxyConfig
        {
            Servers =
            [
                new ServerDescriptor { Name = "A", Host = "127.0.0.1", Port = serverA.Port },
                new ServerDescriptor { Name = "B", Host = "127.0.0.1", Port = serverB.Port },
            ],
        };

        await using var host = await StartHostAsync(config);
        var client = await FakeTerrariaClient.ConnectAsync(host.BoundEndPoint!.Port);
        await client.CompleteLobbyHandshakeAsync("Charlie", "uuid-charlie");

        var session = host.Sessions.FindByName("Charlie")!;
        var joinA = await session.TransferToAsync(config.Servers[0]);
        Assert.True(joinA.IsSuccess);

        // Fire concurrent transfer attempts to verify single-writer actor rejects overlapping requests cleanly.
        var t1 = session.TransferToAsync(config.Servers[1]).AsTask();
        var t2 = session.TransferToAsync(config.Servers[1]).AsTask();
        var results = await Task.WhenAll(t1, t2);
        Assert.Contains(results, r => r.IsSuccess);
        Assert.Contains(results, r => r.Status is TransferStatus.InvalidState or TransferStatus.AlreadyInTarget);

        // Abrupt client disconnect while InServer MUST clean up session completely (was permanent leak in v1!).
        await client.DisposeAsync();
        await WaitUntilAsync(() => host.Sessions.Count == 0, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SecurityBoundaries_BlocksClientForgedCustomPackets_AndCrossPlayerBackendCommands()
    {
        const string secret = "hmac-cluster-secret";
        await using var serverRestricted = new FakeTerrariaServer(new FakeServerOptions { WorldName = "Restricted", AssignedPlayerSlot = 1 });
        await using var serverTrusted = new FakeTerrariaServer(new FakeServerOptions { WorldName = "Trusted", AssignedPlayerSlot = 2 });

        var config = new ProxyConfig
        {
            CustomPackets = new CustomPacketSecurityConfig { GlobalSharedSecret = secret },
            Servers =
            [
                new ServerDescriptor { Name = "restricted", Host = "127.0.0.1", Port = serverRestricted.Port, AllowClusterCommands = false },
                new ServerDescriptor { Name = "trusted", Host = "127.0.0.1", Port = serverTrusted.Port, AllowClusterCommands = true },
            ],
        };

        await using var host = await StartHostAsync(config);
        await using var alice = await FakeTerrariaClient.ConnectAsync(host.BoundEndPoint!.Port);
        await alice.CompleteLobbyHandshakeAsync("Alice", "uuid-a");

        await using var bob = await FakeTerrariaClient.ConnectAsync(host.BoundEndPoint!.Port);
        await bob.CompleteLobbyHandshakeAsync("Bob", "uuid-b");

        var aliceSession = host.Sessions.FindByName("Alice")!;
        var bobSession = host.Sessions.FindByName("Bob")!;

        Assert.True((await aliceSession.TransferToAsync(config.Servers[0])).IsSuccess);

        // 1. Client tries to forge Unused15 ExecuteCommand targeting Bob -> MUST be blocked before reaching backend.
        var forgedFrame = host.CustomPacketCodec.Encode(
            BuiltInCustomPacketTypes.ExecuteCommand,
            BuiltInCustomPacketTypes.EncodeExecuteCommandPayload("Bob", "/mse tp trusted"),
            sharedSecret: secret);
        await alice.SendRawAsync(forgedFrame);
        await Task.Delay(150);
        Assert.Equal(SessionPhase.Lobby, bobSession.CurrentState.Phase);

        // 2. Restricted backend sends signed ExecuteCommand targeting Bob -> MUST be blocked (AllowClusterCommands = false).
        var crossPlayerCmd = host.CustomPacketCodec.Encode(
            BuiltInCustomPacketTypes.ExecuteCommand,
            BuiltInCustomPacketTypes.EncodeExecuteCommandPayload("Bob", "/mse tp trusted"),
            sharedSecret: secret);
        await serverRestricted.SendRawToLatestAsync(crossPlayerCmd);
        await Task.Delay(150);
        Assert.Equal(SessionPhase.Lobby, bobSession.CurrentState.Phase);

        // 3. Trusted backend (AllowClusterCommands = true) sends signed ExecuteCommand targeting Bob -> SUCCEEDS!
        Assert.True((await aliceSession.TransferToAsync(config.Servers[1])).IsSuccess);
        var authorizedCmd = host.CustomPacketCodec.Encode(
            BuiltInCustomPacketTypes.ExecuteCommand,
            BuiltInCustomPacketTypes.EncodeExecuteCommandPayload("Bob", "/mse tp restricted"),
            sharedSecret: secret);
        await serverTrusted.SendRawToLatestAsync(authorizedCmd);
        await WaitUntilAsync(() => bobSession.CurrentServer?.Name == "restricted", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BackendSwitchServerPacket_ValidSignatureTransfersAndRejectsBadSignature()
    {
        const string secret = "tshock-bridge-secret";
        await using var serverA = new FakeTerrariaServer(new FakeServerOptions { WorldName = "WorldA", AssignedPlayerSlot = 3 });
        await using var serverB = new FakeTerrariaServer(new FakeServerOptions { WorldName = "WorldB", AssignedPlayerSlot = 8 });

        var config = new ProxyConfig
        {
            ServerVersion = 329,
            CustomPackets = new CustomPacketSecurityConfig { GlobalSharedSecret = secret },
            Servers =
            [
                new ServerDescriptor { Name = "srvA", Host = "127.0.0.1", Port = serverA.Port, Visible = true },
                new ServerDescriptor { Name = "srvB", Host = "127.0.0.1", Port = serverB.Port, Visible = true },
            ],
        };

        await using var host = await StartHostAsync(config);
        await using var client = await FakeTerrariaClient.ConnectAsync(host.BoundEndPoint!.Port);
        await client.CompleteLobbyHandshakeAsync("Dana", "uuid-dana");

        var session = host.Sessions.FindByName("Dana")!;
        Assert.True((await session.TransferToAsync(config.Servers[0])).IsSuccess);
        Assert.Equal("srvA", session.CurrentServer?.Name);

        // Wrong secret must be ignored.
        var badFrame = host.CustomPacketCodec.Encode(
            BuiltInCustomPacketTypes.SwitchServer,
            BuiltInCustomPacketTypes.EncodeSwitchServerPayload("srvB"),
            sharedSecret: "wrong-secret");
        await serverA.SendRawToLatestAsync(badFrame);
        await Task.Delay(150);
        Assert.Equal("srvA", session.CurrentServer?.Name);

        // Valid signed mse.switch_server from TShock backend must transfer player to srvB.
        var validFrame = host.CustomPacketCodec.Encode(
            BuiltInCustomPacketTypes.SwitchServer,
            BuiltInCustomPacketTypes.EncodeSwitchServerPayload("srvB"),
            sharedSecret: secret);
        await serverA.SendRawToLatestAsync(validFrame);
        await WaitUntilAsync(() => session.CurrentServer?.Name == "srvB" && session.CurrentState.Phase == SessionPhase.InServer, TimeSpan.FromSeconds(5));
        Assert.Equal((byte)8, session.CurrentState.ActivePlayerSlot);
    }

    [Fact]
    public async Task ServerProbeService_ConcurrentlyProbesMultipleServersWithoutStaticLockFailure()
    {
        await using var ok1 = new FakeTerrariaServer(new FakeServerOptions { WorldName = "OK1" });
        await using var ok2 = new FakeTerrariaServer(new FakeServerOptions { WorldName = "OK2" });
        await using var locked = new FakeTerrariaServer(new FakeServerOptions { WorldName = "Locked", RequiredPassword = "pw" });

        var servers = new[]
        {
            new ServerDescriptor { Name = "ok1", Host = "127.0.0.1", Port = ok1.Port },
            new ServerDescriptor { Name = "ok2", Host = "127.0.0.1", Port = ok2.Port },
            new ServerDescriptor { Name = "locked", Host = "127.0.0.1", Port = locked.Port },
        };

        await using var host = await StartHostAsync(new ProxyConfig { Servers = [.. servers] });
        var results = await host.Probes.ProbeAllAsync(servers, TimeSpan.FromSeconds(5));

        Assert.Equal(3, results.Count);
        Assert.True(results[0].IsAvailable);
        Assert.True(results[1].IsAvailable);
        Assert.False(results[2].IsAvailable);
        Assert.Contains("password", results[2].FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20).ConfigureAwait(false);
        }
        Assert.True(condition(), $"Condition was not met within {timeout.TotalSeconds:F1}s.");
    }
}
