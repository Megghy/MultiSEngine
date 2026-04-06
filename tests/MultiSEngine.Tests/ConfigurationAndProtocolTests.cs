using System.Collections.Frozen;
using System.Net;
using System.Reflection;
using System.Text;
using MultiSEngine.Models;
using MultiSEngine.Protocol;
using MultiSEngine.Protocol.CustomData;
using TestSupport;
using TrProtocol;

namespace MultiSEngine.Tests;

public sealed class ConfigurationAndProtocolTests
{
    [Fact]
    public void Load_CreatesDefaultConfig_WhenConfigFileIsMissing()
    {
        using var workspace = new TemporaryWorkspace("config-load");

        var config = Config.Load();

        Assert.True(File.Exists(workspace.GetPath("Config.json")));
        Assert.True(config.SwitchToDefaultServerOnJoin);
        Assert.Equal("boss", config.DefaultServer);
        Assert.Contains(config.Servers, server => server.Name == "boss");
    }

    [Fact]
    public void CheckConfig_RemovesDuplicateAndEmptyServers()
    {
        var config = new Config
        {
            ServerVersion = 279,
            Servers =
            [
                new ServerInfo { Name = "alpha", IP = "127.0.0.1", Port = 7777 },
                new ServerInfo { Name = "alpha", IP = "127.0.0.2", Port = 7778 },
                new ServerInfo { Name = string.Empty, IP = "127.0.0.3", Port = 7779 },
                new ServerInfo { Name = "beta", IP = "127.0.0.4", Port = 7780 },
            ],
        };

        var checkedConfig = Config.CheckConfig(config);

        Assert.Equal(["alpha", "beta"], checkedConfig.Servers.Select(server => server.Name).ToArray());
    }

    [Fact]
    public void SyncIp_CustomPacket_SerializesWithExpectedEnvelope()
    {
        var packet = new SyncIP
        {
            PlayerName = "Alice",
            IP = "127.0.0.1",
        };

        using var rental = BaseCustomData.Serialize(packet);
        using var stream = new MemoryStream(rental.Memory.ToArray());
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        Assert.Equal(rental.Memory.Length, reader.ReadUInt16());
        Assert.Equal((byte)MessageID.Unused15, reader.ReadByte());
        Assert.Equal(packet.Name, reader.ReadString());
        Assert.Equal(string.Empty, reader.ReadString());

        var parsed = new SyncIP();
        parsed.InternalRead(reader);

        Assert.Equal(packet.PlayerName, parsed.PlayerName);
        Assert.Equal(packet.IP, parsed.IP);
    }

    [Fact]
    public void RegisterCustomPacket_BuildsLookupIndex()
    {
        DataBridge.RegisterCustomPacket<SyncIP>();
        DataBridge.RebuildCustomPacketIndex();

        var index = GetCustomPacketIndex();

        Assert.True(index.ContainsKey("MultiSEngine.SyncIP"));
        Assert.Equal(typeof(SyncIP), index["MultiSEngine.SyncIP"]);
    }

    [Fact]
    public async Task ResolveAddressAsync_HandlesLiteralAndInvalidAddresses()
    {
        Assert.True(Utils.TryParseAddress("127.0.0.1", out var parsed));
        Assert.Equal(IPAddress.Loopback, parsed);

        var resolved = await Utils.ResolveAddressAsync("127.0.0.1");
        var invalid = await Utils.ResolveAddressAsync("%%%definitely-invalid-host%%%");

        Assert.Equal(IPAddress.Loopback, resolved);
        Assert.False(Utils.TryParseAddress("%%%definitely-invalid-host%%%", out _));
        Assert.Null(invalid);
    }

    private static FrozenDictionary<string, Type> GetCustomPacketIndex()
    {
        var property = typeof(DataBridge).GetProperty("CustomPackets", BindingFlags.Static | BindingFlags.NonPublic);
        return Assert.IsAssignableFrom<FrozenDictionary<string, Type>>(property?.GetValue(null));
    }
}
