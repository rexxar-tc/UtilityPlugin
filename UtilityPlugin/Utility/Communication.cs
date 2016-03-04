using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NLog;
using Sandbox.Common;
using Sandbox.ModAPI;
using SEModAPIExtensions.API;
using VRage.Game;
using VRageMath;

namespace UtilityPlugin.Utility
{
    public class Communication
    {
        public enum DataMessageType
        {
            Test = 5000,
            VoxelHeader,
            VoxelPart,
            Message,
            RemoveStubs,
            ChangeServer,
            ServerSpeed,
            Credits,

            //skipped a few addresses to avoid conflict
            //just in case
            Dialog = 5020,
            Move,
            Notification,
            MaxSpeed,
            ServerInfo,
            Waypoint
        }

        private static readonly Logger Log = LogManager.GetLogger("PluginLog");

        public static void SendPointsList( List<LineStruct> pointsList )
        {
            var messageString = MyAPIGateway.Utilities.SerializeToXML( pointsList );
            var data = Encoding.UTF8.GetBytes( messageString );
            BroadcastDataMessage(DataMessageType.Test, data );
        }

        public struct LineStruct
        {
            public LineStruct( Vector3D start, Vector3D end )
            {
                startPoint = start;
                endPoint = end;
            }
            public Vector3D startPoint;
            public Vector3D endPoint;
        }

        public static void SendPublicInformation(string infoText)
        {
            if (infoText == "")
                return;

            var MessageItem = new ServerMessageItem();
            MessageItem.From = PluginSettings.Instance.ServerChatName;
            MessageItem.Message = infoText;

            var messageString = MyAPIGateway.Utilities.SerializeToXML(MessageItem);
            var data = Encoding.UTF8.GetBytes(messageString);

            if (ChatManager.EnableData)
            {
                BroadcastDataMessage(DataMessageType.Message, data);
            }
            else
                ChatManager.Instance.SendPublicChatMessage(infoText);

            ChatManager.Instance.AddChatHistory(new ChatManager.ChatEvent(DateTime.Now, 0, infoText));
        }

        public static void SendPrivateInformation(ulong playerId, string infoText)
        {
            if (infoText == "")
                return;

            var messageItem = new ServerMessageItem
            {
                From = PluginSettings.Instance.ServerChatName,
                Message = infoText
            };

            var messageString = MyAPIGateway.Utilities.SerializeToXML(messageItem);
            var data = Encoding.UTF8.GetBytes(messageString);

            if (ChatManager.EnableData)
            {
                SendDataMessage(playerId, DataMessageType.Message, data);
            }
            else
                ChatManager.Instance.SendPrivateChatMessage(playerId, infoText);

            var chatItem = new ChatManager.ChatEvent
            {
                Timestamp = DateTime.Now,
                RemoteUserId = 0,
                Message = infoText
            };
            ChatManager.Instance.AddChatHistory(chatItem);
        }

        public static void Notification(ulong steamId, MyFontEnum color, int timeInSeconds, string message)
        {
            var MessageItem = new ServerNotificationItem();
            MessageItem.color = color;
            MessageItem.time = timeInSeconds;
            MessageItem.message = message;

            var messageString = MyAPIGateway.Utilities.SerializeToXML(MessageItem);
            var data = Encoding.UTF8.GetBytes(messageString);

            if (steamId != 0)
                SendDataMessage(steamId, DataMessageType.Notification, data);
            else
                BroadcastDataMessage(DataMessageType.Notification, data);
        }

        public static void DisplayDialog(ulong steamId, string header, string subheader, string content,
            string buttonText = "OK")
        {
            var MessageItem = new ServerDialogItem();
            MessageItem.title = header;
            MessageItem.header = subheader;
            MessageItem.content = content;
            MessageItem.buttonText = buttonText;

            var messageString = MyAPIGateway.Utilities.SerializeToXML(MessageItem);
            var data = Encoding.UTF8.GetBytes(messageString);

            SendDataMessage(steamId, DataMessageType.Dialog, data);
        }

        public static void DisplayDialog(ulong steamId, ServerDialogItem MessageItem)
        {
            var messageString = MyAPIGateway.Utilities.SerializeToXML(MessageItem);
            var data = Encoding.UTF8.GetBytes(messageString);

            SendDataMessage(steamId, DataMessageType.Dialog, data);
        }

        public static void MoveMessage(ulong steamId, string moveType, double x, double y, double z, long entityId = 0)
        {
            var MoveItem = new ServerMoveItem();
            MoveItem.moveType = moveType;
            MoveItem.x = x;
            MoveItem.y = y;
            MoveItem.z = z;
            MoveItem.entityId = entityId;

            var messageString = MyAPIGateway.Utilities.SerializeToXML(MoveItem);
            var data = Encoding.UTF8.GetBytes(messageString);
            if (steamId != 0)
                SendDataMessage(steamId, DataMessageType.Move, data);
            else
                BroadcastDataMessage(DataMessageType.Move, data);
        }

        public static void MoveMessage(ulong steamId, string moveType, Vector3D position)
        {
            var MoveItem = new ServerMoveItem();
            MoveItem.moveType = moveType;
            MoveItem.x = position.X;
            MoveItem.y = position.Y;
            MoveItem.z = position.Z;

            var messageString = MyAPIGateway.Utilities.SerializeToXML(MoveItem);
            var data = Encoding.UTF8.GetBytes(messageString);

            SendDataMessage(steamId, DataMessageType.Move, data);
        }

        public static void SendDataMessage(ulong steamId, DataMessageType messageType, byte[] data)
        {
            //this may be unsafe, but whatever, my sanity requires the enum
            var msgId = (long) messageType;

            //TODO: Check for max message size of 4kB
            var msgIdString = msgId.ToString();
            var newData = new byte[data.Length + msgIdString.Length + 1];
            newData[0] = (byte) msgIdString.Length;
            for (var r = 0; r < msgIdString.Length; r++)
                newData[r + 1] = (byte) msgIdString[r];

            Buffer.BlockCopy(data, 0, newData, msgIdString.Length + 1, data.Length);

            Wrapper.GameAction(() => { MyAPIGateway.Multiplayer.SendMessageTo(9000, newData, steamId); });
            //ServerNetworkManager.SendDataMessage( 9000, newData, steamId );
        }

        public static void BroadcastDataMessage(DataMessageType messageType, byte[] data)
        {
            //this may be unsafe, but whatever, my sanity requires the enum
            var msgId = (long) messageType;

            var msgIdString = msgId.ToString();
            var newData = new byte[data.Length + msgIdString.Length + 1];
            newData[0] = (byte) msgIdString.Length;
            for (var r = 0; r < msgIdString.Length; r++)
                newData[r + 1] = (byte) msgIdString[r];

            Buffer.BlockCopy(data, 0, newData, msgIdString.Length + 1, data.Length);

            Wrapper.GameAction(() => { MyAPIGateway.Multiplayer.SendMessageToOthers(9000, newData); });
        }

        public class ServerMessageItem
        {
            public string From { get; set; }

            public string Message { get; set; }
        }

        public class ServerDialogItem
        {
            public string title { get; set; }

            public string header { get; set; }

            public string content { get; set; }

            public string buttonText { get; set; }
        }

        public class ServerNotificationItem
        {
            public MyFontEnum color { get; set; }

            public int time { get; set; }

            public string message { get; set; }
        }

        public class ServerMoveItem
        {
            public string moveType { get; set; }

            public double x { get; set; }

            public double y { get; set; }

            public double z { get; set; }

            public long entityId { get; set; }
        }
    }
}