using System;
using System.Collections.Generic;

namespace CoopAmbitions.Net
{
    /// <summary>
    /// Transport en mémoire : un hôte et des invités reliés par des files de messages.
    /// Permet de développer/tester CoopSession et le protocole sans Steam ni le jeu
    /// (lien parfait : fiable, ordonné, sans latence — la latence simulée viendra
    /// plus tard via les paramètres FakeSendPacketLag/Loss de Steam pour le vrai lien).
    /// Les événements sont livrés au Pump() suivant, comme sur un vrai transport.
    /// </summary>
    public sealed class LoopbackTransport : ICoopTransport
    {
        public bool IsHost { get; private set; }
        public bool IsRunning { get; private set; }
        public ulong LocalId { get; }
        public string LocalName { get; }

        public event Action<ulong, byte[]> MessageReceived;
        public event Action<ulong> PeerConnected;
        public event Action<ulong> PeerDisconnected;
        public event Action<string> StatusChanged;

        private readonly Queue<Action> _inbox = new();
        private LoopbackTransport _host;
        private readonly List<LoopbackTransport> _guests = new();

        private LoopbackTransport(ulong id, string name)
        {
            LocalId = id;
            LocalName = name;
        }

        public static LoopbackTransport CreateHost(string name = "hôte")
        {
            return new LoopbackTransport(1, name);
        }

        /// <summary>Crée un invité connecté à cet hôte (appeler après StartHost).</summary>
        public LoopbackTransport CreateGuest(string name = null)
        {
            if (!IsHost) throw new InvalidOperationException("CreateGuest s'appelle sur l'hôte.");

            var guest = new LoopbackTransport((ulong)(_guests.Count + 2), name ?? $"invité{_guests.Count + 1}")
            {
                _host = this,
                IsRunning = true,
            };
            _guests.Add(guest);

            var guestId = guest.LocalId;
            _inbox.Enqueue(() => PeerConnected?.Invoke(guestId));
            guest._inbox.Enqueue(() => guest.PeerConnected?.Invoke(0));
            return guest;
        }

        public void StartHost()
        {
            IsHost = true;
            IsRunning = true;
            StatusChanged?.Invoke("Loopback : hôte démarré.");
        }

        public void Pump()
        {
            while (_inbox.Count > 0)
                _inbox.Dequeue().Invoke();
        }

        public void SendToAll(byte[] data, bool reliable)
        {
            if (!IsRunning) return;
            var copy = (byte[])data.Clone();

            if (IsHost)
            {
                foreach (var guest in _guests)
                {
                    var g = guest;
                    g._inbox.Enqueue(() => g.MessageReceived?.Invoke(0, copy));
                }
            }
            else if (_host != null)
            {
                var host = _host;
                var fromId = LocalId;
                host._inbox.Enqueue(() => host.MessageReceived?.Invoke(fromId, copy));
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            if (IsHost)
            {
                foreach (var guest in _guests)
                {
                    var g = guest;
                    g._inbox.Enqueue(() =>
                    {
                        g.IsRunning = false;
                        g.PeerDisconnected?.Invoke(0);
                    });
                }
                _guests.Clear();
            }
            else if (_host != null)
            {
                var host = _host;
                var id = LocalId;
                host._guests.Remove(this);
                host._inbox.Enqueue(() => host.PeerDisconnected?.Invoke(id));
                _host = null;
            }

            StatusChanged?.Invoke("Loopback : arrêté.");
        }

        public void Dispose() => Stop();
    }
}
