using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TrProtocol;

namespace MultiSEngine.Core
{
    public abstract class MSEPlugin
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string Author { get; }
        public abstract Version Version { get; }
        public abstract void Initialize();
        public abstract void Dispose();
        public virtual PacketSerializer Serializer { get; } = new(true);
    }
    public class PluginSystem
    {
        public static readonly string PluginPath = Path.Combine(Environment.CurrentDirectory, "Plugins");
        public static readonly List<MSEPlugin> PluginList = new();
        internal static void Load()
        {
            if (!Directory.Exists(PluginPath))
                Directory.CreateDirectory(PluginPath);
            Directory.GetFiles(PluginPath, "*.dll").ForEach(p =>
            {
                Assembly plugin = Assembly.LoadFile(p);
                if (plugin.GetTypes().Where(t => t.BaseType == typeof(MSEPlugin))?.ToList() is { Count: > 0 } instances)
                {
                    instances.ForEach(instance =>
                    {
                        try
                        {
                            var pluginInstance = Activator.CreateInstance(instance) as MSEPlugin;
                            pluginInstance.Initialize();
                            Logs.Success($"- Loaded plugin: {pluginInstance.Name} <{pluginInstance.Author}> V{pluginInstance.Version}");
                            PluginList.Add(pluginInstance);
                        }
                        catch (Exception ex)
                        {
                            Logs.Warn($"Failed to load plugin: {p}{Environment.NewLine}{ex}");
                        }
                    });
                }
            });
        }
        internal static void Unload()
        {
            PluginList.ForEach(p =>
            {
                p.Dispose();
                Logs.Text($"Plugin: {p.Name} disposed.");
            });
            PluginList.Clear();
        }
    }
}
