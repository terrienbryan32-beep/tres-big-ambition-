using System;
using System.Collections.Generic;

namespace CoopAmbitions.Debug
{
    /// <summary>
    /// Logger du mod : préfixe [Coop.tag] (grep-able dans le Player.log), sortie vers le
    /// logger du ModAPI quand disponible, sinon la console Unity. WarnOnce évite de
    /// spammer le log à chaque frame pour un même problème (pattern Nitrox).
    /// </summary>
    public static class CoopLog
    {
        /// <summary>Branché sur ModContext.Logger.Info au chargement du mod.</summary>
        public static Action<string> Sink;

        /// <summary>Logs verbeux (réseau frame par frame) — activable via les options.</summary>
        public static bool Verbose;

        private static readonly HashSet<string> WarnedKeys = new();

        public static void Info(string message) => Write("Coop", message);

        public static void Info(string tag, string message) => Write($"Coop.{tag}", message);

        public static void Debug(string tag, string message)
        {
            if (Verbose)
                Write($"Coop.{tag}", message);
        }

        public static void Warn(string tag, string message) => Write($"Coop.{tag}", $"AVERTISSEMENT : {message}");

        /// <summary>N'émet l'avertissement qu'une fois par clé (et par session de jeu).</summary>
        public static void WarnOnce(string key, string message)
        {
            if (WarnedKeys.Add(key))
                Warn("once", message);
        }

        public static void Error(string tag, string message) => Write($"Coop.{tag}", $"ERREUR : {message}");

        private static void Write(string tag, string message)
        {
            var line = $"[{tag}] {message}";
            if (Sink != null)
                Sink(line);
            else
                UnityEngine.Debug.Log(line);
        }
    }
}
