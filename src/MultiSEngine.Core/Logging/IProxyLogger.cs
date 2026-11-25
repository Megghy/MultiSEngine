namespace MultiSEngine.Core.Logging;

public enum ProxyLogLevel
{
    Debug,
    Text,
    Info,
    Success,
    Warn,
    Error,
}

public interface IProxyLogger
{
    void Log(ProxyLogLevel level, string category, string message, Exception? exception = null);

    void Debug(string message, string category = "Core") => Log(ProxyLogLevel.Debug, category, message);
    void Text(string message, string category = "Core") => Log(ProxyLogLevel.Text, category, message);
    void Info(string message, string category = "Core") => Log(ProxyLogLevel.Info, category, message);
    void Success(string message, string category = "Core") => Log(ProxyLogLevel.Success, category, message);
    void Warn(string message, string category = "Core") => Log(ProxyLogLevel.Warn, category, message);
    void Error(string message, Exception? exception = null, string category = "Core") => Log(ProxyLogLevel.Error, category, message, exception);
}
