using CoopAmbitions.Net;
using UnityEngine;

namespace CoopAmbitions.Sync
{
    /// <summary>
    /// Avatar d'un joueur distant : pour le MVP, une capsule + pseudo au-dessus de la
    /// tête, avec interpolation entre les deux derniers états reçus (~150 ms de retard
    /// volontaire pour lisser les 10 Hz du réseau).
    ///
    /// Phase 1+ : remplacer la capsule par un vrai modèle (asset bundle du mod, ou
    /// clone du prefab de personnage du jeu) et piloter son Animator avec Speed.
    /// </summary>
    public sealed class RemotePlayerView
    {
        private const float InterpolationDelay = 0.15f;

        public ulong SteamId { get; }

        private readonly GameObject _root;
        private PlayerStateData _previous;
        private PlayerStateData _latest;
        private float _previousTime;
        private float _latestTime;

        public RemotePlayerView(ulong steamId, string playerName)
        {
            SteamId = steamId;

            _root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _root.name = $"CoopAmbitions_Remote_{steamId}";
            Object.Destroy(_root.GetComponent<Collider>()); // fantôme : aucune collision
            Object.DontDestroyOnLoad(_root);

            var label = new GameObject("Nameplate");
            label.transform.SetParent(_root.transform, false);
            label.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var text = label.AddComponent<TextMesh>();
            text.text = playerName;
            text.fontSize = 48;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.LowerCenter;
            text.color = Color.white;
            label.AddComponent<FaceCamera>();
        }

        public void ApplyState(in PlayerStateData state)
        {
            _previous = _latest;
            _previousTime = _latestTime;
            _latest = state;
            _latestTime = Time.unscaledTime;

            // Premier état reçu : téléportation directe.
            if (_previousTime <= 0f)
            {
                _previous = state;
                _previousTime = _latestTime;
                _root.transform.SetPositionAndRotation(state.Position,
                    Quaternion.Euler(0f, state.Yaw, 0f));
            }
        }

        /// <summary>À appeler chaque frame.</summary>
        public void Tick()
        {
            if (_latestTime <= 0f) return;

            var renderTime = Time.unscaledTime - InterpolationDelay;
            var span = _latestTime - _previousTime;
            var t = span > 0.0001f ? Mathf.Clamp01((renderTime - _previousTime) / span) : 1f;

            var pos = Vector3.LerpUnclamped(_previous.Position, _latest.Position, Mathf.Min(t, 1.5f));
            var yaw = Mathf.LerpAngle(_previous.Yaw, _latest.Yaw, t);
            _root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }

        public void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root);
        }

        /// <summary>Oriente le pseudo vers la caméra.</summary>
        private sealed class FaceCamera : MonoBehaviour
        {
            private void LateUpdate()
            {
                var cam = Camera.main;
                if (cam == null) return;
                transform.rotation =
                    Quaternion.LookRotation(transform.position - cam.transform.position);
            }
        }
    }
}
