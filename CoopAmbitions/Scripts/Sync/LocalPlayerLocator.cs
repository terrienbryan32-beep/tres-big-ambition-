using UnityEngine;

namespace CoopAmbitions.Sync
{
    /// <summary>
    /// Retrouve le Transform du personnage joueur local.
    ///
    /// A AJUSTER : une fois le projet ouvert dans Unity avec les DLL du jeu importées,
    /// remplacer ces heuristiques par l'accès direct au service du jeu (chercher dans
    /// BigAmbitions.Characters / Services un singleton exposant le personnage local,
    /// p. ex. via l'explorateur d'objets de l'éditeur pendant que le jeu tourne).
    /// Tout le reste du mod ne dépend que de cette classe pour ça.
    /// </summary>
    public static class LocalPlayerLocator
    {
        private static Transform _cached;
        private static float _nextSearchTime;

        public static Transform Find()
        {
            if (_cached != null) return _cached;
            if (Time.unscaledTime < _nextSearchTime) return null;
            _nextSearchTime = Time.unscaledTime + 2f; // ne pas scanner à chaque frame

            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null)
                return _cached = tagged.transform;

            // Repli : contrôleur de personnage le plus proche de la caméra active.
            var cam = Camera.main;
            var controllers = Object.FindObjectsOfType<CharacterController>();
            Transform best = null;
            var bestDist = float.MaxValue;
            foreach (var controller in controllers)
            {
                var d = cam != null
                    ? (controller.transform.position - cam.transform.position).sqrMagnitude
                    : 0f;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = controller.transform;
                }
            }

            return _cached = best;
        }

        public static void InvalidateCache()
        {
            _cached = null;
            _nextSearchTime = 0f;
        }
    }
}
