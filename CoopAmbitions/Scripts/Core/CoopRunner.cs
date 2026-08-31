using CoopAmbitions.Net;
using CoopAmbitions.Sync;
using UnityEngine;

namespace CoopAmbitions.Core
{
    /// <summary>
    /// Pilote du mod : vit pour toute la durée du jeu (DontDestroyOnLoad), pompe le
    /// réseau à chaque frame et gère les raccourcis clavier du MVP.
    ///
    /// Raccourcis (MVP — à remplacer plus tard par une vraie UI) :
    ///   F9  : héberger (crée le lobby Steam et ouvre l'overlay d'invitation)
    ///   F10 : arrêter la session en cours
    /// Rejoindre ne demande aucun raccourci : accepter l'invitation Steam suffit.
    /// </summary>
    public sealed class CoopRunner : MonoBehaviour
    {
        private CoopSession _session;

        private void Awake()
        {
            _session = new CoopSession(CoopMod.Log);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9) && !_session.IsRunning)
                _session.StartHost();

            if (Input.GetKeyDown(KeyCode.F10) && _session.IsRunning)
                _session.Stop();

            _session.Tick();
        }

        private void OnDestroy()
        {
            _session?.Dispose();
            _session = null;
            LocalPlayerLocator.InvalidateCache();
        }
    }
}
