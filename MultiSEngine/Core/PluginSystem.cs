﻿using System;
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
        public virtual void GetPacket(Hooks.PacketEventArgs args)
        {
        }
        public virtual void SendPacket(Hooks.PacketEventArgs args)
        {
        }
        public virtual void GetCustomPacket(Hooks.RecieveCustomPacketEventArgs p)
        {
        }
        public virtual void OnChat(Hooks.ChatEventArgs args)
        {
        }
        public virtual void OnJoin(Hooks.PlayerJoinEventArgs args)
        {
        }
        public virtual void OnLeave(Hooks.PlayerLeaveEventArgs args)
        {
        }
        public virtual void PreSwitch(Hooks.SwitchEventArgs args)
        {
        }
        public virtual void PostSwitch(Hooks.SwitchEventArgs args)
        {
        }
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
                try
                {
                    Assembly plugin = Assembly.LoadFile(p);
                    if (plugin.GetTypes().FirstOrDefault(t => t.BaseType == typeof(MSEPlugin)) is { } mainType)
                    {
                        var pluginInstance = Activator.CreateInstance(mainType) as MSEPlugin;
                        pluginInstance.Initialize();
                        Logs.Success($"- Loaded plugin: {pluginInstance.Name} <{pluginInstance.Author}> V{pluginInstance.Version}");
                        PluginList.Add(pluginInstance);
                    }
                }
                catch (Exception ex)
                {
                    Logs.Warn($"Failed to load plugin: {p}{Environment.NewLine}{ex}");
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
        internal static void OnEvent(Hooks.IEventArgs e)
        {
            PluginList.ForEach(p =>
            {
                try
                {
                    switch (e)
                    {
                        case Hooks.ChatEventArgs chat:
                            p.OnChat(chat);
                            break;
                        case Hooks.PacketEventArgs packet:
                            if (packet.IsSend)
                                p.SendPacket(packet);
                            else
                                p.GetPacket(packet);
                            break;
                        case Hooks.PlayerJoinEventArgs join:
                            p.OnJoin(join);
                            break;
                        case Hooks.PlayerLeaveEventArgs leave:
                            p.OnLeave(leave);
                            break;
                        case Hooks.SwitchEventArgs s:
                            if (s.PreSwitch)
                                p.PreSwitch(s);
                            else
                                p.PostSwitch(s);
                            break;
                        case Hooks.RecieveCustomPacketEventArgs custom:
                            p.GetCustomPacket(custom);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logs.Error($"[Plugin] <{p.Name}> Hook handling failed.{Environment.NewLine}{ex}");
                }
            });
        }
    }
}
