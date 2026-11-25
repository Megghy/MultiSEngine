namespace MultiSEngine.Core.Configuration;

public interface IConfigStore
{
    ProxyConfig Current { get; }
    event Action<ProxyConfig>? Reloaded;
    ProxyConfig Reload();
    void Save(ProxyConfig config);
}

public interface ILocalizationStore
{
    string Get(string key);
    string Format(string key, params object?[] args);
    void Reload();
}

public interface IMotdStore
{
    string Render(string serverName, int onlineCount, IEnumerable<string> playerNames, IEnumerable<string> visibleServers);
    void Reload();
}
