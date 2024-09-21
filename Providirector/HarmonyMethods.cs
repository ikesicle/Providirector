using HarmonyLib;
using MonoMod.Cil;
using UnityEngine;
using RoR2;
using Mono.Cecil.Cil;

namespace Providirector;

[HarmonyPatch]
public static class HarmonyMethods
{
    [HarmonyILManipulator, HarmonyPatch(typeof(RunCameraManager), nameof(RunCameraManager.Update))]
    public static void LinkDirectorHUD(ILContext il)
    {
        ILCursor cursor = new ILCursor(il).Goto(0);
        if (cursor.TryGotoNext(
                x => x.MatchLdloc(10),
                x => x.MatchLdloc(8),
                x => x.MatchCallvirt<CameraRigController>("set_viewer")))
        {
            if (cursor.TryGotoNext(
                    /*
                     * ldloc.s      cameraRigController
                       IL_032f: ldloc.s      networkUserBodyObject
                       IL_0331: stfld        class [UnityEngine.CoreModule]UnityEngine.Game
                     */
                    x => x.MatchLdloc(10), 
                    x => x.MatchLdloc(12),
                    x => x.MatchStfld<CameraRigController>("nextTarget")))
            {
                cursor.GotoNext();
                cursor.GotoNext();
                cursor.RemoveRange(4);
                cursor.EmitDelegate(DoDirectorHUD);
                for (int i = 0; i < 3; i++) cursor.Emit(OpCodes.Nop);
            }
        }
    }
        
    private static void DoDirectorHUD(CameraRigController localRig, GameObject origTgt)
    {
        if (!(Providirector.runIsActive && Providirector.directorIsLocal && Providirector.instance.isControllingCharacter))
        {
            localRig.nextTarget = origTgt;
            localRig.cameraMode = RoR2.CameraModes.CameraModePlayerBasic.playerBasic;
            return;
        }
        localRig.nextTarget = Providirector.instance.spectateTarget;
        localRig.cameraMode = CameraModeDirector.director;
    }
}