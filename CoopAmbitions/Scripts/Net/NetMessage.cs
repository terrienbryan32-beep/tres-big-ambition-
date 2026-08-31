using System;
using System.IO;
using UnityEngine;

namespace CoopAmbitions.Net
{
    /// <summary>
    /// Protocole binaire du mod. Un octet de type en tête, payload BinaryWriter.
    /// Incrémenter <see cref="ProtocolVersion"/> à CHAQUE changement de format.
    /// </summary>
    public static class NetProtocol
    {
        public const ushort ProtocolVersion = 1;
    }

    public enum MessageType : byte
    {
        Hello = 1, // client -> hôte : version, identité
        Welcome = 2, // hôte -> client : version, joueurs présents
        PlayerState = 3, // les deux sens, ~10 Hz, non-fiable
        PlayerLeft = 4, // hôte -> clients
    }

    public struct PlayerStateData
    {
        public ulong SteamId;
        public Vector3 Position;
        public float Yaw;
        public float Speed;

        public void Write(BinaryWriter w)
        {
            w.Write(SteamId);
            w.Write(Position.x);
            w.Write(Position.y);
            w.Write(Position.z);
            w.Write(Yaw);
            w.Write(Speed);
        }

        public static PlayerStateData Read(BinaryReader r)
        {
            return new PlayerStateData
            {
                SteamId = r.ReadUInt64(),
                Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Yaw = r.ReadSingle(),
                Speed = r.ReadSingle(),
            };
        }
    }

    /// <summary>Helpers d'encodage : un buffer par message, taille modeste.</summary>
    public static class NetMessage
    {
        public static byte[] Build(MessageType type, Action<BinaryWriter> payload = null)
        {
            using var ms = new MemoryStream(64);
            using var w = new BinaryWriter(ms);
            w.Write((byte)type);
            payload?.Invoke(w);
            w.Flush();
            return ms.ToArray();
        }

        public static byte[] BuildHello(ulong steamId, string playerName)
        {
            return Build(MessageType.Hello, w =>
            {
                w.Write(NetProtocol.ProtocolVersion);
                w.Write(steamId);
                w.Write(playerName ?? "?");
            });
        }

        public static byte[] BuildWelcome(PlayerStateData[] currentPlayers, string[] names)
        {
            return Build(MessageType.Welcome, w =>
            {
                w.Write(NetProtocol.ProtocolVersion);
                w.Write((byte)currentPlayers.Length);
                for (var i = 0; i < currentPlayers.Length; i++)
                {
                    currentPlayers[i].Write(w);
                    w.Write(names[i] ?? "?");
                }
            });
        }

        public static byte[] BuildPlayerState(in PlayerStateData state)
        {
            var s = state;
            return Build(MessageType.PlayerState, w => s.Write(w));
        }

        public static byte[] BuildPlayerLeft(ulong steamId)
        {
            return Build(MessageType.PlayerLeft, w => w.Write(steamId));
        }
    }
}
