using System.Collections.Concurrent;
using MultiSEngine.Protocol.CustomPackets;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace MultiSEngine.TShock;

[ApiVersion(2, 1)]
public sealed class MultiSEngineTShockPlugin : TerrariaPlugin
{
    private readonly CustomPacketCodec _codec = new();
    private readonly ConcurrentDictionary<int, ProxySlotState> _slots = new();
    private readonly string _configPath;
    private volatile TShockBridgeConfig _config = new();

    private sealed record ProxySlotState(string SyncedIp, string ProxySourceIp, string PlayerName);

    public MultiSEngineTShockPlugin(Main game) : base(game)
    {
        Order = -1000;
        _configPath = Path.Combine(TShockAPI.TShock.SavePath, "MultiSEngine.json");
    }

    public override string Name => "MultiSEngine.TShock";
    public override string Author => "Megghy";
    public override string Description => "Verifies MultiSEngine v2 MSE2 packets, restores real client IPs, and bridges cross-server transfers on TShock 6.";
    public override Version Version => typeof(MultiSEngineTShockPlugin).Assembly.GetName().Version ?? new Version(2, 0, 0);

    public override void Initialize()
    {
        ReloadConfig();
        MultiSEngineBridge.Attach(this);

        ServerApi.Hooks.NetGetData.Register(this, OnNetGetData, -1000);
        ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin, -1000);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer, -1000);
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
        GeneralHooks.ReloadEvent += OnReload;

        Commands.ChatCommands.Add(new Command("mse.switch", OnSwitchCommand, "mseswitch", "msetp")
        {
            HelpText = "Requests MultiSEngine to switch your current server: /mseswitch <server>",
        });
        Commands.ChatCommands.Add(new Command("mse.admin", OnReloadCommand, "msereload")
        {
            HelpText = "Reloads MultiSEngine.TShock configuration.",
        });

        TShockAPI.TShock.Log.ConsoleInfo($"[MultiSEngine.TShock] v{Version} initialized (AllowDirectJoin={_config.AllowDirectJoin}, SharedSecretConfigured={!string.IsNullOrEmpty(_config.SharedSecret)}).");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServerApi.Hooks.NetGetData.Deregister(this, OnNetGetData);
            ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
            GeneralHooks.ReloadEvent -= OnReload;

            Commands.ChatCommands.RemoveAll(c =>
                c.HasAlias("mseswitch") || c.HasAlias("msereload"));

            MultiSEngineBridge.Detach(this);
            _slots.Clear();
        }

        base.Dispose(disposing);
    }

    internal bool IsProxied(int playerIndex) => _slots.ContainsKey(playerIndex);

    internal string? GetSyncedIp(int playerIndex)
        => _slots.TryGetValue(playerIndex, out var state) ? state.SyncedIp : null;

    internal bool SendSignedCustomPacket(TSPlayer player, string packetType, ReadOnlySpan<byte> payload)
    {
        var cfg = _config;
        if (string.IsNullOrEmpty(cfg.SharedSecret) && !cfg.AllowUnsignedInDevelopment)
        {
            TShockAPI.TShock.Log.ConsoleError($"[MultiSEngine.TShock] Cannot send '{packetType}': SharedSecret is empty in {_configPath}.");
            return false;
        }

        var frame = _codec.Encode(packetType, payload, cfg.SharedSecret);
        player.SendRawData(frame);
        return true;
    }

    private void OnNetGetData(GetDataEventArgs args)
    {
        if (args.Handled || (byte)args.MsgID != CustomPacketCodec.Unused15MessageId)
            return;

        // Always intercept Unused15 so Terraria's State < 10 check never boots proxied handshakes.
        args.Handled = true;

        var who = args.Msg.whoAmI;
        var bodyLength = args.Length - 1;
        if (bodyLength <= 0 || args.Index < 0 || args.Index + bodyLength > args.Msg.readBuffer.Length)
            return;

        var cfg = _config;
        var body = args.Msg.readBuffer.AsSpan(args.Index, bodyLength);
        var decoded = _codec.DecodePayload(
            body,
            expectedSecret: cfg.SharedSecret,
            maxClockSkew: TimeSpan.FromSeconds(cfg.MaxClockSkewSeconds),
            allowUnsigned: cfg.AllowUnsignedInDevelopment);

        if (!decoded.IsSuccess || decoded.Envelope is null)
        {
            TShockAPI.TShock.Log.Warn($"[MultiSEngine.TShock] Rejected MSE2 packet from slot {who}: {decoded.Status} ({decoded.Error}).");
            return;
        }

        var envelope = decoded.Envelope;
        if (string.Equals(envelope.PacketType, BuiltInCustomPacketTypes.SyncIp, StringComparison.Ordinal))
        {
            HandleSyncIp(who, envelope, cfg);
            return;
        }

        var player = (uint)who < (uint)TShockAPI.TShock.Players.Length ? TShockAPI.TShock.Players[who] : null;
        MultiSEngineBridge.RaiseCustomPacketReceived(new CustomPacketReceivedEventArgs(
            who,
            player,
            envelope.PacketType,
            envelope.Payload,
            envelope.IsAuthenticated));
    }

    private void HandleSyncIp(int who, CustomPacketEnvelope envelope, TShockBridgeConfig cfg)
    {
        var proxyIp = RealIpApplicator.GetSocketIp(who);
        if (!cfg.IsTrustedProxyIp(proxyIp))
        {
            TShockAPI.TShock.Log.Warn($"[MultiSEngine.TShock] Blocked mse.sync_ip from untrusted source IP '{proxyIp ?? "unknown"}' on slot {who}.");
            return;
        }

        string playerName;
        string realIp;
        try
        {
            (playerName, realIp) = BuiltInCustomPacketTypes.DecodeSyncIpPayload(envelope.Payload);
        }
        catch (Exception ex)
        {
            TShockAPI.TShock.Log.Warn($"[MultiSEngine.TShock] Malformed mse.sync_ip payload on slot {who}: {ex.Message}");
            return;
        }

        if (!RealIpApplicator.TryApply(who, realIp, out var normalizedIp))
        {
            TShockAPI.TShock.Log.Warn($"[MultiSEngine.TShock] Invalid IP '{realIp}' in mse.sync_ip for '{playerName}' (slot {who}).");
            return;
        }

        _slots[who] = new ProxySlotState(normalizedIp, proxyIp ?? string.Empty, playerName);

        if (cfg.EnforceIpBansAfterSync &&
            (uint)who < (uint)TShockAPI.TShock.Players.Length &&
            TShockAPI.TShock.Players[who] is { } player &&
            RealIpApplicator.TryFindActiveIpBan(player, normalizedIp, out var banReason))
        {
            player.Disconnect($"Banned: {banReason}");
            return;
        }

        TShockAPI.TShock.Log.ConsoleInfo($"[MultiSEngine.TShock] Restored real IP {normalizedIp} for '{playerName}' (slot {who}).");
    }

    private void OnServerJoin(JoinEventArgs args)
    {
        if (args.Handled)
            return;

        var who = args.Who;
        if ((uint)who >= (uint)TShockAPI.TShock.Players.Length)
            return;

        if (_slots.TryGetValue(who, out var state) && TShockAPI.TShock.Players[who] is { } player)
        {
            RealIpApplicator.ReapplyToPlayer(player, state.SyncedIp);
            if (_config.EnforceIpBansAfterSync &&
                RealIpApplicator.TryFindActiveIpBan(player, state.SyncedIp, out var banReason))
            {
                player.Disconnect($"Banned: {banReason}");
                args.Handled = true;
            }
        }
    }

    private void OnGreetPlayer(GreetPlayerEventArgs args)
    {
        if (args.Handled)
            return;

        var who = args.Who;
        if ((uint)who >= (uint)TShockAPI.TShock.Players.Length)
            return;

        var player = TShockAPI.TShock.Players[who];
        if (player is null)
            return;

        var cfg = _config;
        if (_slots.TryGetValue(who, out var state))
        {
            RealIpApplicator.ReapplyToPlayer(player, state.SyncedIp);
            return;
        }

        if (!cfg.AllowDirectJoin)
        {
            TShockAPI.TShock.Log.Warn($"[MultiSEngine.TShock] Disconnected direct connection '{player.Name}' ({player.IP}) because AllowDirectJoin=false.");
            player.Disconnect(cfg.DirectJoinKickMessage);
            args.Handled = true;
        }
    }

    private void OnServerLeave(LeaveEventArgs args)
    {
        _slots.TryRemove(args.Who, out _);
    }

    private void OnReload(ReloadEventArgs args)
    {
        ReloadConfig();
        args.Player.SendSuccessMessage("[MultiSEngine.TShock] Configuration reloaded.");
    }

    private void OnReloadCommand(CommandArgs args)
    {
        ReloadConfig();
        args.Player.SendSuccessMessage("[MultiSEngine.TShock] Configuration reloaded.");
    }

    private void OnSwitchCommand(CommandArgs args)
    {
        if (!args.Player.RealPlayer)
        {
            args.Player.SendErrorMessage("This command can only be executed by an in-game player.");
            return;
        }

        if (args.Parameters.Count < 1 || string.IsNullOrWhiteSpace(args.Parameters[0]))
        {
            args.Player.SendErrorMessage("Usage: /mseswitch <server>");
            return;
        }

        var target = args.Parameters[0];
        if (MultiSEngineBridge.SendSwitchServer(args.Player, target))
        {
            args.Player.SendInfoMessage($"Requesting MultiSEngine transfer to '{target}'...");
        }
        else
        {
            args.Player.SendErrorMessage("Failed to send transfer request to MultiSEngine (check SharedSecret configuration).");
        }
    }

    private void ReloadConfig()
    {
        try
        {
            _config = TShockBridgeConfig.LoadOrCreate(_configPath);
        }
        catch (Exception ex)
        {
            TShockAPI.TShock.Log.ConsoleError($"[MultiSEngine.TShock] Failed loading {_configPath}: {ex}");
        }
    }
}
