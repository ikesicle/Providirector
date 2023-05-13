using System;
using HarmonyLib;
using MonoMod.Cil;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.TextCore;

namespace DacityP
{
    [HarmonyPatch(MethodType.Getter)]
    public static class HarmonyPatches
	{
        [HarmonyPostfix, HarmonyPatch(typeof(Run), nameof(Run.participatingPlayerCount))]
        public static void ParticipatingPlayerCountOverride(ref int __result)
        {
            if (Providirector.runIsActive && !RoR2Application.isInSinglePlayer) __result = Math.Max(0, __result-1);
        }
    }
}

