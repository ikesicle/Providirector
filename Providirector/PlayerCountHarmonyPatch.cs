using HarmonyLib;
using RoR2;
using UnityEngine.Networking;

namespace DacityP
{
    [HarmonyPatch(MethodType.Getter)]
    public static class HarmonyPatches
	{
        [HarmonyPostfix, HarmonyPatch(typeof(Run), nameof(Run.participatingPlayerCount))]
        public static void ParticipatingPlayerCountOverride(ref int __result)
        {
            if (!Providirector.runIsActive || !NetworkServer.active) return;
            __result--;
            if (__result <= 0) __result = 1;
        }
    }
}

