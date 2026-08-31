using System;

namespace CoopAmbitions.Net
{
    /// <summary>
    /// Abstraction du transport réseau. Deux implémentations :
    /// - SteamTransport : lobby + relay Steam (le vrai jeu) ;
    /// - LoopbackTransport : paire en mémoire pour développer et tester le protocole
    ///   sans deuxième compte Steam ni même lancer le jeu.
    /// Convention d'expéditeur : côté hôte, l'id du pair émetteur ; côté invité,
    /// 0 (tout vient de l'hôte, l'expéditeur réel est dans le payload).
    /// </summary>
    public interface ICoopTransport : IDisposable
    {
        bool IsHost { get; }
        bool IsRunning { get; }
        ulong LocalId { get; }
        string LocalName { get; }

        event Action<ulong, byte[]> MessageReceived;
        event Action<ulong> PeerConnected;
        event Action<ulong> PeerDisconnected;
        event Action<string> StatusChanged;

        void StartHost();

        /// <summary>À appeler chaque frame : pompe les messages entrants.</summary>
        void Pump();

        /// <summary>Envoie à tous les pairs (hôte : broadcast; invité : vers l'hôte).</summary>
        void SendToAll(byte[] data, bool reliable);

        void Stop();
    }
}
