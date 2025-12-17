using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.EventArgs;

namespace MultiSEngine.Core
{
    public class Hooks
    {
        public static class HookDelegates
        {
            public delegate void PlayerJoinEvent(PlayerJoinEventArgs args);

            public delegate void PlayerLeaveEvent(PlayerLeaveEventArgs args);

            public delegate void RecieveCustomPacketEvent(RecieveCustomPacketEventArgs args);

            public delegate void PreSwitchEvent(SwitchEventArgs args);

            public delegate void PostSwitchEvent(SwitchEventArgs args);

            public delegate void ChatEvent(ChatEventArgs args);

            //public delegate void SendPacketEvent(SendPacketEventArgs args);

            //public delegate void RecievePacketEvent(GetPacketEventArgs args);
        }

        public static event HookDelegates.PlayerJoinEvent PlayerJoin;
        public static event HookDelegates.PlayerLeaveEvent PlayerLeave;
        public static event HookDelegates.RecieveCustomPacketEvent RecieveCustomData;
        public static event HookDelegates.PreSwitchEvent PreSwitch;
        public static event HookDelegates.PostSwitchEvent PostSwitch;
        public static event HookDelegates.ChatEvent Chat;
        //public static event HookDelegates.SendPacketEvent SendPacket;
        //public static event HookDelegates.RecievePacketEvent RecievePacket;
        internal static bool OnPlayerJoin(ClientData client, string ip, int port, string version, out PlayerJoinEventArgs args)
        {
            args = new(client, ip, port, version);
            try
            {
                PlayerJoin?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logs.Error($"<PlayerJoin> Hook handling failed.{Environment.NewLine}{ex}");
            }
            return args.Handled;
        }
        internal static bool OnPlayerLeave(ClientData client, out PlayerLeaveEventArgs args)
        {
            args = new(client);
            try
            {
                PlayerLeave?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logs.Error($"<PlayerLeave> Hook handling failed.{Environment.NewLine}{ex}");
            }
            return args.Handled;
        }
        internal static bool OnRecieveCustomData(ClientData client, Packet packet, BinaryReader reader, out RecieveCustomPacketEventArgs args)
        {
            var position = reader.BaseStream.Position;
            args = new(client, packet, reader);
            try
            {
                args.Reader.BaseStream.Position = 3L;
                RecieveCustomData?.Invoke(args);
                args.Reader.BaseStream.Position = position;
            }
            catch (Exception ex)
            {
                Logs.Error($"<RecieveCustomData> Hook handling failed.{Environment.NewLine}{ex}");
            }
            return args.Handled;
        }
        internal static bool OnPreSwitch(ClientData client, ServerInfo targetServer, out SwitchEventArgs args)
        {
            args = new(client, targetServer, true);
            try
            {
                PreSwitch?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logs.Error($"<PreSwitch> Hook handling failed.{Environment.NewLine}{ex}");
            }
            return args.Handled;
        }
        internal static bool OnPostSwitch(ClientData client, ServerInfo targetServer, out SwitchEventArgs args)
        {
            args = new(client, targetServer, false);
            try
            {
                PostSwitch?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logs.Error($"<PostSwitch> Hook handling failed.{Environment.NewLine}{ex}");
            }
            return args.Handled;
        }
        internal static bool OnChat(ClientData client, NetTextModuleC2S module, out ChatEventArgs args)
        {
            args = new(client, module.Text);
            try
            {
                Chat?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logs.Error($"<Chat> Hook handling failed.{Environment.NewLine}{ex}");
            }
            return args.Handled;
        }
        /*internal static bool OnSendData(ClientData client, Packet packet, bool toClient, out SendPacketEventArgs args)
        {
            args = new(client, packet, toClient);
            try
            {
                SendPacket?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logs.Error($"<SendPacket> Hook handling failed.{Environment.NewLine}{ex}");
            }
            return args.Handled;
        }
        internal static bool OnGetData(ClientData client, ref Span<byte> buf, bool fromClient, out GetPacketEventArgs args)
        {
            args = new(client, ref buf, fromClient);
            try
            {
                RecievePacket?.Invoke(args);
            }
            catch (Exception ex)
            {
                Logs.Error($"<GetPacket> Hook handling failed.{Environment.NewLine}{ex}");
            }
            return args.Handled;
        }*/
    }
}
