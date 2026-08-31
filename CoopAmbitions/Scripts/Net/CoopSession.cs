using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopAmbitions.Sync;
using UnityEngine;

namespace CoopAmbitions.Net
{
    /// <summary>
    /// Orchestration de la session coop : handshake Hello/Welcome, registre des joueurs,
    /// envoi de l'état local à 10 Hz, relais hôte->clients, avatars distants.
    /// </summary>
    public sealed class CoopSession : IDisposable
    {
        private const float SendInterval = 0.1f; // 10 Hz
        private const float MinMoveSqr = 0.0001f; // ~1 cm : en dessous, on n'envoie pas

        private readonly ICoopTransport _transport;
        private readonly Action<string> _log;
        private readonly Dictionary<ulong, RemotePlayerView> _remotePlayers = new();
        private readonly Dictionary<ulong, string> _playerNames = new();
        private float _nextSendTime;
        private Vector3 _lastSentPosition = new(float.MaxValue, 0f, 0f);
        private float _lastSentYaw;

        public bool IsRunning => _transport.IsRunning;
        public bool IsHost => _transport.IsHost;
        public int RemotePlayerCount => _remotePlayers.Count;

        public CoopSession(ICoopTransport transport, Action<string> log)
        {
            _log = log ?? (_ => { });
            _transport = transport;
            _transport.StatusChanged += _log;
            _transport.MessageReceived += OnMessage;
            _transport.PeerConnected += OnPeerConnected;
            _transport.PeerDisconnected += OnPeerDisconnected;
        }

        public void StartHost() => _transport.StartHost();

        public void Stop()
        {
            _transport.Stop();
            ClearRemotePlayers();
        }

        /// <summary>À appeler chaque frame (depuis le CoopRunner).</summary>
        public void Tick()
        {
            _transport.Pump();
            if (!_transport.IsRunning) return;

            foreach (var view in _remotePlayers.Values)
                view.Tick();

            if (Time.unscaledTime >= _nextSendTime)
            {
                _nextSendTime = Time.unscaledTime + SendInterval;
                SendLocalState();
            }
        }

        // ------------------------------------------------------------- émission

        private void SendLocalState()
        {
            var player = LocalPlayerLocator.Find();
            if (player == null) return;

            var pos = player.position;
            var yaw = player.eulerAngles.y;
            var moved = (pos - _lastSentPosition).sqrMagnitude > MinMoveSqr
                        || Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentYaw)) > 0.5f;
            if (!moved) return;

            var speed = (pos - _lastSentPosition).magnitude / SendInterval;
            _lastSentPosition = pos;
            _lastSentYaw = yaw;

            var state = new PlayerStateData
            {
                SteamId = _transport.LocalId,
                Position = pos,
                Yaw = yaw,
                Speed = float.IsInfinity(speed) ? 0f : speed,
            };
            _transport.SendToAll(NetMessage.BuildPlayerState(state), reliable: false);
        }

        // ------------------------------------------------------------ réception

        private void OnPeerConnected(ulong steamId)
        {
            if (_transport.IsHost)
            {
                // Le client va envoyer Hello; on attend ça pour l'enregistrer.
                _log($"Connexion entrante ({steamId}).");
            }
            else
            {
                // Client connecté à l'hôte : se présenter.
                _transport.SendToAll(
                    NetMessage.BuildHello(_transport.LocalId, _transport.LocalName),
                    reliable: true);
            }
        }

        private void OnPeerDisconnected(ulong steamId)
        {
            if (steamId == 0 && !_transport.IsHost)
            {
                // Client : l'hôte est parti, fin de session.
                Stop();
                return;
            }

            RemoveRemotePlayer(steamId);
            if (_transport.IsHost)
                _transport.SendToAll(NetMessage.BuildPlayerLeft(steamId), reliable: true);
        }

        private void OnMessage(ulong fromSteamId, byte[] data)
        {
            try
            {
                using var r = new BinaryReader(new MemoryStream(data));
                var type = (MessageType)r.ReadByte();
                switch (type)
                {
                    case MessageType.Hello:
                        HandleHello(r);
                        break;
                    case MessageType.Welcome:
                        HandleWelcome(r);
                        break;
                    case MessageType.PlayerState:
                        HandlePlayerState(r, data);
                        break;
                    case MessageType.PlayerLeft:
                        RemoveRemotePlayer(r.ReadUInt64());
                        break;
                    default:
                        _log($"Message inconnu ({(byte)type}) ignoré.");
                        break;
                }
            }
            catch (Exception e)
            {
                _log($"Message invalide ignoré : {e.Message}");
            }
        }

        private void HandleHello(BinaryReader r)
        {
            if (!_transport.IsHost) return;

            var version = r.ReadUInt16();
            var steamId = r.ReadUInt64();
            var name = r.ReadString();

            if (version != NetProtocol.ProtocolVersion)
            {
                _log($"{name} a une version de protocole incompatible ({version}).");
                return;
            }

            _playerNames[steamId] = name;
            EnsureRemotePlayer(steamId, name);
            _log($"{name} a rejoint la partie.");

            // Etat courant de la session pour le nouveau venu (hôte inclus).
            var known = _remotePlayers.Keys.Where(id => id != steamId)
                .Append(_transport.LocalId)
                .Select(id => new PlayerStateData { SteamId = id })
                .ToArray();
            var names = known.Select(p =>
                p.SteamId == _transport.LocalId
                    ? _transport.LocalName
                    : _playerNames.GetValueOrDefault(p.SteamId, "?")).ToArray();
            _transport.SendToAll(NetMessage.BuildWelcome(known, names), reliable: true);
        }

        private void HandleWelcome(BinaryReader r)
        {
            var version = r.ReadUInt16();
            if (version != NetProtocol.ProtocolVersion)
            {
                _log("Version de protocole incompatible avec l'hôte.");
                Stop();
                return;
            }

            var count = r.ReadByte();
            for (var i = 0; i < count; i++)
            {
                var state = PlayerStateData.Read(r);
                var name = r.ReadString();
                if (state.SteamId == _transport.LocalId) continue;
                _playerNames[state.SteamId] = name;
                EnsureRemotePlayer(state.SteamId, name);
            }

            _log($"Partie rejointe — {count} joueur(s) présent(s).");
        }

        private void HandlePlayerState(BinaryReader r, byte[] raw)
        {
            var state = PlayerStateData.Read(r);
            if (state.SteamId == _transport.LocalId) return;

            var view = EnsureRemotePlayer(state.SteamId,
                _playerNames.GetValueOrDefault(state.SteamId, "?"));
            view.ApplyState(state);

            // Topologie en étoile : l'hôte rediffuse aux autres clients.
            if (_transport.IsHost)
                _transport.SendToAll(raw, reliable: false);
        }

        // -------------------------------------------------------------- avatars

        private RemotePlayerView EnsureRemotePlayer(ulong steamId, string name)
        {
            if (!_remotePlayers.TryGetValue(steamId, out var view))
            {
                view = new RemotePlayerView(steamId, name);
                _remotePlayers[steamId] = view;
            }

            return view;
        }

        private void RemoveRemotePlayer(ulong steamId)
        {
            if (_remotePlayers.TryGetValue(steamId, out var view))
            {
                view.Destroy();
                _remotePlayers.Remove(steamId);
                _log($"{_playerNames.GetValueOrDefault(steamId, "?")} a quitté la partie.");
            }
        }

        private void ClearRemotePlayers()
        {
            foreach (var view in _remotePlayers.Values)
                view.Destroy();
            _remotePlayers.Clear();
        }

        public void Dispose()
        {
            ClearRemotePlayers();
            _transport.Dispose();
        }
    }
}
