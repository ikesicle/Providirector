using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RoR2;
using RoR2.CameraModes;
using R2API.Utils;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine.Networking;
using ProvidirectorGame;
using RoR2.CharacterAI;
using RoR2.Stats;
using RoR2.UI;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using TMPro;
using IL.RoR2.Artifacts;
using RoR2.Artifacts;

#pragma warning disable Publicizer001

namespace DacityP
{
    /*
     * PregameController - Controls Pregame actions, including char select
     * CharacterSelectController - Controls Character Selection screen
     */
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync)]
    [BepInDependency("com.bepis.r2api", BepInDependency.DependencyFlags.HardDependency)]
    // I'll add a more elegant way to enable and disable the mod later, but for now the setting is in modoptions.
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("com.DacityP.Providirector", "Providirector", "0.0.1")]
    public class Providirector : BaseUnityPlugin
    {
        private static ConfigEntry<bool> _modEnabled;
        private static readonly bool _modEnabledfallback = true;
        public static bool ModEnabled => _modEnabled == null ? _modEnabledfallback : _modEnabled.Value;
        
        
        private static ConfigEntry<bool> _debugEnabled;
        private static readonly bool _debugEnabledFallback = false;
        public static bool debugEnabled => _debugEnabled == null ? _debugEnabledFallback : _debugEnabled.Value;

        private static ConfigEntry<float> _vanillaCreditScale;
        private static readonly float _vanillaCreditScaleFallback = 0.85f;
        public static float vanillaCreditScale => _vanillaCreditScale == null ? _vanillaCreditScaleFallback : _vanillaCreditScale.Value;

        private static ConfigEntry<float> _directorCredInit;
        private static ConfigEntry<float> _directorCredGain;
        private static ConfigEntry<int> _directorWalletInit;
        private static ConfigEntry<int> _directorWalletGain;

        public static bool runIsActive = false;
        private static Harmony harmonyInstance;
        private static GameObject hud;
        //private static GameObject commandPanelPrefab;
        private LocalUser localUser => LocalUserManager.readOnlyLocalUsersList[0];
        private NetworkUser directorUser => localUser.currentNetworkUser;
        private GameObject spectateTarget;
        private CharacterMaster currentMaster;
        private CharacterMaster defaultMaster;
        private CombatSquad currentSquad;

        private PlayerCharacterMasterController currentController => currentMaster?.playerCharacterMasterController;
        private CharacterBody currentbody
        {
            get
            {
                if (currentMaster) return currentMaster.GetBody();
                return null;
            }
        }
        private CameraRigController maincam => directorUser.cameraRigController;
        private BaseAI currentai;
        private AssetBundle assets;
        private AssetBundle icons;
        private GameObject activehud;
        private HealthBar targethb;
        private TextMeshProUGUI spnamelabel;

        private bool scecontrolnext;
        private bool scecontrolcurrent;

        public static Providirector instance;

        // Compat
        private PluginInfo umbralMithrix;

        public void Awake()
        {
            RoR2Application.isModded = true;
            RoR2.Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
            Run.onServerGameOver += Run_onServerGameOver;
            On.RoR2.Run.Start += Run_Start;
            CommandHelper.AddToConsoleWhenReady();

            var path = System.IO.Path.GetDirectoryName(Info.Location);
            assets = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "providirectorui"));
            hud = assets.LoadAsset<GameObject>("ProvidirectorUIRoot");
            icons = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "monstericons"));
            MonsterIcon.AddIconsFromBundle(icons);

            harmonyInstance = new Harmony(Info.Metadata.GUID);
            if (Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions")) SetupRiskOfOptions();
            RunHookSetup();
        }

        public void Start()
        {
            if (Chainloader.PluginInfos.TryGetValue("com.Nuxlar.UmbralMithrix", out var _umbralMithrix)) umbralMithrix = _umbralMithrix;
        }

        private void RunHookSetup()
        {
            RoR2Application.onUpdate += RoR2Application_onUpdate;
            GlobalEventManager.onCharacterDeathGlobal += SwapTargetAfterDeath;
            On.RoR2.Run.OnServerSceneChanged += Run_OnServerSceneChanged;
            On.RoR2.RunCameraManager.Update += RunCameraManager_Update;
            On.RoR2.Run.OnUserAdded += Run_OnUserAdded;
            On.RoR2.Run.BeginGameOver += Run_BeginGameOver;
            On.RoR2.CombatDirector.Awake += CombatDirector_Awake;
            On.RoR2.CharacterSpawnCard.GetPreSpawnSetupCallback += NewPrespawnSetup;
            On.RoR2.MapZone.TeleportBody += MapZone_TeleportBody;
            On.RoR2.VoidRaidGauntletController.Start += VoidlingReady;
            On.RoR2.ScriptedCombatEncounter.BeginEncounter += SCEControlGate;
            On.RoR2.ArenaMissionController.BeginRound += FieldCardUpdate;
            On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.PreEncounterBegin += MithrixPlayerSetup;
            On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.OnMemberAddedServer += MithrixPlayerExecute;
            On.EntityStates.Missions.BrotherEncounter.PreEncounter.OnEnter += PreEncounterReady;
            On.EntityStates.Missions.BrotherEncounter.Phase1.OnEnter += Phase1Ready;
            On.EntityStates.Missions.BrotherEncounter.Phase2.OnEnter += Phase2Ready;
            On.EntityStates.Missions.BrotherEncounter.Phase3.OnEnter += Phase3Ready;
            On.EntityStates.Missions.BrotherEncounter.EncounterFinished.OnEnter += EncounterFinish;
            if (harmonyInstance != null) harmonyInstance.PatchAll(typeof(HarmonyPatches));
        }

        private void SwapTargetAfterDeath(DamageReport obj)
        {
            if (!runIsActive) return;
            if (obj.victimMaster.GetBodyObject() == spectateTarget)
            {
                Debug.Log("Current spectator target died, waiting to swap to the next target.");
                Invoke("ChangeNextTarget", 3);
            }
        }

        private void FieldCardUpdate(On.RoR2.ArenaMissionController.orig_BeginRound orig, ArenaMissionController self)
        {
            orig(self);
            if (!runIsActive) return;
            DirectorState.spawnCardTemplates.Clear();
            foreach (DirectorCard c in self.activeMonsterCards) DirectorState.spawnCardTemplates.Add(c.spawnCard);
            DirectorState.monsterInv = ArenaMissionController.instance.inventory;
            DirectorState.instance.isDirty = true;
            DirectorState.instance.rateModifier = DirectorState.RateModifier.None;
        }

        private void SCEControlGate(On.RoR2.ScriptedCombatEncounter.orig_BeginEncounter orig, ScriptedCombatEncounter self)
        {
            if (self.hasSpawnedServer || !NetworkServer.active) return;
            if (!runIsActive) { orig(self); return; }
            if (scecontrolnext)
            {
                scecontrolnext = false;
                scecontrolcurrent = true;
                currentSquad = self.combatSquad;
                self.onBeginEncounter += delegate(ScriptedCombatEncounter _) {
                    scecontrolcurrent = false;
                };
                currentSquad.onDefeatedServer += delegate ()
                {
                    Debug.Log("Combat squad defeated, reverting to null");
                    currentSquad = null;
                };
            }
            orig(self);
        }

        private void VoidlingReady(On.RoR2.VoidRaidGauntletController.orig_Start orig, VoidRaidGauntletController self)
        {
            orig(self);
            if (runIsActive) defaultMaster.GetBodyObject().layer = LayerIndex.noCollision.intVal;
        }

        private void PreEncounterReady(On.EntityStates.Missions.BrotherEncounter.PreEncounter.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.PreEncounter self)
        {
            orig(self);
            if (runIsActive && defaultMaster && defaultMaster.GetBodyObject()) defaultMaster.GetBodyObject().layer = LayerIndex.noCollision.intVal;
            else Debug.LogError("Unable to find the default master for the director!");
        }

        private void MapZone_TeleportBody(On.RoR2.MapZone.orig_TeleportBody orig, MapZone self, CharacterBody characterBody)
        {
            // Special exception
            if (defaultMaster && characterBody == defaultMaster.GetBody())
            {
                Debug.LogWarning("In-zone TP cancelled for the director.");
                return;
            }
            orig(self, characterBody);
        }

        private void Run_BeginGameOver(On.RoR2.Run.orig_BeginGameOver orig, Run self, GameEndingDef gameEndingDef)
        {
            if (debugEnabled && runIsActive && !gameEndingDef.isWin)
            {

                //Debug.LogWarning("Game Over prevented by Providirector's Debug Mode. To turn this off, ask the server host to disable the Providirector debug mode in mod settings.");
                return;
            }
            orig(self, gameEndingDef);
        }

        private void SetupRiskOfOptions()
        {
            Debug.LogWarning("Setting up Risk of Options for Providirector!");
            _modEnabled = Config.Bind<bool>("General", "Mod Enabled", true, "If checked, the mod is enabled and will be started in any multiplayer games where there are 2 or more players, and you are the host.");
            _debugEnabled = Config.Bind("General", "Debug Mode", false, "Whether or not debug mode is enabled. This enables the mod to run in singleplayer games and enables more controls for Director mode (targeting non-player bodies, debug Lemurian, etc.)\nNOTE: DO NOT LEAVE THIS ON DURING REGULAR GAMEPLAY!\nTHIS MODE PREVENTS GAME OVERS AND IS PRONE TO SOFTLOCKS.");
            _directorCredInit = Config.Bind<float>("Director", "Initial Credit", DirectorState.baseCreditGain, String.Format("The amount of credits gained by the player director per second. Default value is {0}.", DirectorState.baseCreditGain));
            _directorCredGain = Config.Bind<float>("Director", "Credit Gain Per Level", DirectorState.creditGainPerLevel, String.Format("The amount credit gain increases with level. Default value is {0}.", DirectorState.creditGainPerLevel));
            _directorWalletInit = Config.Bind<int>("Director", "Initial Capacity", (int)DirectorState.baseWalletSize, String.Format("The base maximum capacity of the player director wallet. Default value is {0}.", (int)DirectorState.baseWalletSize));
            _directorWalletGain = Config.Bind<int>("Director", "Capacity Gain Per Level", (int)DirectorState.walletGainPerLevel, String.Format("The amount wallet size increases with level. Default value is {0}.", (int)DirectorState.walletGainPerLevel));
            _vanillaCreditScale = Config.Bind<float>("Vanilla Mods", "Vanilla Director Credit", 0.85f, "How much the vanilla directors have their credit gain scaled. Default value is 85%.");
            ModSettingsManager.AddOption(new CheckBoxOption(_modEnabled));
            ModSettingsManager.AddOption(new SliderOption(_directorCredInit, new SliderConfig { min = 0f, max = 10f, formatString = "{0:G2}" }));
            ModSettingsManager.AddOption(new SliderOption(_directorCredGain, new SliderConfig { min = 0f, max = 3f, formatString = "{0:G2}" }));
            ModSettingsManager.AddOption(new IntSliderOption(_directorWalletInit, new IntSliderConfig { min = 0, max = 100 }));
            ModSettingsManager.AddOption(new IntSliderOption(_directorWalletGain, new IntSliderConfig { min = 0, max = 100 }));
            ModSettingsManager.AddOption(new SliderOption(_vanillaCreditScale, new SliderConfig { min = 0f, max = 1f, formatString = "{0:P0}" }));
            ModSettingsManager.AddOption(new CheckBoxOption(_debugEnabled));
        }

        private void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            if (ModEnabled && NetworkServer.active && (self.participatingPlayerCount > 1 || debugEnabled))
            {
                runIsActive = true;
                Debug.Log("Providirector has been set up for this run!");
                if (LocalUserManager.readOnlyLocalUsersList == null) { Debug.Log("No local users! Something is terribly wrong."); return; }
            }
            orig(self);
        }

        private void Run_onServerGameOver(Run run, GameEndingDef ending)
        {
            Run_onRunDestroyGlobal(run);
        }

        private void Run_onRunDestroyGlobal(Run obj)
        {
            if (activehud) Destroy(activehud);
            activehud = null;
            runIsActive = false;
            spectateTarget = null;
            currentMaster = null;
            defaultMaster = null;
        }

        void OnEnable()
        {
            instance = this;
        }

        void OnDisable()
        {
            instance = null;
        }

        private void EncounterFinish(On.EntityStates.Missions.BrotherEncounter.EncounterFinished.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.EncounterFinished self)
        {
            orig(self);
            if (DirectorState.instance) DirectorState.instance.rateModifier = DirectorState.RateModifier.TeleporterBoosted;
            if (runIsActive)
            {
                TeleportHelper.TeleportGameObject(currentMaster.GetBodyObject(), new Vector3(303, -169, 394));
                //defaultMaster.GetBodyObject().layer = LayerIndex.playerBody.intVal;
            }
        }

        private void Phase3Ready(On.EntityStates.Missions.BrotherEncounter.Phase3.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.Phase3 self)
        {
            orig(self);
            if (DirectorState.instance) DirectorState.instance.rateModifier = DirectorState.RateModifier.Locked;
        }

        private void Phase2Ready(On.EntityStates.Missions.BrotherEncounter.Phase2.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.Phase2 self)
        {
            orig(self);
            if (DirectorState.instance)
            {
                if (umbralMithrix != null) DirectorState.instance.rateModifier = DirectorState.RateModifier.Locked;
                else DirectorState.instance.rateModifier = DirectorState.RateModifier.TeleporterBoosted;
            }
        }

        private void Phase1Ready(On.EntityStates.Missions.BrotherEncounter.Phase1.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.Phase1 self)
        {
            orig(self);
            if (DirectorState.instance) DirectorState.instance.rateModifier = DirectorState.RateModifier.Locked;
        }

        private Action<CharacterMaster> NewPrespawnSetup(On.RoR2.CharacterSpawnCard.orig_GetPreSpawnSetupCallback orig, CharacterSpawnCard self)
        {
            return (CharacterMaster c) =>
            {
                PlayerCharacterMasterController cmc = c.GetComponent<PlayerCharacterMasterController>();
                PlayerStatsComponent psc = c.GetComponent<PlayerStatsComponent>();
                if (scecontrolcurrent)
                {
                    if (!cmc) cmc = c.gameObject.AddComponent<PlayerCharacterMasterController>();
                    cmc.enabled = false;
                    if (!psc) c.gameObject.AddComponent<PlayerStatsComponent>();
                    Debug.LogFormat("Added player controls to {0}", c.name);
                }
            };
        }

        private void MithrixPlayerExecute(On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.orig_OnMemberAddedServer orig, EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState self, CharacterMaster master)
        {
            orig(self, master);
            if ((self.phaseControllerChildString == "Phase2" && umbralMithrix == null) || !runIsActive || (defaultMaster != currentMaster)) return;
            AddPlayerControl(master);
        }

        private void MithrixPlayerSetup(On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.orig_PreEncounterBegin orig, EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState self)
        {
            orig(self);
            if ((self.phaseControllerChildString == "Phase2" && umbralMithrix == null) || !runIsActive) return;
            scecontrolnext = true;
        }

        private void CombatDirector_Awake(On.RoR2.CombatDirector.orig_Awake orig, CombatDirector self)
        {
            if (runIsActive)
            {
                self.creditMultiplier *= vanillaCreditScale;
            }
            orig(self);
        }

        private void RoR2Application_onUpdate()
        {
            if (!runIsActive) return;
            if (directorUser == null) return;

            if (spectateTarget == null && !currentMaster) ChangeNextTarget(); // Attempt to lock on to anything at all, every frame - This is to prevent problems with the camera not auto-locking
            InputManager.SwapPage.PushState(Input.GetKey(KeyCode.Space));
            InputManager.Slot1.PushState(Input.GetKey(KeyCode.Alpha1));
            InputManager.Slot2.PushState(Input.GetKey(KeyCode.Alpha2));
            InputManager.Slot3.PushState(Input.GetKey(KeyCode.Alpha3));
            InputManager.Slot4.PushState(Input.GetKey(KeyCode.Alpha4));
            InputManager.Slot5.PushState(Input.GetKey(KeyCode.Alpha5));
            InputManager.Slot6.PushState(Input.GetKey(KeyCode.Alpha6));
            InputManager.DebugSpawn.PushState(Input.GetKey(KeyCode.Alpha0));
            InputManager.BoostTarget.PushState(Input.GetKey(KeyCode.B));
            InputManager.ToggleAffixCommon.PushState(Input.GetKey(KeyCode.C));
            InputManager.ToggleAffixRare.PushState(Input.GetKey(KeyCode.V));
            InputManager.NextTarget.PushState(Input.GetKey(KeyCode.Mouse0));
            InputManager.PrevTarget.PushState(Input.GetKey(KeyCode.Mouse1));
            InputManager.FocusTarget.PushState(Input.GetKey(KeyCode.F));
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            if (directorUser && maincam)
            {
                pos = maincam.sceneCam.transform.position;
                rot = maincam.sceneCam.transform.rotation;
                pos += rot * new Vector3(0, 0, 5);
            }
            if (DirectorState.instance == null) return;
            bool honorenabled = RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.EliteOnly);
            if (InputManager.ToggleAffixCommon.justPressed && !honorenabled) DirectorState.instance.eliteTier = DirectorState.eliteTiers[1];
            if (InputManager.ToggleAffixCommon.justReleased && !honorenabled) DirectorState.instance.eliteTier = DirectorState.eliteTiers[0];
            if (InputManager.ToggleAffixRare.justPressed) DirectorState.instance.eliteTier = DirectorState.eliteTiers[3];
            if (InputManager.ToggleAffixRare.justReleased) DirectorState.instance.eliteTier = DirectorState.eliteTiers[honorenabled ? 2 : 0];
            if ((localUser.eventSystem && localUser.eventSystem.isCursorVisible) || currentMaster != defaultMaster) return;
            if (InputManager.DebugSpawn.justPressed && debugEnabled) AddPlayerControl(Spawn("LemurianMaster", "LemurianBody", pos, rot));
            if (InputManager.NextTarget.justPressed) ChangeNextTarget();
            if (InputManager.PrevTarget.justPressed) ChangePreviousTarget();
            if (InputManager.Slot1.justPressed) DirectorState.instance.TrySpawn(0, pos, rot);
            if (InputManager.Slot2.justPressed) DirectorState.instance.TrySpawn(1, pos, rot);
            if (InputManager.Slot3.justPressed) DirectorState.instance.TrySpawn(2, pos, rot);
            if (InputManager.Slot4.justPressed) DirectorState.instance.TrySpawn(3, pos, rot);
            if (InputManager.Slot5.justPressed) DirectorState.instance.TrySpawn(4, pos, rot);
            if (InputManager.Slot6.justPressed) DirectorState.instance.TrySpawn(5, pos, rot);
            if (InputManager.SwapPage.justPressed) DirectorState.instance.secondPage = !DirectorState.instance.secondPage;
            if (InputManager.FocusTarget.justPressed)
            {
                if (spectateTarget) {
                    CharacterMaster target = spectateTarget.GetComponent<CharacterMaster>();
                    foreach (TeamComponent tc in TeamComponent.GetTeamMembers(TeamIndex.Monster))
                    {
                        CharacterMaster c = tc.body.master;
                        if (target == c) continue;
                        foreach (BaseAI ai in c.aiComponents)
                        {
                            ai.currentEnemy.gameObject = spectateTarget;
                            ai.enemyAttentionDuration = 10f;
                        }
                    }
                }
            }
            if (InputManager.BoostTarget.justPressed) DirectorState.instance.ApplyFrenzy();
        }

        private void Run_OnUserAdded(On.RoR2.Run.orig_OnUserAdded orig, Run self, NetworkUser user)
        {
            orig(self, user);
            if (!user.master) { Debug.LogWarning("No master found on the spawned player!"); return; }
            if (!runIsActive) return;
            else if (user != directorUser)
            {
                if (user) spectateTarget = user.master.GetBodyObject();
                return;
            }
                // At this point we know that the user being added is the player who will be the director

            defaultMaster = user.master;
            defaultMaster.bodyPrefab = BodyCatalog.FindBodyPrefab("WispBody");
            defaultMaster.godMode = true;
            
            defaultMaster.teamIndex = TeamIndex.Neutral;
            currentMaster = defaultMaster;
            currentMaster.inventory.GiveItem(RoR2Content.Items.TeleportWhenOob);
            
            currentai = null;
            var bodysetupdel = (CharacterBody body) =>
            {
                if (!body)
                {
                    Debug.LogWarning("No body object found!");
                    return;
                }
                body.AddBuff(RoR2Content.Buffs.Cloak);
                body.AddBuff(RoR2Content.Buffs.Intangible);
                body.AddBuff(RoR2Content.Buffs.Entangle);
                Debug.LogWarning("Added buffs.");
                body.teamComponent.teamIndex = TeamIndex.Neutral;
                body.skillLocator.primary = null;
                body.skillLocator.secondary = null;
                body.skillLocator.utility = null;
                body.skillLocator.special = null;
                body.master.preventGameOver = false;
                body.gameObject.layer = LayerIndex.noCollision.intVal;
                ChangeNextTarget();
                Debug.Log("Setup complete!");
            };
            defaultMaster.onBodyStart += bodysetupdel;
        }

        private void Run_OnServerSceneChanged(On.RoR2.Run.orig_OnServerSceneChanged orig, Run self, string sceneName)
        {
            orig(self, sceneName);
            if (runIsActive) SetupSceneChange();
        }

        private void RunCameraManager_Update(On.RoR2.RunCameraManager.orig_Update orig, RunCameraManager self)
        {
            if (!runIsActive)
            {
                orig(self);
                return;
            }
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
                    else if ((bool)networkUserBodyObject && defaultMaster != null && networkUserBodyObject != defaultMaster.GetBodyObject())
                    {
                        cameraRigController.nextTarget = networkUserBodyObject;
                        cameraRigController.cameraMode = CameraModePlayerBasic.playerBasic;
                    } else if (runIsActive) {
                        cameraRigController.nextTarget = spectateTarget;
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
            DirectorState.UpdateMonsterSelection();
            if 
            if (activehud == null)
            {
                activehud = Instantiate(hud);
                Debug.LogWarning("Instantiated new hud.");
            }
            Invoke("PostStart", 0.7f);
        }

        private void PostStart()
        {
            DisengagePlayerControl();
            Debug.Log("Starting post-init");
            activehud.GetComponent<Canvas>().worldCamera = maincam.uiCam;
            Debug.Log("Camera set.");
            if (DirectorState.instance != null) DirectorState.instance.RefreshForNewStage();
            else Debug.LogWarning("No DirectorState exists yet.");
            Debug.Log("UI Instantiated.");
            ChildLocator t = activehud.GetComponent<ChildLocator>();
            targethb = t.FindChild(0).GetComponent<HealthBar>();
            spnamelabel = t.FindChild(1).GetComponent<TextMeshProUGUI>();
            ChangeNextTarget();
            DirectorState.eliteTiers = CombatDirector.eliteTiers;
            Debug.LogFormat("Setting up in {0}", Stage.instance.sceneDef.baseSceneName);
            if (Stage.instance.sceneDef.baseSceneName.Equals("moon2"))
            {
                Debug.Log("Moon setup called");
                currentbody.gameObject.layer = LayerIndex.playerBody.intVal;
                TeleportHelper.TeleportGameObject(currentbody.gameObject, new Vector3(-47.0f, 524.0f, -23.0f));
            }
            else if (Stage.instance.sceneDef.baseSceneName.Equals("voidraid"))
            {
                Debug.Log("Voidling setup called");
                currentbody.gameObject.layer = LayerIndex.playerBody.intVal;
                TeleportHelper.TeleportGameObject(currentbody.gameObject, new Vector3(-81.0f, 50.0f, 82.0f));
            }
            else if (Stage.instance.sceneDef.baseSceneName.Equals("arena"))
            {
                Debug.Log("Void Field setup called");
                DirectorState.instance.rateModifier = DirectorState.RateModifier.Locked;
                DirectorState.spawnCardTemplates.Clear();
            }
            if (RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.MonsterTeamGainsItems)) DirectorState.monsterInv = RoR2.Artifacts.MonsterTeamGainsItemsArtifactManager.monsterTeamInventory;
            else DirectorState.monsterInv = null;
            if (RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.EliteOnly)) DirectorState.instance.eliteTier = DirectorState.eliteTiers[2];
            scecontrolcurrent = false;
            scecontrolnext = false;

            if (_modEnabled != null)
            {
                DirectorState.baseCreditGain = _directorCredInit.Value;
                DirectorState.creditGainPerLevel = _directorCredGain.Value;
                DirectorState.baseWalletSize = _directorWalletInit.Value;
                DirectorState.walletGainPerLevel = _directorWalletGain.Value;
            }
            HUDEnable();
            SetBaseUIVisible(false);
        }

        private void SetBaseUIVisible(bool value)
        {
            Transform root = maincam.hud.mainContainer.transform;
            Transform basicstats = root.Find("MainUIArea/SpringCanvas/BottomLeftCluster/BarRoots");
            Transform skillicons = root.Find("MainUIArea/SpringCanvas/BottomRightCluster");
            Transform notifs = root.Find("NotificationArea");
            Transform spectateinfo = root.Find("MainUIArea/SpringCanvas/BottomCenterCluster");
            if (basicstats) basicstats.gameObject.SetActive(value);
            if (skillicons) skillicons.gameObject.SetActive(value);
            if (notifs) notifs.gameObject.SetActive(value);
            if (spectateinfo) spectateinfo.gameObject.SetActive(value);
        }

        private void ChangeNextTarget()
        {
            ReadOnlyCollection<CharacterBody> readOnlyInstancesList = CharacterBody.readOnlyInstancesList;
            if (readOnlyInstancesList.Count == 0) return;
            CharacterBody characterBody = spectateTarget ? spectateTarget.GetComponent<CharacterBody>() : null;
            int num = (characterBody ? readOnlyInstancesList.IndexOf(characterBody) : 0);
            for (int i = num + 1; i < readOnlyInstancesList.Count; i++)
            {
                if ((readOnlyInstancesList[i].teamComponent && readOnlyInstancesList[i].teamComponent.teamIndex == TeamIndex.Player) || (debugEnabled && readOnlyInstancesList[i].teamComponent.teamIndex != TeamIndex.None))
                {
                    spectateTarget = readOnlyInstancesList[i].gameObject;
                    if (debugEnabled) Debug.LogFormat("Now spectating {0} on team {1}", readOnlyInstancesList[i].name, readOnlyInstancesList[i].teamComponent.teamIndex);
                    UpdateHUD();
                    CancelInvoke("ChangeNextTarget");
                    CancelInvoke("ChangePreviousTarget");
                    return;
                }
            }
            for (int j = 0; j <= num; j++)
            {
                if ((readOnlyInstancesList[j].teamComponent && readOnlyInstancesList[j].teamComponent.teamIndex == TeamIndex.Player) || (debugEnabled && readOnlyInstancesList[j].teamComponent.teamIndex != TeamIndex.None))
                {
                    spectateTarget = readOnlyInstancesList[j].gameObject;
                    if (debugEnabled) Debug.LogFormat("Now spectating {0} on team {1}", readOnlyInstancesList[j].name, readOnlyInstancesList[j].teamComponent.teamIndex);
                    UpdateHUD();
                    CancelInvoke("ChangeNextTarget");
                    CancelInvoke("ChangePreviousTarget");
                    return;
                }
            }
        }

        private void ChangePreviousTarget()
        {
            ReadOnlyCollection<CharacterBody> readOnlyInstancesList = CharacterBody.readOnlyInstancesList;
            if (readOnlyInstancesList.Count == 0)
            {
                spectateTarget = null;
                return;
            }
            CharacterBody characterBody = spectateTarget ? spectateTarget.GetComponent<CharacterBody>() : null;
            int num = (characterBody ? readOnlyInstancesList.IndexOf(characterBody) : 0);
            for (int i = num - 1; i >= 0; i--)
            {
                if ((readOnlyInstancesList[i].teamComponent && readOnlyInstancesList[i].teamComponent.teamIndex == TeamIndex.Player) || (debugEnabled && readOnlyInstancesList[i].teamComponent.teamIndex != TeamIndex.None))
                {
                    spectateTarget = readOnlyInstancesList[i].gameObject;
                    if (debugEnabled) Debug.LogFormat("Now spectating {0} on team {1}", readOnlyInstancesList[i].name, readOnlyInstancesList[i].teamComponent.teamIndex);
                    UpdateHUD();
                    CancelInvoke("ChangeNextTarget");
                    CancelInvoke("ChangePreviousTarget");
                    return;
                }
            }
            for (int j = readOnlyInstancesList.Count - 1; j >= num; j--)
            {
                if ((readOnlyInstancesList[j].teamComponent && readOnlyInstancesList[j].teamComponent.teamIndex == TeamIndex.Player) || (debugEnabled && readOnlyInstancesList[j].teamComponent.teamIndex != TeamIndex.None))
                {
                    spectateTarget = readOnlyInstancesList[j].gameObject;
                    if (debugEnabled) Debug.LogFormat("Now spectating {0} on team {1}", readOnlyInstancesList[j].name, readOnlyInstancesList[j].teamComponent.teamIndex);
                    UpdateHUD();
                    CancelInvoke("ChangeNextTarget");
                    CancelInvoke("ChangePreviousTarget");
                    return;
                }
            }
        }

        private void UpdateHUD()
        {
            if (spectateTarget && targethb) targethb.source = spectateTarget.GetComponent<HealthComponent>();
            if (spectateTarget && spnamelabel) spnamelabel.text = Util.GetBestBodyName(spectateTarget);
        }

        private void AddPlayerControl(CharacterMaster c)
        {
            if (c == null || c == currentMaster)
            {
                Debug.LogWarning("Attempt to switch control onto a nonexistent or already present character!");
                return;
            }
            Debug.LogFormat("Attempting to take control of CharacterMaster {0}", c.name);
            if (currentMaster) DisengagePlayerControl(revertfallback: false);
            else Debug.Log("No currently set master - we can proceed as normal.");
            currentMaster = c;
            currentai = currentMaster.GetComponent<BaseAI>();
            currentMaster.playerCharacterMasterController = currentMaster.GetComponent<PlayerCharacterMasterController>();
            PlayerStatsComponent playerStatsComponent = currentMaster.GetComponent<PlayerStatsComponent>();
            if (!currentController)
            {
                Debug.LogWarningFormat("CharacterMaster {0} does not have a PCMC! Instantiating one now... though this will lead to desyncs between the client and server.", c.name);
                currentMaster.playerCharacterMasterController = c.gameObject.AddComponent<PlayerCharacterMasterController>();
            }
            if (!playerStatsComponent)
            {
                Debug.LogWarningFormat("CharacterMaster {0} does not have a PSC! Instantiating one now... though this will lead to desyncs between the client and server.", c.name);
                playerStatsComponent = c.gameObject.AddComponent<PlayerStatsComponent>();
            }
            GameObject oldprefab = c.bodyPrefab;
            currentController.LinkToNetworkUserServer(directorUser);
            currentController.master.bodyPrefab = oldprefab; // RESET
            currentMaster.preventGameOver = false;
            if (c != defaultMaster) HUDDisable();
            else
            {
                HUDEnable();
                ChangeNextTarget();
            }
            currentController.enabled = true;
            playerStatsComponent.enabled = true;
            Run.instance.userMasters[directorUser.id] = c;
            AIDisable();
            if (currentai) currentai.onBodyDiscovered += AIDisable;
            currentMaster.onBodyDeath.AddListener(onNewMasterDeath);
            if (currentbody) {
                //currentbody.networkIdentity.localPlayerAuthority = true;
                //currentbody.networkIdentity.AssignClientAuthority(directorUser.connectionToClient);
            }
            currentMaster.onBodyStart += delegate(CharacterBody b) {
                b.master.preventGameOver = false;
                //b.networkIdentity.localPlayerAuthority = true;
                //b.networkIdentity.AssignClientAuthority(directorUser.connectionToClient);
            };
            SetBaseUIVisible(c != defaultMaster);
            if (c == defaultMaster) ChangeNextTarget();
            Debug.LogFormat("{0} set as new master.", currentMaster);
            void onNewMasterDeath()
            {
                currentMaster.onBodyDeath.RemoveListener(onNewMasterDeath);
                Debug.Log("Current Master has died, checking if we should disengage.");
                if (currentMaster.IsDeadAndOutOfLivesServer())
                {
                    DisengagePlayerControl();
                }
                else Debug.Log("No need to disengage, we have a pending revive.");
            }
        }

        private void DisengagePlayerControl(bool revertfallback = true)
        {
            Debug.LogFormat("Disengaging player control from {0}...", currentMaster);
            if (currentMaster)
            {
                if (currentMaster != defaultMaster)
                {
                    Debug.Log("Non-default character, performing special remove...");
                    if (currentai) currentai.onBodyDiscovered -= AIDisable;
                    AIEnable();
                    currentai = null;
                    if (currentbody && currentbody.networkIdentity) currentbody.networkIdentity.RemoveClientAuthority(directorUser.connectionToClient);
                    currentMaster.playerCharacterMasterController = null;
                }
                if (currentController) currentController.enabled = false;
                Debug.LogFormat("Characterbody disengaged! There are now {0} active PCMCs", PlayerCharacterMasterController.instances.Count);
                currentMaster = null;
            }
            if (currentMaster == null && revertfallback)
            {
                if (currentSquad)
                {
                    Debug.Log("Swapping to next active master in current squad...");
                    foreach (CharacterMaster candidate in currentSquad.readOnlyMembersList)
                    {
                        if (!candidate.IsDeadAndOutOfLivesServer())
                        {
                            AddPlayerControl(candidate);
                            return;
                        }
                    }
                    Debug.Log("No alive candidates. Proceeding.");
                }
                Debug.Log("Reverting to default master...");
                AddPlayerControl(defaultMaster);
            }
        }

        private void AIDisable()
        {
            if (currentai)
            {
                if (currentbody) currentai.OnBodyLost(currentbody);
                currentai.enabled = false;
                //Debug.Log("AI Disabled.");
            }
            else
            {
                Debug.LogWarning("Warning: No AI component to disable.");
            }
        }

        private void AIDisable(CharacterBody _) { AIDisable(); }
        
        private void AIEnable()
        {
            if (currentai)
            {
                currentai.enabled = true;
                if (currentbody) currentai.OnBodyStart(currentbody);
                //Debug.Log("AI Enabled.");
            }
        }

        public CharacterMaster Spawn(string mastername, string bodyname, Vector3 position, Quaternion rotation, EliteDef eliteDef = null, int levelbonus = 0, bool includePlayerControlInterface = true, bool bigBuffs = true)
        {
            // Modified code taken from DebugToolkit
            GameObject preinst = MasterCatalog.FindMasterPrefab(mastername);
            GameObject preinstbody = BodyCatalog.FindBodyPrefab(bodyname);
            if (!preinst || !preinstbody) return null;
            GameObject bodyGameObject = Instantiate(preinst, position, rotation);
            CharacterMaster master = bodyGameObject.GetComponent<CharacterMaster>();
            if (includePlayerControlInterface)
            {
                bodyGameObject.AddComponent<PlayerCharacterMasterController>().enabled = false;
                if (!bodyGameObject.GetComponent<PlayerStatsComponent>())
                {
                    Debug.Log("CharacterMaster does not have stat component. Adding...");
                    bodyGameObject.AddComponent<PlayerStatsComponent>().enabled = false;
                }
            }
            NetworkServer.Spawn(bodyGameObject);
            master.bodyPrefab = preinstbody;
            master.SpawnBody(position, Quaternion.identity);
            master.inventory.GiveItem(RoR2Content.Items.UseAmbientLevel);
            if (bigBuffs)
            {
                master.inventory.GiveItem(RoR2Content.Items.Syringe, 100);
                master.inventory.GiveItem(RoR2Content.Items.Hoof, 20);
                master.inventory.GiveItem(RoR2Content.Items.Knurl, 100);
                master.inventory.GiveItem(RoR2Content.Items.Feather, 5);
            }
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

        private void HUDDisable()
        {
            if (activehud)
            {
                activehud.SetActive(false);
            }
        }

        private void HUDEnable()
        {
            if (activehud)
            {
                activehud.SetActive(true);
            }
        }

        [ConCommand(commandName = "check_cameras", flags = ConVarFlags.None, helpText = "Checks the state of all currently active CRCs.")]
        private static void CCCheckCameras(ConCommandArgs _)
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

        [ConCommand(commandName = "prvd_rundata", flags = ConVarFlags.None, helpText = "Check the data of the current run.")]
        private static void CCRunData(ConCommandArgs _)
        {
            if (!Run.instance)
            {
                Debug.Log("-- No run data present");
                return;
            }
            Debug.LogFormat("");
            Debug.LogFormat("Time: {0}", Run.instance.GetRunStopwatch());
            Debug.LogFormat("Players: {0}", Run.instance.participatingPlayerCount);
            Debug.LogFormat("PCMC Instances: {0}", PlayerCharacterMasterController.instances.Count);
            Debug.LogFormat("Stages Cleared: {0}", Run.instance.stageClearCount);
            Debug.LogFormat("Difficulty Scaling: {0}", Run.instance.difficultyCoefficient);
            Debug.LogFormat("Difficulty Level: {0}", Run.instance.ambientLevel);
        }

        [ConCommand(commandName = "prvd_dump", flags = ConVarFlags.None, helpText = "Dumps director data.")]
        private static void CCDumpVars(ConCommandArgs _)
        {
            Debug.Log("Providirector Data ---");
            Debug.LogFormat("Mod Enabled: {0} {1}", ModEnabled, debugEnabled ? "Debug" : "Normal");
            Debug.LogFormat("Active Run: ", runIsActive);
            Debug.LogFormat("Director CMaster: {0}", instance.currentMaster);
            Debug.LogFormat("Director DMaster: {0}", instance.defaultMaster);
            Debug.LogFormat("SpectateTarget: {0}", instance.spectateTarget);
        }

        [ConCommand(commandName = "prvd_players", flags = ConVarFlags.None, helpText = "Dumps player data.")]
        private static void CCDumpPlayer(ConCommandArgs _)
        {
            Debug.Log("Player Data ---");
            Debug.LogFormat("PCMC Count: {0}", PlayerCharacterMasterController.instances.Count);
            Debug.LogFormat("In-game count: {0}", Run.instance.participatingPlayerCount);
            Debug.Log("---------------");
            foreach (PlayerCharacterMasterController pcmc in PlayerCharacterMasterController.instances)
            {
                Debug.LogFormat("{0}: {1}", pcmc.GetDisplayName(), pcmc.preventGameOver);
            }
        }
    }
}

