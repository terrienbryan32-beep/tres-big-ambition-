using Helpers;
using UnityEngine;

namespace CoopAmbitions.Sync
{
    /// <summary>
    /// Retrouve le Transform du personnage joueur local.
    ///
    /// Accès attestés par les mods d'exemple du SDK officiel (BackAlleyDealerVehicleService)
    /// et les mods communautaires : GameManager.Instance.playerController, avec
    /// PlayerHelper.PlayerController (namespace Helpers) en secours.
    /// </summary>
    public static class LocalPlayerLocator
    {
        private static Transform _cached;

        public static Transform Find()
        {
            if (_cached != null) return _cached;

            var controller = GameManager.Instance?.playerController;
            if (controller != null)
                return _cached = controller.transform;

            var helperController = PlayerHelper.PlayerController;
            if (helperController != null)
                return _cached = helperController.transform;

            return null;
        }

        public static void InvalidateCache() => _cached = null;
    }
}
