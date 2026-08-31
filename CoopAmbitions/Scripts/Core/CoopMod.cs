using System;
using System.Threading.Tasks;
using BAModAPI;
using CoopAmbitions.Core;
using CoopAmbitions.Debug;
using UnityEngine;

[assembly: RegisterModClass(typeof(CoopMod))]

namespace CoopAmbitions.Core
{
    /// <summary>
    /// Point d'entrée du mod, chargé par le jeu via le ModAPI officiel.
    /// Crée le GameObject pilote (CoopRunner) qui héberge la session réseau.
    /// </summary>
    [ModEntryOnInitializationLoad]
    public class CoopMod : IModBigAmbitions
    {
        public static ModContext Context { get; private set; }

        private GameObject _runnerObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            Context = context;
            CoopLog.Sink = context.Logger.Info;

            _runnerObject = new GameObject("CoopAmbitions");
            UnityEngine.Object.DontDestroyOnLoad(_runnerObject);
            _runnerObject.AddComponent<CoopRunner>();

            context.Logger.Info("CoopAmbitions chargé. F9 pour héberger une session.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_runnerObject != null)
            {
                UnityEngine.Object.Destroy(_runnerObject);
                _runnerObject = null;
            }

            Context = null;
            CoopLog.Sink = null;
            return Task.CompletedTask;
        }

        public static void Log(string message) => CoopLog.Info(message);
    }
}
