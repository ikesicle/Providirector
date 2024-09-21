using HarmonyLib;
using UnityEngine;
using RoR2;
using MonoMod.Cil;
using Mono.Cecil.Cil;

namespace Providirector
{
    [HarmonyPatch(MethodType.Getter)]
    public static class HarmonyGetters
    {
        [HarmonyPostfix, HarmonyPatch(typeof(Run), nameof(Run.participatingPlayerCount))]
        public static void ParticipatingPlayerCountOverride(ref int __result)
        {
            if (!Providirector.runIsActive) return;
            __result--;
            if (__result <= 0) __result = 1;
        }
    }
}

