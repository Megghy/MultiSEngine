using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Logging;
using MultiSEngine.Protocol.Terraria;

namespace MultiSEngine.Runtime.Infrastructure;

public sealed class ConfigStore : IConfigStore
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly string _configPath;
    private readonly IProxyLogger _logger;
    private readonly Lock _ioLock = new();
    private ProxyConfig _current;

    public ConfigStore(string configPath, IProxyLogger logger, ProxyConfig? initialConfig = null)
    {
        _configPath = configPath;
        _logger = logger;
        _current = initialConfig is not null ? ValidateAndNormalize(initialConfig) : LoadOrCreateInternal();
    }

    public ProxyConfig Current => Volatile.Read(ref _current);
    public event Action<ProxyConfig>? Reloaded;

    public ProxyConfig Reload()
    {
        ProxyConfig next;
        lock (_ioLock)
        {
            next = LoadOrCreateInternal();
            Volatile.Write(ref _current, next);
        }

        Reloaded?.Invoke(next);
        return next;
    }

    public void Save(ProxyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var normalized = ValidateAndNormalize(config);
        lock (_ioLock)
        {
            WriteAtomically(_configPath, JsonSerializer.Serialize(normalized, SerializerOptions));
            Volatile.Write(ref _current, normalized);
        }
    }

    private ProxyConfig LoadOrCreateInternal()
    {
        if (!File.Exists(_configPath))
        {
            _logger.Info($"Config file not found at '{_configPath}', creating default configuration.", "Config");
            var created = ValidateAndNormalize(ProxyConfig.CreateDefault());
            WriteAtomically(_configPath, JsonSerializer.Serialize(created, SerializerOptions));
            return created;
        }

        try
        {
            var json = File.ReadAllText(_configPath, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<ProxyConfig>(json, SerializerOptions)
                ?? throw new InvalidDataException("Deserialized configuration was null.");
            return ValidateAndNormalize(parsed);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load config from '{_configPath}', retaining previous configuration.", ex, "Config");
            return _current ?? ValidateAndNormalize(ProxyConfig.CreateDefault());
        }
    }

    private ProxyConfig ValidateAndNormalize(ProxyConfig config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = ImmutableArray.CreateBuilder<ServerDescriptor>();

        if (!config.Servers.IsDefaultOrEmpty)
        {
            foreach (var server in config.Servers)
            {
                if (server is null || string.IsNullOrWhiteSpace(server.Name))
                {
                    _logger.Warn("Removed a server entry with an empty name from configuration.", "Config");
                    continue;
                }

                var trimmedName = server.Name.Trim();
                if (!seen.Add(trimmedName))
                {
                    _logger.Warn($"Duplicate server name '{trimmedName}' found in configuration; redundant entry removed.", "Config");
                    continue;
                }

                if (server.Port is < 1 or > 65535)
                {
                    _logger.Warn($"Server '{trimmedName}' has invalid port {server.Port}; entry skipped.", "Config");
                    continue;
                }

                if (server.VersionNum > 0 && TerrariaVersions.ToVersionString(server.VersionNum) == "Unknown")
                {
                    _logger.Warn($"Server '{trimmedName}' specifies an unknown protocol version ({server.VersionNum}).", "Config");
                }

                builder.Add(server with { Name = trimmedName });
            }
        }

        if (TerrariaVersions.ToVersionString(config.ServerVersion) == "Unknown")
            _logger.Warn($"Global ServerVersion ({config.ServerVersion}) is not a known Terraria protocol version.", "Config");

        var limits = config.Limits ?? new ProxyLimitsConfig();
        var customPackets = config.CustomPackets ?? new CustomPacketSecurityConfig();

        return config with
        {
            ListenIP = string.IsNullOrWhiteSpace(config.ListenIP) ? "0.0.0.0" : config.ListenIP.Trim(),
            ListenPort = config.ListenPort is >= 0 and <= 65535 ? config.ListenPort : 7778,
            SwitchTimeoutMs = Math.Max(1000, config.SwitchTimeoutMs),
            PasswordTimeoutMs = Math.Max(5000, config.PasswordTimeoutMs),
            HandshakeTimeoutMs = Math.Max(1000, config.HandshakeTimeoutMs),
            Limits = limits with
            {
                MaxConnections = Math.Max(1, limits.MaxConnections),
                SendPipePauseThresholdBytes = Math.Max(4096, limits.SendPipePauseThresholdBytes),
                SendPipeResumeThresholdBytes = Math.Clamp(limits.SendPipeResumeThresholdBytes, 1024, Math.Max(4096, limits.SendPipePauseThresholdBytes)),
                SendFlushTimeoutMs = Math.Max(500, limits.SendFlushTimeoutMs),
                MaxPreConnectBufferedBytes = Math.Max(64 * 1024, limits.MaxPreConnectBufferedBytes),
                MaxHandshakePacketsPerSecond = Math.Max(16, limits.MaxHandshakePacketsPerSecond),
                SessionMailboxCapacity = Math.Max(16, limits.SessionMailboxCapacity),
            },
            CustomPackets = customPackets with
            {
                MaxClockSkewSeconds = Math.Max(1, customPackets.MaxClockSkewSeconds),
            },
            Servers = builder.ToImmutable(),
        };
    }

    internal static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, path, overwrite: true);
    }
}

public sealed class LocalizationStore : ILocalizationStore
{
    public const string DefaultLocalizationJson = """
{
  "Prefix": "<[C/B1DAE4:MultiSEngine]> ",
  "Command_NotJoined": "你尚未加入服务器",
  "Command_IsSwitching": "正在跳转中, 请勿使用此命令",
  "Command_AlreadyIn": "你已处于服务器 {0} 中",
  "Command_Switch": "正在传送至服务器 {0}",
  "Command_Back": "已返回主服务器",
  "Command_Kick": "你已被移出服务器 {0}: {1}",
  "Command_AvailableServer": "可用的服务器: ",
  "Command_ServerNotFound": "未找到含有关键词 {0} 的服务器",
  "Help_Tp": "/mse tp([c/B3CE95:t]) <[c/B3CE95:服务器名]> -- 传送到指定服务器",
  "Help_Back": "/mse back([c/B3CE95:b]) -- 传送回主服务器",
  "Help_List": "/mse list([c/B3CE95:l]) -- 列出所有可用的服务器",
  "Help_Password": "/mse password([c/B3CE95:p]) <[c/B3CE95:密码]> -- 向当前连接到的服务器发送指定的密码",
  "Prompt_VersionNotAllowed": "不允许的游戏版本: {0}",
  "Prompt_NoAvailableServer": "无可用服务器, 你已被传送回临时世界",
  "Prompt_InvalidFormat": "无效的格式",
  "Prompt_PasswordTimeout": "输入密码时超时",
  "Prompt_WrongPassword": "错误的服务器密码",
  "Prompt_UnknownError": "由于不可预料的错误, 你已被传送回主服务器",
  "Prompt_CannotConnect": "无法连接至服务器 {0}",
  "Prompt_UnknownAddress": "无效的服务器地址",
  "Prompt_DefaultServerNotFound": "未找到指定的默认服务器: {0}",
  "Prompt_CommandFailed": "执行指令失败",
  "Prompt_Disconnect": "你已被移出服务器 {0}: {1}",
  "Prompt_NeedPassword": "服务器 {0} 需要密码, 请输入 {1}",
  "Prompt_ConnectSuccess": "成功传送至服务器 {0}"
}
""";

    private readonly string? _path;
    private readonly IProxyLogger _logger;
    private readonly Lock _ioLock = new();
    private ImmutableDictionary<string, string> _entries;

    public LocalizationStore(string? path, IProxyLogger logger)
    {
        _path = path;
        _logger = logger;
        _entries = ParseJson(DefaultLocalizationJson);
        if (!string.IsNullOrWhiteSpace(path))
            Reload();
    }

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        return Volatile.Read(ref _entries).TryGetValue(key, out var value) ? value : key;
    }

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        if (args.Length == 0)
            return template;

        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public void Reload()
    {
        if (string.IsNullOrWhiteSpace(_path))
            return;

        lock (_ioLock)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    ConfigStore.WriteAtomically(_path, DefaultLocalizationJson);
                    Volatile.Write(ref _entries, ParseJson(DefaultLocalizationJson));
                    return;
                }

                var json = File.ReadAllText(_path, Encoding.UTF8);
                var parsed = ParseJson(json);
                var merged = ParseJson(DefaultLocalizationJson).SetItems(parsed);
                Volatile.Write(ref _entries, merged);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to reload localization file '{_path}': {ex.Message}", "Localization");
            }
        }
    }

    private static ImmutableDictionary<string, string> ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in document.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
                builder[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }
        return builder.ToImmutable();
    }
}

public sealed class MotdStore : IMotdStore
{
    public const string DefaultMotd = """
欢迎来到 {name} 服务器!
当前服务器在线 [C/97ADD6:{online}] 人: {players}
可用服务器: [C/B1DAE4:{servers}]
使用 [C/B6E1C1:/mse tp <服务器名>] 来传送到指定服务器
""";

    private readonly string? _path;
    private readonly IProxyLogger _logger;
    private readonly Lock _ioLock = new();
    private string _template = DefaultMotd;

    public MotdStore(string? path, IProxyLogger logger)
    {
        _path = path;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(path))
            Reload();
    }

    public string Render(string serverName, int onlineCount, IEnumerable<string> playerNames, IEnumerable<string> visibleServers)
    {
        var template = Volatile.Read(ref _template);
        return template
            .Replace("{online}", onlineCount.ToString(), StringComparison.Ordinal)
            .Replace("{name}", serverName, StringComparison.Ordinal)
            .Replace("{players}", string.Join(", ", playerNames), StringComparison.Ordinal)
            .Replace("{servers}", string.Join(", ", visibleServers), StringComparison.Ordinal);
    }

    public void Reload()
    {
        if (string.IsNullOrWhiteSpace(_path))
            return;

        lock (_ioLock)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    ConfigStore.WriteAtomically(_path, DefaultMotd);
                    Volatile.Write(ref _template, DefaultMotd);
                    return;
                }

                Volatile.Write(ref _template, File.ReadAllText(_path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to reload MOTD file '{_path}': {ex.Message}", "Motd");
            }
        }
    }
}

public static class NetworkAddressResolver
{
    public static IPAddress ParseListenAddress(string? listenIp)
    {
        if (string.IsNullOrWhiteSpace(listenIp) || listenIp == "0.0.0.0" || listenIp == "*")
            return IPAddress.Any;

        if (string.Equals(listenIp, "localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;

        if (IPAddress.TryParse(listenIp, out var ip))
            return ip;

        throw new FormatException($"Invalid ListenIP address: '{listenIp}'.");
    }

    public static async ValueTask<IPAddress?> ResolveHostAsync(string? host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;

        if (IPAddress.TryParse(host, out var literal))
            return literal;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            return addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
