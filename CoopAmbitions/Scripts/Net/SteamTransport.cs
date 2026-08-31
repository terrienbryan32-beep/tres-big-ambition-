using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;

namespace CoopAmbitions.Net
{
    /// <summary>
    /// Transport réseau via Steam : lobby (invitations amis) + SteamNetworkingSockets
    /// en mode relay (pas de ports à ouvrir, NAT traversal géré par Valve).
    /// Le jeu initialise déjà SteamClient — on ne fait que s'en servir.
    /// </summary>
    public sealed class SteamTransport : IDisposable
    {
        public const int VirtualPort = 0;
        public const int MaxPlayers = 4;
        private const string LobbyKey = "coopambitions";

        public bool IsHost { get; private set; }
        public bool IsRunning { get; private set; }
        public ulong LocalSteamId => SteamClient.IsValid ? SteamClient.SteamId.Value : 0;
        public string LocalName => SteamClient.IsValid ? SteamClient.Name : "local";

        /// <summary>(steamIdExpéditeur — 0 si inconnu/hôte, données) reçu du réseau.</summary>
        public event Action<ulong, byte[]> MessageReceived;
        public event Action<ulong> PeerConnected;
        public event Action<ulong> PeerDisconnected;
        public event Action<string> StatusChanged;

        private Lobby? _lobby;
        private HostSocket _hostSocket;
        private ClientConnection _clientConnection;
        private readonly Dictionary<uint, ulong> _connIdToSteamId = new();

        public SteamTransport()
        {
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
            SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberDisconnected += OnLobbyMemberLeft;
            SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeft;
        }

        // ---------------------------------------------------------------- hôte

        public async void StartHost()
        {
            if (IsRunning) return;
            if (!SteamClient.IsValid)
            {
                Status("Steam indisponible — impossible d'héberger.");
                return;
            }

            SteamNetworkingUtils.InitRelayNetworkAccess();
            _hostSocket = SteamNetworkingSockets.CreateRelaySocket<HostSocket>(VirtualPort);
            _hostSocket.Transport = this;
            IsHost = true;
            IsRunning = true;

            var lobby = await SteamMatchmaking.CreateLobbyAsync(MaxPlayers);
            if (!lobby.HasValue)
            {
                Status("Échec de création du lobby Steam.");
                StopInternal();
                return;
            }

            _lobby = lobby.Value;
            _lobby.Value.SetFriendsOnly();
            _lobby.Value.SetJoinable(true);
            _lobby.Value.SetData(LobbyKey, NetProtocol.ProtocolVersion.ToString());

            Status("Lobby créé — invite tes amis (overlay Steam).");
            SteamFriends.OpenGameInviteOverlay(_lobby.Value.Id);
        }

        // -------------------------------------------------------------- client

        private async void OnGameLobbyJoinRequested(Lobby lobby, SteamId friendId)
        {
            if (IsRunning) return;
            Status("Invitation acceptée, connexion au lobby…");
            await lobby.Join();
        }

        private void OnLobbyEntered(Lobby lobby)
        {
            // L'hôte reçoit aussi cet événement pour son propre lobby : ignorer.
            if (IsHost) return;
            if (lobby.GetData(LobbyKey) != NetProtocol.ProtocolVersion.ToString())
            {
                Status("Version du mod différente de celle de l'hôte.");
                lobby.Leave();
                return;
            }

            _lobby = lobby;
            SteamNetworkingUtils.InitRelayNetworkAccess();
            _clientConnection =
                SteamNetworkingSockets.ConnectRelay<ClientConnection>(lobby.Owner.Id, VirtualPort);
            _clientConnection.Transport = this;
            IsHost = false;
            IsRunning = true;
            Status($"Connexion à {lobby.Owner.Name}…");
        }

        private void OnLobbyMemberLeft(Lobby lobby, Friend friend)
        {
            if (IsHost)
                PeerDisconnected?.Invoke(friend.Id.Value);
        }

        // -------------------------------------------------------------- commun

        /// <summary>À appeler chaque frame : pompe les messages entrants.</summary>
        public void Pump()
        {
            _hostSocket?.Receive();
            _clientConnection?.Receive();
        }

        /// <summary>Envoie à tous les pairs (hôte : broadcast; client : vers l'hôte).</summary>
        public void SendToAll(byte[] data, bool reliable)
        {
            var sendType = reliable ? SendType.Reliable : SendType.Unreliable;
            if (IsHost && _hostSocket != null)
            {
                foreach (var conn in _hostSocket.Connected)
                    conn.SendMessage(data, sendType);
            }
            else
            {
                _clientConnection?.Connection.SendMessage(data, sendType);
            }
        }

        public void Stop()
        {
            StopInternal();
            Status("Session coop arrêtée.");
        }

        private void StopInternal()
        {
            _lobby?.Leave();
            _lobby = null;
            _clientConnection?.Close();
            _clientConnection = null;
            _hostSocket?.Close();
            _hostSocket = null;
            _connIdToSteamId.Clear();
            IsHost = false;
            IsRunning = false;
        }

        public void Dispose()
        {
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
            SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberDisconnected -= OnLobbyMemberLeft;
            SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeft;
            StopInternal();
        }

        // ------------------------------------------------------------ interne

        internal void MapConnection(uint connId, ulong steamId) => _connIdToSteamId[connId] = steamId;

        internal ulong SteamIdFor(uint connId) =>
            _connIdToSteamId.TryGetValue(connId, out var id) ? id : 0;

        internal void RaiseMessage(ulong fromSteamId, byte[] data) =>
            MessageReceived?.Invoke(fromSteamId, data);

        internal void RaisePeerConnected(ulong steamId) => PeerConnected?.Invoke(steamId);

        internal void RaisePeerDisconnected(ulong steamId) => PeerDisconnected?.Invoke(steamId);

        internal void Status(string message) => StatusChanged?.Invoke(message);

        internal static byte[] CopyPayload(IntPtr data, int size)
        {
            var buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, size);
            return buffer;
        }

        /// <summary>Socket côté hôte : accepte les connexions relay entrantes.</summary>
        private sealed class HostSocket : SocketManager
        {
            public SteamTransport Transport;

            public override void OnConnecting(Connection connection, ConnectionInfo info)
            {
                connection.Accept();
            }

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                var steamId = info.Identity.SteamId.Value;
                Transport?.MapConnection(connection.Id, steamId);
                Transport?.RaisePeerConnected(steamId);
            }

            public override void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                Transport?.RaisePeerDisconnected(Transport.SteamIdFor(connection.Id));
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data,
                int size, long messageNum, long recvTime, int channel)
            {
                if (Transport == null) return;
                Transport.RaiseMessage(identity.SteamId.Value, CopyPayload(data, size));
            }
        }

        /// <summary>Connexion côté client vers l'hôte.</summary>
        private sealed class ClientConnection : ConnectionManager
        {
            public SteamTransport Transport;

            public override void OnConnected(ConnectionInfo info)
            {
                Transport?.Status("Connecté à l'hôte.");
                Transport?.RaisePeerConnected(0);
            }

            public override void OnDisconnected(ConnectionInfo info)
            {
                Transport?.Status("Déconnecté de l'hôte.");
                Transport?.RaisePeerDisconnected(0);
            }

            public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime,
                int channel)
            {
                // Côté client, tout vient de l'hôte : l'expéditeur réel est dans le payload.
                Transport?.RaiseMessage(0, CopyPayload(data, size));
            }
        }
    }
}
