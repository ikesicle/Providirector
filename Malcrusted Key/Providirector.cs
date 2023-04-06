using BepInEx;
using RoR2;
using RoR2.CameraModes;
using R2API.Utils;
using UnityEngine;
using System;
using System.IO;
using System.Collections.ObjectModel;
using KinematicCharacterController;
using UnityEngine.Networking;
using ProvidirectorGame;
using JetBrains.Annotations;

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
        private static bool runIsActive = false;
        private NetworkUser dirpnuser = null;
        private GameObject spectatetarget = null;
        private CameraRigController maincam = null;
        private Vector3 viewingOverride = Vector3.zero;
        private AssetBundle assets = null;
        private GameObject activehud;
        private static GameObject hud = null;
        private static Vector3 moon2FightActivate = new Vector3(-57, 550, 38);
        private static Vector3 moonFightActivate = new Vector3(91, 554, 83);
        // Because Rewired is dumb and requires you to edit something else in the game's data to



        public void Awake()
        {
            RoR2.Run.onRunStartGlobal += Run_onRunStartGlobal;
            RoR2.Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
            RoR2Application.onUpdate += RoR2Application_onUpdate;
            On.RoR2.Run.OnServerSceneChanged += Run_OnServerSceneChanged;
            On.RoR2.RunCameraManager.Update += RunCameraManager_Update;
            On.RoR2.Run.OnUserAdded += Run_OnUserAdded;
            R2API.Utils.CommandHelper.AddToConsoleWhenReady();

            var path = System.IO.Path.GetDirectoryName(Info.Location);
            assets = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "providirectorui"));
            hud = assets.LoadAsset<GameObject>("ProvidirectorUIRoot");
            
        }

        private void Run_OnUserAdded(On.RoR2.Run.orig_OnUserAdded orig, Run self, NetworkUser user)
        {
            if (LocalUserManager.readOnlyLocalUsersList == null) { Debug.Log("No local users! Something is terribly wrong."); return; }
            NetworkUser localuser = dirpnuser ? dirpnuser : LocalUserManager.readOnlyLocalUsersList[0].currentNetworkUser;
            if (localuser == null) { Debug.Log("Local user does not have an associated network user!"); return; }
            if (user != localuser) orig(self, user);
            else Debug.Log("Player creation was stopped by Providirector.");
        }

        private void RoR2Application_onUpdate()
        {
            if (!runIsActive) return;
            if (dirpnuser == null) return;

            InputManager.SwapPage.PushState(Input.GetKey(KeyCode.Space));
            InputManager.Slot1.PushState(Input.GetKey(KeyCode.Q));
            InputManager.Slot2.PushState(Input.GetKey(KeyCode.Alpha2));
            InputManager.Slot3.PushState(Input.GetKey(KeyCode.Alpha3));
            InputManager.Slot4.PushState(Input.GetKey(KeyCode.Alpha4));
            InputManager.Slot5.PushState(Input.GetKey(KeyCode.Alpha5));
            InputManager.Slot6.PushState(Input.GetKey(KeyCode.Alpha6));
            InputManager.Slot7.PushState(Input.GetKey(KeyCode.Alpha7));
            InputManager.ToggleAffixCommon.PushState(Input.GetKey(KeyCode.C));
            InputManager.ToggleAffixRare.PushState(Input.GetKey(KeyCode.V));
            InputManager.NextTarget.PushState(Input.GetKey(KeyCode.Mouse0));
            InputManager.PrevTarget.PushState(Input.GetKey(KeyCode.Mouse1));
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            if (dirpnuser && dirpnuser.cameraRigController)
            {
                pos = dirpnuser.cameraRigController.sceneCam.transform.position;
                rot = dirpnuser.cameraRigController.sceneCam.transform.rotation;
                pos = pos + rot * new Vector3(0, 0, 5);
            }
            if (DirectorState.instance == null)
            {
                Debug.Log("No directorstate!");
                return;
            }
            if (InputManager.NextTarget.justPressed) ChangeNextTarget();
            if (InputManager.PrevTarget.justPressed) ChangePreviousTarget();
            if (InputManager.Slot1.justPressed) DirectorState.instance.TrySpawn(0, pos, rot);
            if (InputManager.Slot2.justPressed) DirectorState.instance.TrySpawn(1, pos, rot);
            if (InputManager.Slot3.justPressed) DirectorState.instance.TrySpawn(2, pos, rot);
            if (InputManager.Slot4.justPressed) DirectorState.instance.TrySpawn(3, pos, rot);
            if (InputManager.Slot5.justPressed) DirectorState.instance.TrySpawn(4, pos, rot);
            if (InputManager.Slot6.justPressed) DirectorState.instance.TrySpawn(5, pos, rot);
            if (InputManager.ToggleAffixCommon.justPressed) DirectorState.instance.Tier1Active = true;
            if (InputManager.ToggleAffixCommon.justReleased) DirectorState.instance.Tier1Active = false;
            if (InputManager.ToggleAffixRare.justPressed) DirectorState.instance.Tier2Active = true;
            if (InputManager.ToggleAffixRare.justReleased) DirectorState.instance.Tier2Active = false;
            if (InputManager.SwapPage.justPressed) DirectorState.instance.secondPage = !DirectorState.instance.secondPage;

        }

        private void Run_onRunStartGlobal(Run instance)
        {
            if (modenabled)
            {
                runIsActive = true;
                Run.ambientLevelCap = 1500;
                Debug.Log("Providirector has been set up for this run!");
                if (LocalUserManager.readOnlyLocalUsersList == null) { Debug.Log("No local users! Something is terribly wrong."); return; }
                dirpnuser = LocalUserManager.readOnlyLocalUsersList[0].currentNetworkUser;
            }
        }

        private void Run_onRunDestroyGlobal(Run obj)
        {
            dirpnuser = null;
            Run.ambientLevelCap = 99;
            if (activehud) Destroy(activehud);
            activehud = null;
            runIsActive = false;
        }

        private void Run_OnServerSceneChanged(On.RoR2.Run.orig_OnServerSceneChanged orig, Run self, string sceneName)
        {
            orig(self, sceneName);
            //if (sceneName == "moon2") viewingOverride = moon2FightActivate;
            //else if (sceneName == "moon") viewingOverride = moonFightActivate;
            //else viewingOverride = Vector3.zero;
            Invoke("SetupSceneChange", 0.1f);
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
                        cameraRigController.nextTarget = networkUserBodyObject;
                        cameraRigController.cameraMode = CameraModePlayerBasic.playerBasic;
                    } else if (runIsActive) {
                        cameraRigController.nextTarget = spectatetarget;
                        cameraRigController.cameraMode = CameraModeDirector.director;
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

        private void SetupSceneChange()
        {
            Debug.Log("Setting Up Spawncards...");
            DirectorState.UpdateMonsterSelection();
            if (activehud == null)
            {
                Debug.Log("Attempting to instantiate UI...");
                activehud = Instantiate(hud);
                if (dirpnuser) activehud.GetComponent<Canvas>().worldCamera = dirpnuser.cameraRigController.uiCam;
                else
                {
                    Debug.Log("Local network user doesn't exist! Attempting to regenerate...");

                }
                Debug.Log("UI Instantiated.");
            }
        }

        private void ChangeNextTarget()
        {
            ReadOnlyCollection<CharacterBody> readOnlyInstancesList = CharacterBody.readOnlyInstancesList;
            if (readOnlyInstancesList.Count == 0)
            {
                spectatetarget = null;
                return;
            }
            CharacterBody characterBody = spectatetarget ? spectatetarget.GetComponent<CharacterBody>() : null;
            int num = (characterBody ? readOnlyInstancesList.IndexOf(characterBody) : 0);
            for (int i = num + 1; i < readOnlyInstancesList.Count; i++)
            {
                if (Util.LookUpBodyNetworkUser(readOnlyInstancesList[i]) || true)
                {
                    spectatetarget = readOnlyInstancesList[i].gameObject;
                    return;
                }
            }
            for (int j = 0; j <= num; j++)
            {
                if ((Util.LookUpBodyNetworkUser(readOnlyInstancesList[j])) || true)
                {
                    spectatetarget = readOnlyInstancesList[j].gameObject;
                    return;
                }
            }
        }

        private void ChangePreviousTarget()
        {
            ReadOnlyCollection<CharacterBody> readOnlyInstancesList = CharacterBody.readOnlyInstancesList;
            if (readOnlyInstancesList.Count == 0)
            {
                spectatetarget = null;
                return;
            }
            CharacterBody characterBody = spectatetarget ? spectatetarget.GetComponent<CharacterBody>() : null;
            int num = (characterBody ? readOnlyInstancesList.IndexOf(characterBody) : 0);
            for (int i = num - 1; i >= 0; i--)
            {
                if (Util.LookUpBodyNetworkUser(readOnlyInstancesList[i]) || true)
                {
                    spectatetarget = readOnlyInstancesList[i].gameObject;
                    return;
                }
            }
            for (int j = readOnlyInstancesList.Count - 1; j >= num; j--)
            {
                if ((Util.LookUpBodyNetworkUser(readOnlyInstancesList[j])) || true)
                {
                    spectatetarget = readOnlyInstancesList[j].gameObject;
                    return;
                }
            }
        }
        
        public void Spawn(string mastername, string bodyname, Vector3 position, Quaternion rotation, EliteDef eliteDef, int levelbonus = 0)
        {
            // Modified code taken from DebugToolkit
            GameObject preinst = MasterCatalog.FindMasterPrefab(mastername);
            GameObject preinstbody = BodyCatalog.FindBodyPrefab(bodyname);
            if (!preinst || !preinstbody) return;
            GameObject bodyGameObject = UnityEngine.Object.Instantiate<GameObject>(preinst, position, rotation);
            CharacterMaster master = bodyGameObject.GetComponent<CharacterMaster>();
            NetworkServer.Spawn(bodyGameObject);
            master.bodyPrefab = preinstbody;
            master.SpawnBody(position, Quaternion.identity);
            master.inventory.GiveItem(RoR2Content.Items.UseAmbientLevel);
            if (eliteDef)
            {
                master.inventory.SetEquipmentIndex(eliteDef.eliteEquipmentDef.equipmentIndex);
                master.inventory.GiveItem(RoR2Content.Items.BoostHp, Mathf.RoundToInt((eliteDef.healthBoostCoefficient - 1) * 10));
                master.inventory.GiveItem(RoR2Content.Items.BoostDamage, Mathf.RoundToInt(eliteDef.damageBoostCoefficient - 1) * 10);
            }
            if (levelbonus > 0) master.inventory.GiveItem(RoR2Content.Items.LevelBonus, levelbonus);
            master.teamIndex = TeamIndex.Monster;
            master.GetBody().teamComponent.teamIndex = TeamIndex.Monster;


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

        [ConCommand(commandName = "list_masters", flags = ConVarFlags.None, helpText = "Lists all available masters.")]
        private static void CCMasterList(ConCommandArgs args)
        {
            string output = "";
            foreach (string master in MasterCatalog.nameToIndexMap.Keys)
            {
                output += master;
                output += "\n";
            }
            Debug.Log(output);
        }

    }
}

