using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

namespace Inworld.NDK
{
    public enum PacketType {
        None = 0, // Useful for indicating that no field is set
        Text = 2,
        Control = 3,
        AudioChunk = 4, // Note: This is deprecated in your proto definition
        Custom = 8,
        CancelResponses = 10, // Note: This is deprecated in your proto definition
        Emotion = 11,
        DataChunk = 12,
        Action = 13,
        Mutation = 15,
        LoadSceneOutput = 16,
        DebugInfo = 18
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct SimpleActor {
        public int type;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SimpleRouting {
        public SimpleActor source;
        public SimpleActor target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SimplePacketId {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string packet_id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string utterance_id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string interaction_id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string correlation_id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SimpleInworldPacket {
        public long timestamp_seconds;
        public int timestamp_nanos;
        // ... Add other members ...

        public PacketUnion packet;
        public int packetType;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PacketUnion {
        [FieldOffset(0)] public SimpleTextEvent textEvent;
        // ... other events will be added here ...
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SimpleTextEvent {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string text;
        public int sourceType;
        public bool isFinal;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct SimpleControlEvent {
        public int action;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string payload;
    }
}