using BepInEx;
using BepInEx.Logging;
using RoR2;
using RoR2.Skills;
using RoR2.Networking;
using RoR2.CameraModes;
using R2API;
using R2API.Utils;
using UnityEngine;
using HG;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HarmonyLib;
using KinematicCharacterController;
using Rewired;

#pragma warning disable Publicizer001

namespace DacityP
{
    /*
     * Objectives for the Mod:
     * 1. Allow for a player (the host) to join the game while having no hitbox or kinematic body, preferably by setting User Network State.
     *     - We change the user's 
     *    
     * 1a. Alternatively, maybe we program a special survivor which has no body and a versatile camera? That would be really jank, though.
     * 2. Figure out how to have the camera for that "player" lock onto entities in the game world (in the future, this will be players)
     * 3. Allow for the "Player" to spawn in monsters at the specified location
     *     - The actual directors do this by calling GameObject gameObject = DirectorCore.instance.TrySpawnObject(DirectorSpawnRequest);
     *     - However, we can get away with directly copying the Spawn method found in the SpawnCard implementation.
     * 
     * Bookmarked Methods:
     * PregameController - Controls Pregame actions, including char select
     * CharacterSelectController - Controls Character Selection screen
     */
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync)]
    [BepInDependency("com.bepis.r2api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("com.DacityP.Providirector", "Providirector", "0.0.1")]
    public class Providirector : BaseUnityPlugin
    {
        private static bool modenabled = true;
        private PlayerCharacterMasterController dirpcontroller = null;
        private CharacterMaster dirpmaster = null;
        private NetworkUser dirpnuser = null;
        private CharacterBody dirpbody = null;
        private KinematicCharacterMotor dirpkinemotor = null;

        // Because Rewired is dumb and requires you to edit something else in the game's data to



        public void Awake()
        {
            RoR2.Run.onRunStartGlobal += Run_onRunStartGlobal;
            RoR2.Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
            R2API.Utils.CommandHelper.AddToConsoleWhenReady();
            On.RoR2.Run.OnServerCharacterBodySpawned += Run_OnServerCharacterBodySpawned;
            On.RoR2.InteractionDriver.FixedUpdate += InteractionDriver_FixedUpdate;
            On.RoR2.InteractionDriver.FindBestInteractableObject += InteractionDriver_FindBestInteractableObject;
            On.RoR2.CharacterBody.GetVisibilityLevel_TeamIndex += CharacterBody_GetVisibilityLevel_TeamIndex;
            RoR2Application.onUpdate += RoR2Application_onUpdate;
            RoR2.CameraRigController.onCameraTargetChanged += CameraRigController_onCameraTargetChanged;
            On.RoR2.RunCameraManager.Update += RunCameraManager_Update;
        }

        private void RunCameraManager_Update(On.RoR2.RunCameraManager.orig_Update orig, RunCameraManager self)
        {
            // Copied code with a single exception implemented for the new player
            bool flag = Stage.instance;
            CameraRigController[] cameras = self.cameras;
            if (flag)
            {
                int i = 0;
                for (int count = CameraRigController.readOnlyInstancesList.Count; i < count; i++)
                {
                    if (CameraRigController.readOnlyInstancesList[i].suppressPlayerCameras)
                    {
                        flag = false;
                        return;
                    }
                }
            }
            if (flag)
            {
                int num = 0;
                ReadOnlyCollection<NetworkUser> readOnlyLocalPlayersList = NetworkUser.readOnlyLocalPlayersList;
                for (int j = 0; j < readOnlyLocalPlayersList.Count; j++)
                {
                    NetworkUser networkUser = readOnlyLocalPlayersList[j];
                    CameraRigController cameraRigController = cameras[num];
                    if (!cameraRigController)
                    {
                        cameraRigController = UnityEngine.Object.Instantiate(LegacyResourcesAPI.Load<GameObject>("Prefabs/Main Camera")).GetComponent<CameraRigController>();
                        cameras[num] = cameraRigController;
                    }
                    cameraRigController.viewer = networkUser;
                    networkUser.cameraRigController = cameraRigController;
                    GameObject networkUserBodyObject = RunCameraManager.GetNetworkUserBodyObject(networkUser);
                    ForceSpectate forceSpectate = InstanceTracker.FirstOrNull<ForceSpectate>();

                    if ((bool)forceSpectate)
                    {
                        cameraRigController.nextTarget = forceSpectate.target;
                        cameraRigController.cameraMode = CameraModePlayerBasic.spectator;
                    }
                    else if ((bool)networkUserBodyObject)
                    {
                        if (modenabled && dirpmaster && dirpmaster.GetBodyObject() == networkUserBodyObject)
                        {
                            cameraRigController.cameraMode = CameraModeDirector.director;
                        }
                        else
                        {
                            cameraRigController.nextTarget = networkUserBodyObject;
                            cameraRigController.cameraMode = CameraModePlayerBasic.playerBasic;
                        }
                    }
                    else if (!cameraRigController.disableSpectating)
                    {
                        cameraRigController.cameraMode = CameraModePlayerBasic.spectator;
                        if (!cameraRigController.target)
                        {
                            cameraRigController.nextTarget = CameraRigControllerSpectateControls.GetNextSpectateGameObject(networkUser, null);
                        }
                    }
                    else
                    {
                        cameraRigController.cameraMode = CameraModeNone.instance;
                    }
                    num++;
                }
                int num2 = num;
                for (int k = num; k < cameras.Length; k++)
                {
                    ref CameraRigController reference = ref cameras[num];
                    if ((object)reference != null)
                    {
                        if ((bool)reference)
                        {
                            UnityEngine.Object.Destroy(cameras[num].gameObject);
                        }
                        reference = null;
                    }
                }
                Rect[] array = RunCameraManager.screenLayouts[num2];
                for (int l = 0; l < num2; l++)
                {
                    cameras[l].viewport = array[l];
                }
                return;
            }
            for (int m = 0; m < cameras.Length; m++)
            {
                if ((bool)cameras[m])
                {
                    UnityEngine.Object.Destroy(cameras[m].gameObject);
                }
            }
        }

        private void CameraRigController_onCameraTargetChanged(CameraRigController instance, GameObject targetobj)
        {
            if (dirpbody && (dirpbody == instance.targetBody))
            {
                Debug.Log("We're looking at the director!!! Setting the Camera to use the new director configuration...");
                instance.cameraMode = dirpnuser.cameraRigController.cameraMode = CameraModeDirector.director;
                Debug.Log("Success!");
            }
        }

        private void Run_onRunDestroyGlobal(Run obj)
        {
            dirpcontroller = null;
            dirpmaster = null;
            dirpnuser = null;
            dirpbody = null;
            dirpkinemotor = null;


        }

        private void RoR2Application_onUpdate()
        {
            InputManager.SwapPage.PushState(Input.GetKey(KeyCode.Space));
            InputManager.Slot1.PushState(Input.GetKey(KeyCode.Alpha1));
            InputManager.Slot2.PushState(Input.GetKey(KeyCode.Alpha2));
            InputManager.Slot3.PushState(Input.GetKey(KeyCode.Alpha3));
            InputManager.Slot4.PushState(Input.GetKey(KeyCode.Alpha4));
            InputManager.Slot5.PushState(Input.GetKey(KeyCode.Alpha5));
            InputManager.Slot6.PushState(Input.GetKey(KeyCode.Alpha6));
            InputManager.Slot7.PushState(Input.GetKey(KeyCode.Alpha7));
            InputManager.ToggleAffixCommon.PushState(Input.GetKey(KeyCode.C));
            InputManager.ToggleAffixRare.PushState(Input.GetKey(KeyCode.V));
        }



        private VisibilityLevel CharacterBody_GetVisibilityLevel_TeamIndex(On.RoR2.CharacterBody.orig_GetVisibilityLevel_TeamIndex orig, CharacterBody self, TeamIndex observerTeam)
        {
            //if (self == dirpbody) return VisibilityLevel.Invisible;
            return orig(self, observerTeam);
        }

        private GameObject InteractionDriver_FindBestInteractableObject(On.RoR2.InteractionDriver.orig_FindBestInteractableObject orig, InteractionDriver self)
        {
            if (dirpmaster == null) return orig(self);
            if (self.characterBody != dirpmaster.GetBody()) return orig(self);
            return null;
        }

        private void InteractionDriver_FixedUpdate(On.RoR2.InteractionDriver.orig_FixedUpdate orig, InteractionDriver self)
        {
            if (!dirpmaster) orig(self);
            else if (self.characterBody != dirpmaster.GetBody()) orig(self);
        }

        private void Run_onRunStartGlobal(Run instance)
        {
            if (PlayerCharacterMasterController.instances.Count > 0 && modenabled) {
                dirpcontroller = PlayerCharacterMasterController.instances[0];
                dirpmaster = dirpcontroller.master;
                dirpmaster.bodyPrefab = BodyCatalog.FindBodyPrefab("WispBody");
                dirpnuser = dirpcontroller.networkUser;
                Debug.Log("Providirector is enabled for this run!");
                
            }
        }

        private void Run_OnServerCharacterBodySpawned(On.RoR2.Run.orig_OnServerCharacterBodySpawned orig, Run self, CharacterBody characterBody)
        {
            if (dirpmaster == null) return;
            if (dirpmaster.GetBody() != characterBody) return;
            // Since the orig has no content in it, we don't need to call it at all.
            dirpbody = characterBody;
            dirpkinemotor = dirpbody.GetComponentInChildren<KinematicCharacterMotor>();
            Rigidbody r = dirpbody.rigidbody;
            foreach (HurtBox x in dirpbody.hurtBoxGroup.hurtBoxes)
            {
                x.collider.enabled = false;
            }
            dirpbody.mainHurtBox.collider.enabled = false;
            Debug.Log("Successfully disabled hitboxes!");
            if (!dirpmaster.godMode) dirpmaster.ToggleGod();
            Debug.Log("Godmode Enabled for contingency reasons");
            dirpbody.AddBuff(RoR2Content.Buffs.Cloak);
            dirpbody.teamComponent.hideAllyCardDisplay = true;
            Debug.Log("Ally Card Display Hidden");
            dirpmaster.money = 0;
            dirpbody.inventory.GiveItem(RoR2Content.Items.TeleportWhenOob);
            // Special item to stop the server from automatically killing the director
            
            
            SkillLocator s = dirpbody.skillLocator;
            if (s == null)
            {
                Debug.Log("No skill locator detected on this body's GameObject.");
            }
            else
            {
                Debug.Log("We found a skillLocator! Now we set all skills to null.");
                s.primary = null;
                s.secondary = null;
                s.utility = null;
                s.special = null;
            }
        }

        [ConCommand(commandName = "check_cameras", flags = ConVarFlags.None, helpText = "Checks the state of all currently active CRCs.")]
        private static void CCCheckCameras(ConCommandArgs args)
        {
            int i = 0;
            foreach (CameraRigController c in CameraRigController.readOnlyInstancesList)
            {
                Camera scenecam = c.sceneCam;
                Debug.LogFormat("Camera in Slot {0} ===", i);
                Debug.Log("sceneCam Scene: " + scenecam.scene.name);
                Debug.Log("sceneCam Position + Rot: " + scenecam.transform.position + " -- " + scenecam.transform.rotation);
                Debug.Log("Viewer: " + (c.viewer ? c.viewer.userName : "null"));
                Debug.Log("Target: " + (c.target ? c.target.name : "null"));
                Debug.Log("CameraMode: " + c.cameraMode.GetType());
                Debug.Log("Is Being Overridden: " + (c.hasOverride ? "Yes" : "No"));
                Debug.Log("In a Cutscene: " + (c.isCutscene ? "Yes" : "No"));
                i++;
            }
        }

        [ConCommand(commandName = "toggle_prvd", flags = ConVarFlags.None, helpText = "Toggles enabling of PRVD.")]
        private static void CCTogglePrvd(ConCommandArgs args)
        {
            modenabled = !modenabled;
            Debug.LogFormat("Providirector enabled set to {0}", modenabled);
        }

    }
}

