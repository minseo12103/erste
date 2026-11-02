using System;
using Steamworks;
using UnityEngine;

namespace ShapeCoop
{
    // Initialize as early as Unity permits, before a scene creates any UI or game objects.
    public static class SteamEarlyInit
    {
        public static bool Available { get; private set; }
        public static string Error { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void Initialize()
        {
            try { Available = SteamAPI.Init(); Error = Available ? null : "Steam is not running or initialization failed"; }
            catch (Exception e) { Available = false; Error = e.Message; Debug.LogWarning("Steam initialization failed: " + e.Message); }
        }
        public static void Shutdown() { if (Available) SteamAPI.Shutdown(); Available = false; }
    }
}
