/*************************************************************************************************
* Copyright 2022 Theai, Inc. (DBA Inworld)
*
* Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
* that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
*************************************************************************************************/
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Inworld.Grpc;
using Inworld.Packets;
using Inworld.Runtime;
using Inworld.Util;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using AudioChunk = Inworld.Packets.AudioChunk;
using ControlEvent = Inworld.Grpc.ControlEvent;
using CustomEvent = Inworld.Packets.CustomEvent;
using EmotionEvent = Inworld.Packets.EmotionEvent;
using GestureEvent = Inworld.Packets.GestureEvent;
using GrpcPacket = Inworld.Grpc.InworldPacket;
using InworldPacket = Inworld.Packets.InworldPacket;
using Routing = Inworld.Packets.Routing;
using TextEvent = Inworld.Packets.TextEvent;
using Inworld.NdkData;

namespace Inworld
{
    /// <summary>
    ///     This class used to save the communication data in runtime.
    /// </summary>
    class Connection
    {
        // Animation Chunks.
        internal readonly ConcurrentQueue<AnimationChunk> incomingAnimationQueue = new ConcurrentQueue<AnimationChunk>();
        // Audio chunks ready to play.
        internal readonly ConcurrentQueue<AudioChunk> incomingAudioQueue = new ConcurrentQueue<AudioChunk>();
        // Events that need to be processed by NPC.
        internal readonly ConcurrentQueue<InworldPacket> incomingInteractionsQueue = new ConcurrentQueue<InworldPacket>();
        // Events ready to send to server.
        internal readonly ConcurrentQueue<GrpcPacket> outgoingEventsQueue = new ConcurrentQueue<GrpcPacket>();
    }

    /// <summary>
    ///     This is the logic class for Server communication.
    /// </summary>
    class InworldClient
    {
        InworldNDKWrapper m_Wrapper;
        ConnectionStateCallbackType m_ConnectionCallback;
        PacketCallbackType m_PacketCallback;
        internal InworldClient()
        {
            m_ConnectionCallback = new ConnectionStateCallbackType(ConnectionStateCallback);
            m_PacketCallback = new PacketCallbackType(PacketCallback);
            m_Wrapper = new InworldNDKWrapper(m_ConnectionCallback, m_PacketCallback);// (ConnectionStateCallback, PacketCallback);//
        }

        //Callback will be invoked through the wrapper NDK side 
        //this callback will likely have to replace the OnPacketEvents functionality
        void PacketCallback(IntPtr packetwrapper)
        {
            Debug.Log("PACKET CALLBACK");
        }
        void ConnectionStateCallback(ConnectionState state)
        {
            Debug.Log("CONNECTION STATE IS " + state);
            switch (state)
            {
                case ConnectionState.Connected:
                    OnAuthCompleted();
                    break;
                case ConnectionState.Disconnected:
                    break;
                case ConnectionState.Failed:
                    OnAuthFailed("Connection state returned failed from NDK");
                    break;
            }

        }

        #region Private Variables
        readonly WorldEngine.WorldEngineClient m_WorldEngineClient;
        readonly Channel m_Channel;
        AsyncDuplexStreamingCall<GrpcPacket, GrpcPacket> m_StreamingCall;
        Connection m_CurrentConnection;
        InworldAuth m_InworldAuth;
        string m_SessionKey = "";
        Metadata m_Header;
        internal event Action<RuntimeStatus, string> RuntimeEvent;
        #endregion
        
        #region Wrapper variables
        ClientOptions m_Options = new ClientOptions();
        #endregion

        #region Properties
        internal ConcurrentQueue<Exception> Errors { get; } = new ConcurrentQueue<Exception>();
        internal bool SessionStarted { get; private set; }
        internal bool HasInit => !m_InworldAuth.IsExpired;
        internal string SessionID => m_InworldAuth?.SessionID ?? "";
        internal string LastState { get; set; }
        bool IsSessionInitialized => m_SessionKey.Length != 0;
        Timestamp Now => Timestamp.FromDateTime(DateTime.UtcNow);
        #endregion

        #region Call backs
        void OnAuthCompleted()
        {
            InworldAI.Log("Init Success!");
            //m_Header = new Metadata
            //{
            //    {"authorization", $"Bearer {m_InworldAuth.Token}"},
            //    {"session-id", m_InworldAuth.SessionID}
            //};
            RuntimeEvent?.Invoke(RuntimeStatus.InitSuccess, "");
        }
        void OnAuthFailed(string msg)
        {
            RuntimeEvent?.Invoke(RuntimeStatus.InitFailed, msg);
        }
        #endregion

        #region Private Functions
        LoadSceneCallbackType callback;
        internal void GetAppAuth()
        {
#if UNITY_EDITOR && VSP
if (!string.IsNullOrEmpty(InworldAI.User.Account))
VSAttribution.VSAttribution.SendAttributionEvent("Login Runtime", InworldAI.k_CompanyName, InworldAI.User.Account);
#endif
            //InitWrapperClient();
            m_Options.AuthUrl = InworldAI.Game.currentServer.studio;//"api-studio.inworld.ai";
            m_Options.LoadSceneUrl = InworldAI.Game.currentServer.RuntimeServer;//"api-engine.inworld.ai:443";
            m_Options.SceneName = InworldAI.Game.currentScene.fullName;//"workspaces/artem_v_test/scenes/demo";
            m_Options.PlayerName = InworldAI.User.Name;//"Player";
            m_Options.ApiKey = InworldAI.Game.APIKey;// "xgV17Aur4k33Qoox8OxepATdBeS0Bamu";//
            m_Options.ApiSecret = InworldAI.Game.APISecret;// "zSpQ2d0wObaPWCiRS2oioh8nDPVf1zkg2OISrdx8Zs47bm8rpdki3s6nejza0aki";//InworldAI.Game.APISecret;
            Debug.Log("GetAppAuth with wrapper options");
            callback =  new LoadSceneCallbackType(LoadSceneCallback);
            byte[] serializedData = m_Options.ToByteArray();

            InworldNDKWrapper.ClientWrapper_StartClientWithCallback(m_Wrapper.instance, serializedData, 
                serializedData.Length, callback);
            
            //m_InworldAuth = new InworldAuth(OnAuthCompleted, OnAuthFailed);
            //m_InworldAuth.GenerateAccessToken(InworldAI.Game.StudioServer, InworldAI.Game.APIKey, InworldAI.Game.APISecret);
        }
        
        //Callback should be invoked through the wrapper in the NDK
        public void LoadSceneCallback(IntPtr serializedAgentInfoArray, int serializedAgentInfoArraySize)
        {
            byte[] serializedData = new byte[serializedAgentInfoArraySize];
            Marshal.Copy(serializedAgentInfoArray, serializedData, 0, serializedAgentInfoArraySize);
            AgentInfoArray agentInfoArray = AgentInfoArray.Parser.ParseFrom(serializedData);

            Debug.Log("LOADSCENE CALLBACK INVOKED FROM DLL ARRAY COUNT IS " + agentInfoArray.AgentInfoList.Count);

            foreach(AgentInfo agentInfo in agentInfoArray.AgentInfoList)
            {
                Debug.Log("first agent info name is " + agentInfo.GivenName);
            }
            // Now you can use agentInfoArray in your C# code

            // Free the allocated buffer
            //Marshal.FreeHGlobal(serializedAgentInfoArray);
            Marshal.FreeCoTaskMem(serializedAgentInfoArray);
        }
        
        internal async Task<LoadSceneResponse> LoadScene(string sceneName)
        {
            LoadSceneRequest lsRequest = new LoadSceneRequest
            {
                Name = sceneName,
                Capabilities = InworldAI.Settings.Capabilities,
                User = InworldAI.User.Request,
                Client = InworldAI.User.Client
            };
            if (!string.IsNullOrEmpty(LastState))
            {
                lsRequest.SessionContinuation = new SessionContinuation
                {
                    PreviousState = ByteString.FromBase64(LastState)
                };
            }
            try
            {
                LoadSceneResponse response = await m_WorldEngineClient.LoadSceneAsync(lsRequest, m_Header);
// Yan: They somehow use {WorkSpace}:{sessionKey} as "sessionKey" now. Need to remove the first part.
                m_SessionKey = response.Key.Split(':')[1];
                if (response.PreviousState != null)
                {
                    foreach (PreviousState.Types.StateHolder stateHolder in response.PreviousState.StateHolders)
                    {
                        InworldAI.Log($"Received Previous Packets: {stateHolder.Packets.Count}");
                    }
                }
                m_Header.Add("Authorization", $"Bearer {m_SessionKey}");
                RuntimeEvent?.Invoke(RuntimeStatus.LoadSceneComplete, m_SessionKey);
                return response;
            }
            catch (RpcException e)
            {
                RuntimeEvent?.Invoke(RuntimeStatus.LoadSceneFailed, e.ToString());
                return null;
            }
        }

        // Marks audio session start.
        internal void StartAudio(Routing routing)
        {
            InworldAI.Log("Start Audio Event");
            if (SessionStarted)
                m_CurrentConnection?.outgoingEventsQueue.Enqueue
                (
                    new GrpcPacket
                    {
                        Timestamp = Now,
                        Routing = routing.ToGrpc(),
                        Control = new ControlEvent
                        {
                            Action = ControlEvent.Types.Action.AudioSessionStart
                        }
                    }
                );
        }

        // Marks session end.
        internal void EndAudio(Routing routing)
        {
            if (SessionStarted)
                m_CurrentConnection?.outgoingEventsQueue.Enqueue
                (
                    new GrpcPacket
                    {
                        Timestamp = Now,
                        Routing = routing.ToGrpc(),
                        Control = new ControlEvent
                        {
                            Action = ControlEvent.Types.Action.AudioSessionEnd
                        }
                    }
                );
        }

        // Sends audio chunk to server.
        internal void SendAudio(AudioChunk audioEvent)
        {
            if (SessionStarted)
            {
                GrpcPacket grpcPacket = audioEvent.ToGrpc();
                //InworldNDKWrapper.ClientWrapper_SendSoundMessage(NDKWrapper, agentId, audioEvent.);
                m_CurrentConnection?.outgoingEventsQueue.Enqueue(grpcPacket);
            }
        }
        
        internal bool GetAudioChunk(out AudioChunk chunk)
        {
            if (m_CurrentConnection != null)
            {
                return m_CurrentConnection.incomingAudioQueue.TryDequeue(out chunk);
            }
            chunk = null;
            return false;
        }
        internal bool GetAnimationChunk(out AnimationChunk chunk)
        {
            if (m_CurrentConnection != null)
            {
                return m_CurrentConnection.incomingAnimationQueue.TryDequeue(out chunk);
            }
            chunk = null;
            return false;
        }
        internal void SendEvent(InworldPacket e)
        {
            if (SessionStarted)
                m_CurrentConnection?.outgoingEventsQueue.Enqueue(e.ToGrpc());
        }
        internal bool GetIncomingEvent(out InworldPacket incomingEvent)
        {
            if (m_CurrentConnection != null)
            {
                return m_CurrentConnection.incomingInteractionsQueue.TryDequeue(out incomingEvent);
            }
            incomingEvent = null;
            return false;
        }
        internal async Task StartSession()
        {
            if (!IsSessionInitialized)
            {
                throw new ArgumentException("No sessionKey to start Inworld session, use CreateWorld first.");
            }
// New queue for new session.
            Connection connection = new Connection();
            m_CurrentConnection = connection;
            SessionStarted = true;
            try
            {
                using (m_StreamingCall = m_WorldEngineClient.Session(m_Header))
                {
                    // https://grpc.github.io/grpc/csharp/api/Grpc.Core.IAsyncStreamReader-1.html
                    Task inputTask = Task.Run
                    (
                        async () =>
                        {
                            while (SessionStarted)
                            {
                                bool next;
                                try
                                {
                                    // Waiting response for some time before checking if done.
                                    next = await m_StreamingCall.ResponseStream.MoveNext();
                                }
                                catch (RpcException rpcException)
                                {
                                    if (rpcException.StatusCode == StatusCode.Cancelled)
                                    {
                                        next = false;
                                    }
                                    else
                                    {
                                        // rethrowing other errors.
                                        throw;
                                    }
                                }
                                if (next)
                                {
                                    _ResolveGRPCPackets(m_StreamingCall.ResponseStream.Current);
                                }
                                else
                                {
                                    InworldAI.Log("Session is closed.");
                                    break;
                                }
                            }
                        }
                    );
                    Task outputTask = Task.Run
                    (
                        async () =>
                        {
                            while (SessionStarted)
                            {
                                Task.Delay(100).Wait();
                                // Sending all outgoing events.
                                GrpcPacket e;
                                while (connection.outgoingEventsQueue.TryDequeue(out e))
                                {
                                    if (SessionStarted)
                                    {
                                        await m_StreamingCall.RequestStream.WriteAsync(e);
                                    }
                                }
                            }
                        }
                    );
                    await Task.WhenAll(inputTask, outputTask);
                }
            }
            catch (Exception e)
            {
                SessionStarted = false;
                Errors.Enqueue(e);
            }
            finally
            {
                SessionStarted = false;
            }
        }
        internal TextEvent ResolvePreviousPackets(GrpcPacket response) => response.Text != null ? new TextEvent(response) : null;

        void _ResolveGRPCPackets(GrpcPacket response)
        {
            m_CurrentConnection ??= new Connection();
            if (response.DataChunk != null)
            {
                switch (response.DataChunk.Type)
                {
                    case DataChunk.Types.DataType.Audio:
                        m_CurrentConnection.incomingAudioQueue.Enqueue(new AudioChunk(response));
                        break;
                    case DataChunk.Types.DataType.Animation:
                        m_CurrentConnection.incomingAnimationQueue.Enqueue(new AnimationChunk(response));
                        break;
                    case DataChunk.Types.DataType.State:
                        StateChunk stateChunk = new StateChunk(response);
                        LastState = stateChunk.Chunk.ToBase64();
                        break;
                    default:
                        InworldAI.LogError($"Unsupported incoming event: {response}");
                        break;
                }
            }
            else if (response.Text != null)
            {
                m_CurrentConnection.incomingInteractionsQueue.Enqueue(new TextEvent(response));
            }
            else if (response.Gesture != null)
            {
                m_CurrentConnection.incomingInteractionsQueue.Enqueue(new GestureEvent(response));
            }
            else if (response.Control != null)
            {
                m_CurrentConnection.incomingInteractionsQueue.Enqueue(new Packets.ControlEvent(response));
            }
            else if (response.Emotion != null)
            {

                m_CurrentConnection.incomingInteractionsQueue.Enqueue(new EmotionEvent(response));
            }
            else if (response.Custom != null)
            {
                m_CurrentConnection.incomingInteractionsQueue.Enqueue(new CustomEvent(response));
            }
            else
            {
                InworldAI.LogError($"Unsupported incoming event: {response}");
            }
        }
        internal async Task EndSession()
        {
            if (SessionStarted)
            {
                m_CurrentConnection = null;
                SessionStarted = false;
                await m_StreamingCall.RequestStream.CompleteAsync();
                m_StreamingCall.Dispose();
            }
        }
        internal void Destroy()
        {
#pragma warning disable CS4014
            EndSession();
#pragma warning restore CS4014
            m_Channel.ShutdownAsync();
        }

        public void UpdateWrapper()
        {
            InworldNDKWrapper.ClientWrapper_Update(m_Wrapper.instance);
        }
#endregion
    }
}





// using Google.Protobuf;
// using Google.Protobuf.WellKnownTypes;
// using Grpc.Core;
// using Inworld.Grpc;
// using Inworld.Packets;
// using Inworld.Runtime;
// using Inworld.Util;
// using System;
// using System.Collections.Concurrent;
// using System.Text;
// using System.Threading.Tasks;
// using UnityEditor;
// using UnityEngine;
// using AudioChunk = Inworld.Packets.AudioChunk;
// using ControlEvent = Inworld.Grpc.ControlEvent;
// using CustomEvent = Inworld.Packets.CustomEvent;
// using EmotionEvent = Inworld.Packets.EmotionEvent;
// using GestureEvent = Inworld.Packets.GestureEvent;
// using GrpcPacket = Inworld.Grpc.InworldPacket;
// using InworldPacket = Inworld.Packets.InworldPacket;
// using Routing = Inworld.Packets.Routing;
// using TextEvent = Inworld.Packets.TextEvent;
//
//
// namespace Inworld
// {
//     /// <summary>
//     ///     This class used to save the communication data in runtime.
//     /// </summary>
//     class Connection
//     {
//         // Animation Chunks.
//         internal readonly ConcurrentQueue<AnimationChunk> incomingAnimationQueue = new ConcurrentQueue<AnimationChunk>();
//         // Audio chunks ready to play.
//         internal readonly ConcurrentQueue<AudioChunk> incomingAudioQueue = new ConcurrentQueue<AudioChunk>();
//         // Events that need to be processed by NPC.
//         internal readonly ConcurrentQueue<InworldPacket> incomingInteractionsQueue = new ConcurrentQueue<InworldPacket>();
//         // Events ready to send to server.
//         internal readonly ConcurrentQueue<GrpcPacket> outgoingEventsQueue = new ConcurrentQueue<GrpcPacket>();
//     }
//     /// <summary>
//     ///     This is the logic class for Server communication.
//     /// </summary>
//     class InworldClient
//     {
//         internal IntPtr NDKWrapper;
//         internal InworldClient()
//         {
//             NDKWrapper = InworldNDKWrapper.ClientWrapper_create();
//             Debug.Log("Should have created a wrapper from the dll");
//             m_Channel = new Channel(InworldAI.Game.RuntimeServer, new SslCredentials());
//             m_WorldEngineClient = new WorldEngine.WorldEngineClient(m_Channel);
//         }
//
//         #region Private Variables
//         readonly WorldEngine.WorldEngineClient m_WorldEngineClient;
//         readonly Channel m_Channel;
//         AsyncDuplexStreamingCall<GrpcPacket, GrpcPacket> m_StreamingCall;
//         Connection m_CurrentConnection;
//         InworldAuth m_InworldAuth;
//         string m_SessionKey = "";
//         Metadata m_Header;
//         internal event Action<RuntimeStatus, string> RuntimeEvent;
//         #endregion
//
//         #region Properties
//         internal ConcurrentQueue<Exception> Errors { get; } = new ConcurrentQueue<Exception>();
//         internal bool SessionStarted { get; private set; }
//         internal bool HasInit => !m_InworldAuth.IsExpired;
//         internal string SessionID => m_InworldAuth?.SessionID ?? "";
//         internal string LastState { get; set; }
//         bool IsSessionInitialized => m_SessionKey.Length != 0;
//         Timestamp Now => Timestamp.FromDateTime(DateTime.UtcNow);
//         #endregion
//
//         #region Call backs
//         void OnAuthCompleted()
//         {
//             InworldAI.Log("Init Success!");
//             m_Header = new Metadata
//             {
//                 {"authorization", $"Bearer {m_InworldAuth.Token}"},
//                 {"session-id", m_InworldAuth.SessionID}
//             };
//             RuntimeEvent?.Invoke(RuntimeStatus.InitSuccess, "");
//         }
//         void OnAuthFailed(string msg)
//         {
//             RuntimeEvent?.Invoke(RuntimeStatus.InitFailed, msg);
//         }
//         #endregion
//
//         #region Private Functions
//         internal void GetAppAuth()
//         {
// #if UNITY_EDITOR && VSP
//             if (!string.IsNullOrEmpty(InworldAI.User.Account))
//                 VSAttribution.VSAttribution.SendAttributionEvent("Login Runtime", InworldAI.k_CompanyName, InworldAI.User.Account);
// #endif
//             m_InworldAuth = new InworldAuth(OnAuthCompleted, OnAuthFailed);
//             m_InworldAuth.GenerateAccessToken(InworldAI.Game.StudioServer, InworldAI.Game.APIKey, InworldAI.Game.APISecret);
//         }
//         internal async Task<LoadSceneResponse> LoadScene(string sceneName)
//         {
//
//             LoadSceneRequest lsRequest = new LoadSceneRequest
//             {
//                 Name = sceneName,
//                 Capabilities = InworldAI.Settings.Capabilities,
//                 User = InworldAI.User.Request,
//                 Client = InworldAI.User.Client
//             };
//             if (!string.IsNullOrEmpty(LastState))
//             {
//                 lsRequest.SessionContinuation = new SessionContinuation
//                 {
//                     PreviousState = ByteString.FromBase64(LastState)
//                 };
//             }
//             try
//             {
//                 LoadSceneResponse response = await m_WorldEngineClient.LoadSceneAsync(lsRequest, m_Header);
//                 // Yan: They somehow use {WorkSpace}:{sessionKey} as "sessionKey" now. Need to remove the first part.
//                 m_SessionKey = response.Key.Split(':')[1];
//                 if (response.PreviousState != null)
//                 {
//                     foreach (PreviousState.Types.StateHolder stateHolder in response.PreviousState.StateHolders)
//                     {
//                         InworldAI.Log($"Received Previous Packets: {stateHolder.Packets.Count}");
//                     }
//                 }
//                 m_Header.Add("Authorization", $"Bearer {m_SessionKey}");
//                 RuntimeEvent?.Invoke(RuntimeStatus.LoadSceneComplete, m_SessionKey);
//                 return response;
//             }
//             catch (RpcException e)
//             {
//                 RuntimeEvent?.Invoke(RuntimeStatus.LoadSceneFailed, e.ToString());
//                 return null;
//             }
//         }
//         // Marks audio session start.
//         internal void StartAudio(Routing routing)
//         {
//             InworldAI.Log("Start Audio Event");
//             if (SessionStarted)
//                 m_CurrentConnection?.outgoingEventsQueue.Enqueue
//                 (
//                     new GrpcPacket
//                     {
//                         Timestamp = Now,
//                         Routing = routing.ToGrpc(),
//                         Control = new ControlEvent
//                         {
//                             Action = ControlEvent.Types.Action.AudioSessionStart
//                         }
//                     }
//                 );
//         }
//
//         // Marks session end.
//         internal void EndAudio(Routing routing)
//         {
//             if (SessionStarted)
//                 m_CurrentConnection?.outgoingEventsQueue.Enqueue
//                 (
//                     new GrpcPacket
//                     {
//                         Timestamp = Now,
//                         Routing = routing.ToGrpc(),
//                         Control = new ControlEvent
//                         {
//                             Action = ControlEvent.Types.Action.AudioSessionEnd
//                         }
//                     }
//                 );
//         }
//
//         // Sends audio chunk to server.
//         internal void SendAudio(AudioChunk audioEvent)
//         {
//             if (SessionStarted)
//                 m_CurrentConnection?.outgoingEventsQueue.Enqueue(audioEvent.ToGrpc());
//         }
//         internal bool GetAudioChunk(out AudioChunk chunk)
//         {
//             if (m_CurrentConnection != null)
//             {
//                 return m_CurrentConnection.incomingAudioQueue.TryDequeue(out chunk);
//             }
//             chunk = null;
//             return false;
//         }
//         internal bool GetAnimationChunk(out AnimationChunk chunk)
//         {
//             if (m_CurrentConnection != null)
//             {
//                 return m_CurrentConnection.incomingAnimationQueue.TryDequeue(out chunk);
//             }
//             chunk = null;
//             return false;
//         }
//         internal void SendEvent(InworldPacket e)
//         {
//             if (SessionStarted)
//                 m_CurrentConnection?.outgoingEventsQueue.Enqueue(e.ToGrpc());
//         }
//         internal bool GetIncomingEvent(out InworldPacket incomingEvent)
//         {
//             if (m_CurrentConnection != null)
//             {
//                 return m_CurrentConnection.incomingInteractionsQueue.TryDequeue(out incomingEvent);
//             }
//             incomingEvent = null;
//             return false;
//         }
//         internal async Task StartSession()
//         {
//             if (!IsSessionInitialized)
//             {
//                 throw new ArgumentException("No sessionKey to start Inworld session, use CreateWorld first.");
//             }
//             // New queue for new session.
//             Connection connection = new Connection();
//             m_CurrentConnection = connection;
//
//             SessionStarted = true;
//             try
//             {
//                 using (m_StreamingCall = m_WorldEngineClient.Session(m_Header))
//                 {
//                     // https://grpc.github.io/grpc/csharp/api/Grpc.Core.IAsyncStreamReader-1.html
//                     Task inputTask = Task.Run
//                     (
//                         async () =>
//                         {
//                             while (SessionStarted)
//                             {
//                                 bool next;
//                                 try
//                                 {
//                                     // Waiting response for some time before checking if done.
//                                     next = await m_StreamingCall.ResponseStream.MoveNext();
//                                 }
//                                 catch (RpcException rpcException)
//                                 {
//                                     if (rpcException.StatusCode == StatusCode.Cancelled)
//                                     {
//                                         next = false;
//                                     }
//                                     else
//                                     {
//                                         // rethrowing other errors.
//                                         throw;
//                                     }
//                                 }
//                                 if (next)
//                                 {
//                                     _ResolveGRPCPackets(m_StreamingCall.ResponseStream.Current);
//                                 }
//                                 else
//                                 {
//                                     InworldAI.Log("Session is closed.");
//                                     break;
//                                 }
//                             }
//                         }
//                     );
//                     Task outputTask = Task.Run
//                     (
//                         async () =>
//                         {
//                             while (SessionStarted)
//                             {
//                                 Task.Delay(100).Wait();
//                                 // Sending all outgoing events.
//                                 GrpcPacket e;
//                                 while (connection.outgoingEventsQueue.TryDequeue(out e))
//                                 {
//                                     if (SessionStarted)
//                                     {
//                                         await m_StreamingCall.RequestStream.WriteAsync(e);
//                                     }
//                                 }
//                             }
//                         }
//                     );
//                     await Task.WhenAll(inputTask, outputTask);
//                 }
//             }
//             catch (Exception e)
//             {
//                 SessionStarted = false;
//                 Errors.Enqueue(e);
//             }
//             finally
//             {
//                 SessionStarted = false;
//             }
//         }
//         internal TextEvent ResolvePreviousPackets(GrpcPacket response) => response.Text != null ? new TextEvent(response) : null;
//
//         void _ResolveGRPCPackets(GrpcPacket response)
//         {
//             m_CurrentConnection ??= new Connection();
//             if (response.DataChunk != null)
//             {
//                 switch (response.DataChunk.Type)
//                 {
//                     case DataChunk.Types.DataType.Audio:
//                         m_CurrentConnection.incomingAudioQueue.Enqueue(new AudioChunk(response));
//                         break;
//                     case DataChunk.Types.DataType.Animation:
//                         m_CurrentConnection.incomingAnimationQueue.Enqueue(new AnimationChunk(response));
//                         break;
//                     case DataChunk.Types.DataType.State:
//                         StateChunk stateChunk = new StateChunk(response);
//                         LastState = stateChunk.Chunk.ToBase64();
//                         break;
//                     default:
//                         InworldAI.LogError($"Unsupported incoming event: {response}");
//                         break;
//                 }
//             }
//             else if (response.Text != null)
//             {
//                 m_CurrentConnection.incomingInteractionsQueue.Enqueue(new TextEvent(response));
//             }
//             else if (response.Gesture != null)
//             {
//                 m_CurrentConnection.incomingInteractionsQueue.Enqueue(new GestureEvent(response));
//             }
//             else if (response.Control != null)
//             {
//                 m_CurrentConnection.incomingInteractionsQueue.Enqueue(new Packets.ControlEvent(response));
//             }
//             else if (response.Emotion != null)
//             {
//                 m_CurrentConnection.incomingInteractionsQueue.Enqueue(new EmotionEvent(response));
//             }
//             else if (response.Custom != null)
//             {
//                 m_CurrentConnection.incomingInteractionsQueue.Enqueue(new CustomEvent(response));
//             }
//             else
//             {
//                 InworldAI.LogError($"Unsupported incoming event: {response}");
//             }
//         }
//
//         internal async Task EndSession()
//         {
//             if (SessionStarted)
//             {
//                 m_CurrentConnection = null;
//                 SessionStarted = false;
//                 await m_StreamingCall.RequestStream.CompleteAsync();
//                 m_StreamingCall.Dispose();
//             }
//         }
//         internal void Destroy()
//         {
// #pragma warning disable CS4014
//             EndSession();
// #pragma warning restore CS4014
//             m_Channel.ShutdownAsync();
//         }
//         #endregion
//     }
// }
