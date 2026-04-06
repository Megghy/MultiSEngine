using System.Text.Json;
using MultiSEngine;

namespace TestSupport;

internal sealed class TemporaryWorkspace : IDisposable
{
    private readonly string _originalCurrentDirectory = Environment.CurrentDirectory;

    public TemporaryWorkspace(string name)
    {
        RootPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "MultiSEngine.Tests",
            name,
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(RootPath);
        Environment.CurrentDirectory = RootPath;
        Config.Reload();
    }

    public string RootPath { get; }

    public string GetPath(string relativePath)
        => System.IO.Path.Combine(RootPath, relativePath);

    public void WriteText(string relativePath, string content)
    {
        var path = GetPath(relativePath);
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    public void WriteJsonFile<T>(string relativePath, T value, JsonSerializerOptions? options = null)
        => WriteText(relativePath, JsonSerializer.Serialize(value, options ?? new JsonSerializerOptions { WriteIndented = true }));

    public void WriteConfig(Config config)
    {
        WriteText("Config.json", JsonSerializer.Serialize(config, Config.DefaultSerializerOptions));
        Config.Reload();
    }

    public void Dispose()
    {
        Config.Reload();
        Environment.CurrentDirectory = _originalCurrentDirectory;
        Config.Reload();

        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
        }
    }
}
