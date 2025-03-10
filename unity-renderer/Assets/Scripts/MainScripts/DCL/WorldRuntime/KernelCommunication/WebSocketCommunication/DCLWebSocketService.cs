using System.IO;
using System.Text;
using DCL;
using DCL.Interface;
using Newtonsoft.Json;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
using BinaryWriter = KernelCommunication.BinaryWriter;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

public class DCLWebSocketService : WebSocketBehavior
{
    public static bool VERBOSE = false;

    private void SendMessageToWeb(string type, string message)
    {
#if (UNITY_EDITOR || UNITY_STANDALONE)
        var x = new Message()
        {
            type = type,
            payload = message
        };

        if (ConnectionState == WebSocketState.Open)
        {
            var serializeObject = JsonConvert.SerializeObject(x);
            
            Send(serializeObject);
        
            if (VERBOSE)
            {
                Debug.Log("SendMessageToWeb: " + type);
            }
        }
#endif
    }

    private void SendBinaryMessageToKernel(string sceneId, byte[] data)
    {
#if (UNITY_EDITOR || UNITY_STANDALONE)
        using (var memoryStream = new MemoryStream())
        {
            using (var binaryWriter = new BinaryWriter(memoryStream))
            { 
                byte[] sceneIdBuffer = Encoding.UTF8.GetBytes(sceneId);
                binaryWriter.WriteInt32(sceneIdBuffer.Length);
                binaryWriter.WriteBytes(sceneIdBuffer);
                binaryWriter.WriteBytes(data);
                Send(memoryStream.ToArray());
            }
        }
#endif
    }

    public class Message
    {
        public string type;
        public string payload;

        public override string ToString() { return string.Format("type = {0}... payload = {1}...", type, payload); }
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        base.OnMessage(e);

        lock (WebSocketCommunication.queuedMessages)
        {
            Message finalMessage = JsonUtility.FromJson<Message>(e.Data);

            WebSocketCommunication.queuedMessages.Enqueue(finalMessage);
            WebSocketCommunication.queuedMessagesDirty = true;
        }
    }

    protected override void OnError(ErrorEventArgs e)
    {
        Debug.LogError(e.Message);
        base.OnError(e);
    }

    protected override void OnClose(CloseEventArgs e)
    {
        base.OnClose(e);
        WebInterface.OnMessageFromEngine -= SendMessageToWeb;
        WebInterface.OnBinaryMessageFromEngine -= SendBinaryMessageToKernel;
        DataStore.i.wsCommunication.communicationEstablished.Set(false);
    }

    protected override void OnOpen()
    {
        Debug.Log("WebSocket Communication Established");
        base.OnOpen();

        WebInterface.OnMessageFromEngine += SendMessageToWeb;
        WebInterface.OnBinaryMessageFromEngine += SendBinaryMessageToKernel;
        DataStore.i.wsCommunication.communicationEstablished.Set(true);
    }
}
