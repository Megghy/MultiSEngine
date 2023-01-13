using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MultiSEngine.DataStruct;
using TrProtocol;

namespace MultiSEngine.Core
{
    public interface IMSEPlugin
    {
        public string Name { get; }
        public string Description { get; }
        public string Author { get; }
        public Version Version { get; }
        public void Initialize();
        public void Dispose();
        public PacketSerializer Serializer => Net.DefaultClientSerializer;
    }
    public class PluginSystem
    {
        public static readonly string PluginPath = Path.Combine(Environment.CurrentDirectory, "Plugins");
        public static readonly List<IMSEPlugin> PluginList = new();
        private static PluginHost<IMSEPlugin> _pluginHost;
        [AutoInit("Loading all plugins.")]
        internal static void Load()
        {
            if (!Directory.Exists(PluginPath))
                Directory.CreateDirectory(PluginPath);
            _pluginHost ??= new PluginHost<IMSEPlugin>();
            _pluginHost.LoadPlugins(PluginPath, plugin => Logs.Success($"- Loaded plugin: {plugin.Name} <{plugin.Author}> V{plugin.Version}"));

            Logs.Info($"{PluginList.Count} Plugin(s) loaded.");
        }
        internal static void Unload()
        {
            PluginList.ForEach(p =>
            {
                p.Dispose();
                Logs.Info($"- Disposed plugin: {p.Name}");
            });
            PluginList.Clear();
            _pluginHost.Unload();
            _pluginHost = null;
        }
        public static void Reload()
        {
            Unload();
            Load();
        }
        #region 插件加载类
        class PluginHost<TPlugin> where TPlugin : IMSEPlugin
        {
            private AssemblyLoadContext _pluginAssemblyLoadingContext;

            public PluginHost()
            {
                _pluginAssemblyLoadingContext = new AssemblyLoadContext("PluginAssemblyContext", isCollectible: true);
            }
            public static void RegisterPlugin(TPlugin plugin, Action<IMSEPlugin> registerCallback = null)
            {
                if (PluginList.Contains(plugin))
                    Logs.Warn($"Plugin:{plugin.Name} already loaded.");
                else
                {
                    try
                    {
                        plugin.Initialize();
                        PluginList.Add(plugin);
                        registerCallback?.Invoke(plugin);
                    }
                    catch (Exception ex)
                    {
                        Logs.Warn($"Failed to initialize plugin: {plugin.Name}{Environment.NewLine}{ex}");
                    }
                }
            }
            public void LoadPlugins(string pluginPath, Action<IMSEPlugin> registerCallback = null)
            {
                LoadPlugins(FindAssemliesWithPlugins(pluginPath), registerCallback);
            }
            public void LoadPlugins(IReadOnlyCollection<string> assembliesWithPlugins, Action<IMSEPlugin> registerCallback = null)
            {
                foreach (var assemblyPath in assembliesWithPlugins)
                {
                    var assembly = _pluginAssemblyLoadingContext.LoadFromAssemblyPath(assemblyPath);
                    var validPluginTypes = GetPluginTypes(assembly);
                    foreach (var pluginType in validPluginTypes)
                    {
                        var pluginInstance = (TPlugin)Activator.CreateInstance(pluginType);
                        RegisterPlugin(pluginInstance, registerCallback);
                    }
                }
            }

            /// <summary>
            /// 寻找存在指定类型的程序集路径
            /// </summary>
            /// <param name="path">路径</param>
            /// <returns></returns>
            [UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file", Justification = "<Pending>")]
            public static IReadOnlyCollection<string> FindAssemliesWithPlugins(string path)
            {
                var assemblies = Directory.GetFiles(path, "*.dll");
                var assemblyPluginInfos = new List<string>();
                var pluginFinderAssemblyContext = new AssemblyLoadContext(name: "PluginFinderAssemblyContext", isCollectible: true);
                foreach (var assemblyPath in assemblies)
                {
                    var assembly = pluginFinderAssemblyContext.LoadFromAssemblyPath(assemblyPath);
                    if (GetPluginTypes(assembly).Any())
                        assemblyPluginInfos.Add(assembly.Location);
                }
                pluginFinderAssemblyContext.Unload();
                return assemblyPluginInfos;
            }
            public static IReadOnlyCollection<Type> GetPluginTypes(Assembly assembly)
            {
                return assembly.GetTypes()
                                .Where(type =>
                                !type.IsAbstract &&
                                typeof(TPlugin).IsAssignableFrom(type))
                                .ToArray();
            }
            public void Unload()
            {
                _pluginAssemblyLoadingContext.Unload();
                _pluginAssemblyLoadingContext = null;
            }
        }
        #endregion
    }
}
