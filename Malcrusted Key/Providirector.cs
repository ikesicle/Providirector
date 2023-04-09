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
using static Rewired.UI.ControlMapper.ControlMapper;
using System.Collections.Generic;
using RoR2.CharacterAI;

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
        private static GameObject hud;
        private NetworkUser dirpnuser;
        private LocalUser localuser;
        private GameObject spectatetarget;
        private CharacterMaster currentmaster;
        private PlayerCharacterMasterController currentcontroller => currentmaster?.playerCharacterMasterController;
        private CharacterBody currentbody => currentmaster.GetBody();
        private CharacterMotor currentmotor => currentbody.characterMotor;
        private CameraRigController maincam => dirpnuser.cameraRigController;
        private BaseAI currentai;
        private Vector3 viewingOverride = Vector3.zero;
        private AssetBundle assets;
        private GameObject activehud;
        private bool addPlayerControlToNextSpawnCardSpawn = false;
        
        private bool isSinglePlayer => RoR2Application.isInSinglePlayer;

        public void Awake()
        {
            RoR2.Run.onRunStartGlobal += Run_onRunStartGlobal;
            RoR2.Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
            RoR2Application.onUpdate += RoR2Application_onUpdate;
            On.RoR2.Run.OnServerSceneChanged += Run_OnServerSceneChanged;
            On.RoR2.RunCameraManager.Update += RunCameraManager_Update;
            On.RoR2.Run.OnUserAdded += Run_OnUserAdded;
            On.RoR2.Run.RecalculateDifficultyCoefficentInternal += Run_RecalculateDifficultyCoefficentInternal;
            On.RoR2.CombatDirector.Awake += CombatDirector_Awake;
            On.RoR2.CharacterSpawnCard.GetPreSpawnSetupCallback += NewPrespawnSetup;
            On.RoR2.BossGroup.DropRewards += BossGroup_DropRewards;
            On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.PreEncounterBegin += BrotherEncounterPhaseBaseState_PreEncounterBegin;
            On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.OnMemberAddedServer += MithrixPlayerControlSetup;
            R2API.Utils.CommandHelper.AddToConsoleWhenReady();


            var path = System.IO.Path.GetDirectoryName(Info.Location);
            assets = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "providirectorui"));
            hud = assets.LoadAsset<GameObject>("ProvidirectorUIRoot");
            
        }

        private void BossGroup_DropRewards(On.RoR2.BossGroup.orig_DropRewards orig, BossGroup self)
        {
            if (!Run.instance)
            {
                Debug.LogError("No valid run instance!");
                return;
            }
            if (self.rng == null)
            {
                Debug.LogError("RNG is null!");
                return;
            }
            int participatingPlayerCount = PlayerCharacterMasterController.instances.Count;
            if (participatingPlayerCount == 0)
            {
                return;
            }
            if ((bool)self.dropPosition)
            {
                PickupIndex none = PickupIndex.none;
                if ((bool)self.dropTable)
                {
                    none = self.dropTable.GenerateDrop(self.rng);
                }
                else
                {
                    List<PickupIndex> list = Run.instance.availableTier2DropList;
                    if (self.forceTier3Reward)
                    {
                        list = Run.instance.availableTier3DropList;
                    }
                    none = self.rng.NextElementUniform(list);
                }
                int num = 1 + self.bonusRewardCount;
                if (self.scaleRewardsByPlayerCount)
                {
                    num *= participatingPlayerCount;
                }
                float angle = 360f / (float)num;
                Vector3 vector = Quaternion.AngleAxis(UnityEngine.Random.Range(0, 360), Vector3.up) * (Vector3.up * 40f + Vector3.forward * 5f);
                Quaternion quaternion = Quaternion.AngleAxis(angle, Vector3.up);
                bool flag = self.bossDrops != null && self.bossDrops.Count > 0;
                bool flag2 = self.bossDropTables != null && self.bossDropTables.Count > 0;
                int num2 = 0;
                while (num2 < num)
                {
                    PickupIndex pickupIndex = none;
                    if (self.bossDrops != null && (flag || flag2) && self.rng.nextNormalizedFloat <= self.bossDropChance)
                    {
                        if (flag2)
                        {
                            PickupDropTable pickupDropTable = self.rng.NextElementUniform(self.bossDropTables);
                            if (pickupDropTable != null)
                            {
                                pickupIndex = pickupDropTable.GenerateDrop(self.rng);
                            }
                        }
                        else
                        {
                            pickupIndex = self.rng.NextElementUniform(self.bossDrops);
                        }
                    }
                    PickupDropletController.CreatePickupDroplet(pickupIndex, self.dropPosition.position, vector);
                    num2++;
                    vector = quaternion * vector;
                }
            }
            else
            {
                Debug.LogWarning("dropPosition not set for BossGroup! No item will be spawned.");
            }
        }

        private Action<CharacterMaster> NewPrespawnSetup(On.RoR2.CharacterSpawnCard.orig_GetPreSpawnSetupCallback orig, CharacterSpawnCard self)
        {
            return (CharacterMaster c) =>
            {
                PlayerCharacterMasterController cmc = c.GetComponent<PlayerCharacterMasterController>();
                if (addPlayerControlToNextSpawnCardSpawn && !cmc)
                {
                    cmc = gameObject.AddComponent<PlayerCharacterMasterController>();
                    cmc.enabled = false;
                    addPlayerControlToNextSpawnCardSpawn = false;
                }
            };
        }

        private void Run_RecalculateDifficultyCoefficentInternal(On.RoR2.Run.orig_RecalculateDifficultyCoefficentInternal orig, Run self)
        {
            if (!runIsActive)
            {
                orig(self);
                return;
            }
            int ppc = PlayerCharacterMasterController.instances.Count;
            float num = self.GetRunStopwatch();
            DifficultyDef difficultyDef = DifficultyCatalog.GetDifficultyDef(self.selectedDifficulty);
            float num2 = Mathf.Floor(num * (1f / 60f));
            float num3 = (float)ppc * 0.3f;
            float num4 = 0.7f + num3;
            float num5 = 0.7f + num3;
            float num6 = Mathf.Pow(ppc, 0.2f);
            float num7 = 0.0506f * difficultyDef.scalingValue * num6;
            float num8 = 0.0506f * difficultyDef.scalingValue * num6;
            float num9 = Mathf.Pow(1.15f, self.stageClearCount);
            self.compensatedDifficultyCoefficient = (num5 + num8 * num2) * num9;
            self.difficultyCoefficient = (num4 + num7 * num2) * num9;
            float num10 = (num4 + num7 * (num * (1f / 60f))) * Mathf.Pow(1.15f, self.stageClearCount);
            self.ambientLevel = Mathf.Min((num10 - num4) / 0.33f + 1f, Run.ambientLevelCap);
            int num11 = self.ambientLevelFloor;
            self.ambientLevelFloor = Mathf.FloorToInt(self.ambientLevel);
            if (num11 != self.ambientLevelFloor && num11 != 0 && self.ambientLevelFloor > num11) self.OnAmbientLevelUp();

        }

        private void MithrixPlayerControlSetup(On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.orig_OnMemberAddedServer orig, EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState self, CharacterMaster master)
        {
            Debug.Log("OnMemAddedServer called...");
            orig(self, master);
            if (self.phaseControllerChildString == "Phase2" || !runIsActive) return;
            AddPlayerControl(master);
        }

        private void BrotherEncounterPhaseBaseState_PreEncounterBegin(On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.orig_PreEncounterBegin orig, EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState self)
        {
            orig(self);
            if (self.phaseControllerChildString == "Phase2" || !runIsActive) return;
            Debug.LogFormat("Pre-encounter called for object on phase {0}", self.phaseControllerChildString);
            addPlayerControlToNextSpawnCardSpawn = true;
        }

        private void CombatDirector_Awake(On.RoR2.CombatDirector.orig_Awake orig, CombatDirector self)
        {
            if (runIsActive)
            {
                self.creditMultiplier *= 0.5f;
                Debug.Log("Combat Director credit count reduced.");
            }
            orig(self);
        }

        private void Run_OnUserAdded(On.RoR2.Run.orig_OnUserAdded orig, Run self, NetworkUser user)
        {
            if (!modenabled)
            {
                orig(self, user);
                return;
            }
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
            InputManager.Slot1.PushState(Input.GetKey(KeyCode.Alpha1));
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
            if (dirpnuser && maincam)
            {
                pos = maincam.sceneCam.transform.position;
                rot = maincam.sceneCam.transform.rotation;
                pos = pos + rot * new Vector3(0, 0, 5);
            }
            if (DirectorState.instance == null) return;
            if ((localuser.eventSystem && localuser.eventSystem.isCursorVisible) || currentmaster) return;
            if (InputManager.Slot7.justPressed) AddPlayerControl(Spawn("LemurianMaster", "LemurianBody", pos, rot));
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
            if (modenabled && NetworkServer.active)
            {
                runIsActive = true;
                Run.ambientLevelCap = 1500;
                Debug.Log("Providirector has been set up for this run!");
                if (LocalUserManager.readOnlyLocalUsersList == null) { Debug.Log("No local users! Something is terribly wrong."); return; }
                localuser = LocalUserManager.readOnlyLocalUsersList[0];
                dirpnuser = LocalUserManager.readOnlyLocalUsersList[0].currentNetworkUser;
            }
        }

        private void Run_onRunDestroyGlobal(Run obj)
        {
            if (!runIsActive) return;
            dirpnuser = null;
            localuser = null;
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
            Invoke("SetupSceneChange", 1f);
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
                if (dirpnuser) activehud.GetComponent<Canvas>().worldCamera = maincam.uiCam;
                else
                {
                    Debug.Log("Local network user doesn't exist!");

                }
                Debug.Log("UI Instantiated.");
            }
            currentmaster = null;
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

        private void AddPlayerControl(CharacterMaster c)
        {
            DisengagePlayerControl();
            Debug.LogFormat("Attempting to take control of CharacterMaster {0}", c.name);
            currentmaster = c;
            currentai = currentmaster.GetComponent<BaseAI>();
            currentmaster.playerCharacterMasterController = currentmaster.GetComponent<PlayerCharacterMasterController>();
            if (!currentcontroller)
            {
                Debug.LogWarningFormat("CharacterMaster {0} does not have a PCMC! Instantiating one now... though this will lead to desyncs between the client and server.", c.name);
                currentmaster.playerCharacterMasterController = c.gameObject.AddComponent<PlayerCharacterMasterController>();
            }
            GameObject oldprefab = c.bodyPrefab;
            currentcontroller.LinkToNetworkUserServer(dirpnuser);
            currentcontroller.master.bodyPrefab = oldprefab; // RESET
            if (activehud) activehud.SetActive(false);
            currentcontroller.enabled = true;
            Run.instance.userMasters[dirpnuser.id] = c;
            currentbody.networkIdentity.AssignClientAuthority(dirpnuser.connectionToClient);
            AIDisable();
            currentai.onBodyDiscovered += AIDisable;
            GlobalEventManager.onCharacterDeathGlobal += DisengagePlayerControl;
        }

        private void DisengagePlayerControl()
        {
            if (currentmaster)
            {
                GlobalEventManager.onCharacterDeathGlobal -= DisengagePlayerControl;
                if (currentcontroller) Destroy(currentcontroller);
                currentai.onBodyDiscovered -= AIDisable;
                AIEnable();
                currentai = null;
                if (currentbody.networkIdentity) currentbody.networkIdentity.RemoveClientAuthority(dirpnuser.connectionToClient);
                currentmaster.playerCharacterMasterController = null;
            }
            currentmaster = null;
            if (activehud) activehud.SetActive(true);
            Debug.LogFormat("Characterbody disengaged! There are now {0} active PCMCs", PlayerCharacterMasterController.instances.Count);
        }

        private void AIDisable()
        {
            if (currentai)
            {
                currentai.OnBodyLost(currentbody);
                currentai.enabled = false;
                Debug.Log("AI Disabled.");
            }
            else
            {
                Debug.Log("It doesn't have an AI!");
            }
        }

        private void AIDisable(CharacterBody _) { AIDisable(); }
        
        private void AIEnable()
        {
            if (currentai)
            {
                currentai.enabled = true;
                currentai.OnBodyStart(currentbody);
            }
        }

        private void DisengagePlayerControl(DamageReport dr)
        {
            if (dr.victimMaster == currentmaster) DisengagePlayerControl();
        }

        public CharacterMaster Spawn(string mastername, string bodyname, Vector3 position, Quaternion rotation, EliteDef eliteDef = null, int levelbonus = 0, bool includePlayerControlInterface = true)
        {
            // Modified code taken from DebugToolkit
            GameObject preinst = MasterCatalog.FindMasterPrefab(mastername);
            GameObject preinstbody = BodyCatalog.FindBodyPrefab(bodyname);
            if (!preinst || !preinstbody) return null;
            GameObject bodyGameObject = Instantiate(preinst, position, rotation);
            CharacterMaster master = bodyGameObject.GetComponent<CharacterMaster>();
            if (includePlayerControlInterface) bodyGameObject.AddComponent<PlayerCharacterMasterController>().enabled = false;
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
            return master;
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

        [ConCommand(commandName = "prvd_item", flags = ConVarFlags.None, helpText = "Gives an item to a user.")]
        private static void CCGiveItem(ConCommandArgs args)
        {
            if (!NetworkServer.active)
            {
                Debug.LogError("prvd_item called on client.");
                return;
            }
            if (args.Count != 3) {
                Debug.Log("Usage: prvd_item [item name] [count] [target name]");
                Debug.LogFormat("3 arguments required, but {1} given.", args.Count);
                return;
            }
            CharacterMaster target = null;
            foreach (PlayerCharacterMasterController cmc in PlayerCharacterMasterController.instances)
            {
                if (cmc.GetDisplayName() == args[2])
                {
                    target = cmc.master;
                    break;
                }
            }
            if (!target)
            {
                Debug.LogErrorFormat("Unable to find playerobject with name {0}", args[2]);
                return;
            }
            ItemIndex itemind = ItemCatalog.FindItemIndex(args[0]);
            
            if (itemind == ItemIndex.None)
            {
                if (Int32.TryParse(args[0], out int direct)) itemind = (ItemIndex)direct;
                else
                {
                    Debug.LogErrorFormat("Could not find corresponding item index for {0}", args[0]);
                    return;
                }
            }
            if (!target.inventory)
            {
                Debug.LogErrorFormat("Player {0} does not have an inventory object!", args[3]);
                return;
            }
            if (!Int32.TryParse(args[1], out int count))
            {
                Debug.LogErrorFormat("{0} is not a valid count!", args[1]);
                return;
            }
            ItemDef itemdef = ItemCatalog.GetItemDef(itemind);
            if (!itemdef)
            {
                Debug.LogErrorFormat("No such item with index {0}", args[0]);
                return;
            }
            target.inventory.GiveItem(itemdef);
        }
    }
}

