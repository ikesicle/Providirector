using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RoR2;
using RoR2.CameraModes;
using RoR2.CharacterAI;
using RoR2.Stats;
using RoR2.UI;
using RoR2.Networking;
using R2API.Utils;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine.Networking;
using ProvidirectorGame;
using Providirector.NetworkCommands;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using TMPro;
using BepInEx.Logging;
using HG;

#pragma warning disable Publicizer001

namespace DacityP
{
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync)]
    [BepInDependency("com.bepis.r2api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.DacityP.Providirector", "Providirector", "0.0.1")]
    public class Providirector : BaseUnityPlugin
    {
        private static ConfigEntry<bool> _modEnabled;
        private static readonly bool _modEnabledfallback = true;
        public static bool modEnabled => _modEnabled == null ? _modEnabledfallback : _modEnabled.Value;
        
        
        private static ConfigEntry<bool> _debugEnabled;
        private static readonly bool _debugEnabledFallback = false;
        public static bool debugEnabled => _debugEnabled == null ? _debugEnabledFallback : _debugEnabled.Value;

        private static ConfigEntry<bool> _nearestNodeSnap;
        private static readonly bool _nearestNodeSnapFallback = true;
        public static bool nearestNodeSnap => _debugEnabled == null ? _nearestNodeSnapFallback : _nearestNodeSnap.Value;

        private static ConfigEntry<float> _vanillaCreditScale;
        private static readonly float _vanillaCreditScaleFallback = 0.85f;
        public static float vanillaCreditScale => _vanillaCreditScale == null ? _vanillaCreditScaleFallback : _vanillaCreditScale.Value;

        private static ConfigEntry<bool> _skipClientVerification;
        private static readonly bool _skipClientVerificationFallback = true;
        public static bool skipClientVerification => _skipClientVerification == null ? _skipClientVerificationFallback : _skipClientVerification.Value;

        private static ConfigEntry<float> _directorCredInit;
        private static ConfigEntry<float> _directorCredGain;
        private static ConfigEntry<int> _directorWalletInit;
        private static ConfigEntry<int> _directorWalletGain;

        // General
        public static bool runIsActive = false;
        private static AssetBundle assets;
        private static AssetBundle icons;
        private static GameObject hudPrefab;
        private static GameObject serverDirectorPrefab;
        private static GameObject clientDirectorPrefab;

        // Server mode director things
        private static Harmony harmonyInstance;
        private static LocalUser localUser => LocalUserManager.readOnlyLocalUsersList[0];
        private CharacterMaster currentMaster;
        private CharacterMaster defaultMaster;
        private DirectorState serverModeDirector;
        private DirectorState clientModeDirector;
        private CombatSquad currentSquad;
        private PlayerCharacterMasterController currentController => currentMaster?.playerCharacterMasterController;
        private CharacterBody currentBody
        {
            get
            {
                if (currentMaster) return currentMaster.GetBody();
                return null;
            }
        }
        private BaseAI currentAI;
        private GameObject activeServerDirectorObject;
        private bool scriptEncounterControlNext;
        private bool scriptEncounterControlCurrent;
        private bool forceScriptEncounterAddControl = false;

        // Client mode director things
        private CameraRigController mainCamera => directorUser?.cameraRigController;
        private GameObject activeHud;
        private GameObject activeClientDirectorObject;
        private HealthBar targetHealthbar;
        private TextMeshProUGUI spectateLabel;
        private GameObject spectateTarget;
        private GameObject spectateTargetMaster => spectateTarget.GetComponent<CharacterBody>().masterObject;
        private bool isInPlayerControlMode = false;

        // Advanced Camera Controls for Voidling, copied from FirstPersonRedux
        private static readonly Vector3 frontalCamera = new Vector3(0f, 0f, 0f);
        private CameraTargetParams.CameraParamsOverrideHandle fpHandle;
        private CharacterCameraParamsData cpData;

        

        public static Providirector instance;

        // Networking parameters
        private static NetworkUser _directorUser;
        private static NetworkUser directorUser
        {
            get { return _directorUser; }
            set
            {
                _directorUser = value;
                PLog("Director User set locally to {0}", _directorUser?.GetNetworkPlayerName().GetResolvedName());
            }
        }
        private static bool directorIsLocal => runIsActive && directorUser == localUser.currentNetworkUser;
        private static bool haveControlAuthority => NetworkServer.active;

        // Other locally defined constants
        private const short prvdChannel = 12345;
        private readonly Vector3 rescueShipTriggerZone = new Vector3(303f, -100f, 393f);
        private readonly Vector3 moonFightTriggerZone = new Vector3(-47.0f, 524.0f, -23.0f);
        private readonly Vector3 voidlingTriggerZone = new Vector3(-81.0f, 50.0f, 82.0f);

        private NetworkConnection serverDirectorConnection => directorUser.connectionToClient;
        private NetworkConnection localToServerConnection => localUser.currentNetworkUser.connectionToServer;


        /* Basic idea of how this will work:
         * While the game hasn't started yet, one can set the directorUser using a command like /director [username]
         * - When attempting to set a player as the director user, the handshake channel is first used to 
         * attempt to ping the networkUser (to see if they have Providirector installed, and if the version is the same)
         * - If successful, the server sets its directorUser as the client, and the client sets its networkuser to itself.
         * - On game start, the server will spawn its own "server-mode" directorstate instance, which always has max money and a burst which is always filled (except when discharging). It will then use the command channel to send a message to the client to start, after which...
         * - The client will spawn its own "client-mode" directorstate, with HUD elements enabled and with money/burst mechanics turned on.
         * - Spawning or bursting client-side instead sends a message through the command channel. The server performs the action if possible, and then sends a confirmation or denial response. If confirmed, the client-mode director reflects the changes locally.
         * - What things need to be done locally?
         */

        // ALWAYS //
        public void Awake()
        {
            RoR2Application.isModded = true;
            CommandHelper.AddToConsoleWhenReady();

            var path = System.IO.Path.GetDirectoryName(Info.Location);
            assets = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "providirectorcore"));
            hudPrefab = assets.LoadAsset<GameObject>("ProvidirectorUIRoot");
            serverDirectorPrefab = assets.LoadAsset<GameObject>("ServerDirectorPrefab");
            clientDirectorPrefab = assets.LoadAsset<GameObject>("ClientDirectorPrefab");
            icons = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "monstericons"));
            MonsterIcon.AddIconsFromBundle(icons);
            harmonyInstance = new Harmony(Info.Metadata.GUID);
            if (Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions")) SetupRiskOfOptions();
            RunHookSetup();
        }

        private void RunHookSetup()
        {

            RoR2.Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
            Run.onServerGameOver += Run_onServerGameOver;
            RoR2Application.onUpdate += RoR2Application_onUpdate;
            GlobalEventManager.onCharacterDeathGlobal += SwapTargetAfterDeath;
            Run.onRunStartGlobal += StartRun;
            Stage.onStageStartGlobal += GlobalStageStart;
            On.RoR2.Run.OnServerSceneChanged += Run_OnServerSceneChanged;
            On.RoR2.RunCameraManager.Update += RunCameraManager_Update;
            Run.onPlayerFirstCreatedServer += SetupDirectorUser;
            On.RoR2.Run.BeginGameOver += Run_BeginGameOver;
            On.RoR2.CombatDirector.Awake += CombatDirector_Awake;
            On.RoR2.CharacterSpawnCard.GetPreSpawnSetupCallback += NewPrespawnSetup;
            On.RoR2.MapZone.TeleportBody += MapZone_TeleportBody;
            On.RoR2.VoidRaidGauntletController.Start += VoidlingReady;
            On.RoR2.ScriptedCombatEncounter.BeginEncounter += SCEControlGate;
            On.RoR2.ArenaMissionController.BeginRound += FieldCardUpdate;
            On.RoR2.ArenaMissionController.EndRound += RoundEndLock;
            On.RoR2.Chat.CCSay += InterpretDirectorCommand;
            On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.PreEncounterBegin += MithrixPlayerSetup;
            On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.OnMemberAddedServer += MithrixPlayerExecute;
            On.EntityStates.Missions.BrotherEncounter.PreEncounter.OnEnter += PreEncounterReady;
            On.EntityStates.Missions.BrotherEncounter.Phase1.OnEnter += Phase1Ready;
            On.EntityStates.Missions.BrotherEncounter.Phase2.OnEnter += Phase2Ready;
            On.EntityStates.Missions.BrotherEncounter.Phase3.OnEnter += Phase3Ready;
            On.EntityStates.Missions.BrotherEncounter.EncounterFinished.OnEnter += EncounterFinish;
            On.RoR2.EscapeSequenceController.CompleteEscapeSequence += EscapeSequenceFinish;
            NetworkManagerSystem.onStartClientGlobal += LogClientMessageHandlers;
            NetworkManagerSystem.onStartServerGlobal += LogServerMessageHandlers;
            On.RoR2.Networking.NetworkManagerSystem.OnClientSceneChanged += SetupSceneChange;

#if DEBUG
            // Graciously stolen from DropInMultiplayer which is presumably taken from somewhere else
            // Instructions:
            // Step One: Assuming this line is in your codebase, start two instances of RoR2 (do this through the .exe directly)
            // Step Two: Host a game with one instance of RoR2.
            // Step Three: On the instance that isn't hosting, open up the console (ctrl + alt + tilde) and enter the command "connect localhost:7777"
            // DO NOT MAKE A MISTAKE SPELLING THE COMMAND OR YOU WILL HAVE TO RESTART THE CLIENT INSTANCE!!
            // Step Four: Test whatever you were going to test

            On.RoR2.Networking.NetworkManagerSystem.OnClientConnect += (orig, a, b) => 
            {
                if (skipClientVerification) PLog("Skipped verification for local join");
                else orig(a, b);
            };
#endif
            if (harmonyInstance != null) harmonyInstance.PatchAll(typeof(HarmonyPatches));
        }

        private IEnumerator MoveCollisionAttempt(bool collision, Vector3 position)
        {
            if (!(runIsActive && defaultMaster && haveControlAuthority)) yield break;
            while (!defaultMaster.GetBodyObject())
            {
                PLog("Unable to find director default body object. Retrying in 1s...");
                yield return new WaitForSeconds(1f);
            }
            while (Run.instance.livingPlayerCount < NetworkUser.readOnlyInstancesList.Count)
            {
                PLog("Not everyone has joined yet ({0}/{1}). Retrying in 1s...", Run.instance.livingPlayerCount, NetworkUser.readOnlyInstancesList.Count);
                yield return new WaitForSeconds(1f);
            }
            GameObject g = defaultMaster.GetBodyObject();
            g.layer = collision ? LayerIndex.playerBody.intVal : LayerIndex.noCollision.intVal;
            TeleportHelper.TeleportGameObject(g, position);
            PLog("MoveCollisionAttempt --> {0}, {1}", collision, position);
        }

        private void GlobalStageStart(Stage obj)
        {
            if (!runIsActive) return;
            string sceneName = obj.sceneDef.baseSceneName;
            if (sceneName.Equals("arena"))
            {
                Debug.Log("Void Field setup called");
                if (clientModeDirector) clientModeDirector.rateModifier = DirectorState.RateModifier.Locked;
                DirectorState.spawnCardTemplates.Clear();
            }
        }

        private void LogServerMessageHandlers()
        {
            // Additional because apparently our thing doesn't register
            NetworkServer.RegisterHandler(prvdChannel, HandleCommsServer);
            PLog("Providirector Server Message Handler registered on channel {0}", prvdChannel);

        }

        private void LogClientMessageHandlers(NetworkClient client)
        {
            client.RegisterHandler(prvdChannel, HandleCommsClient);
            PLog("Providirector Client Message Handler registered on channel {0}", prvdChannel);
        }

        private void SetupRiskOfOptions()
        {
            _modEnabled = Config.Bind<bool>("General", "Mod Enabled", true, "If checked, the mod is enabled and will be started in any multiplayer games where there are 2 or more players, and you are the host.");
            _nearestNodeSnap = Config.Bind<bool>("Director", "Snap to Terrain", true, "If checked, grounded enemies will snap to nearby terrain when spawned. Otherwise, they will spawn directly in front of the camera, like flying enemies.");
            _directorCredInit = Config.Bind<float>("Director", "Initial Credit", DirectorState.baseCreditGain, String.Format("The amount of credits gained by the player director per second. Default value is {0}.", DirectorState.baseCreditGain));
            _directorCredGain = Config.Bind<float>("Director", "Credit Gain Per Level", DirectorState.creditGainPerLevel, String.Format("The amount credit gain increases with level. Default value is {0}.", DirectorState.creditGainPerLevel));
            _directorWalletInit = Config.Bind<int>("Director", "Initial Capacity", (int)DirectorState.baseWalletSize, String.Format("The base maximum capacity of the player director wallet. Default value is {0}.", (int)DirectorState.baseWalletSize));
            _directorWalletGain = Config.Bind<int>("Director", "Capacity Gain Per Level", (int)DirectorState.walletGainPerLevel, String.Format("The amount wallet size increases with level. Default value is {0}.", (int)DirectorState.walletGainPerLevel));
            _vanillaCreditScale = Config.Bind<float>("Vanilla Config", "Vanilla Director Credit", 0.85f, "How much the vanilla directors have their credit gain scaled. Default value is 85%.");
            ModSettingsManager.AddOption(new CheckBoxOption(_modEnabled));
            ModSettingsManager.AddOption(new CheckBoxOption(_nearestNodeSnap));
            ModSettingsManager.AddOption(new SliderOption(_directorCredInit, new SliderConfig { min = 0f, max = 10f, formatString = "{0:G2}" }));
            ModSettingsManager.AddOption(new SliderOption(_directorCredGain, new SliderConfig { min = 0f, max = 3f, formatString = "{0:G2}" }));
            ModSettingsManager.AddOption(new IntSliderOption(_directorWalletInit, new IntSliderConfig { min = 0, max = 100 }));
            ModSettingsManager.AddOption(new IntSliderOption(_directorWalletGain, new IntSliderConfig { min = 0, max = 100 }));
            ModSettingsManager.AddOption(new SliderOption(_vanillaCreditScale, new SliderConfig { min = 0f, max = 1f, formatString = "{0:P0}" }));

#if DEBUG
            _debugEnabled = Config.Bind("General", "Debug Mode", false, "Whether or not debug mode is enabled. This enables the mod to run in singleplayer games and enables more controls for Director mode (targeting non-player bodies).");
            ModSettingsManager.AddOption(new CheckBoxOption(_debugEnabled));
            _skipClientVerification = Config.Bind("General", "Skip Client Verification", false, "Cancels sending authorization when joining.");
            ModSettingsManager.AddOption(new CheckBoxOption(_skipClientVerification));
#endif

        }


        private void StartRun(Run self)
        {
            runIsActive = false;
#if DEBUG
            PLog("Connected Players: {0}", NetworkUser.readOnlyInstancesList.Count);
#endif
            if (modEnabled && NetworkServer.active && (NetworkUser.readOnlyInstancesList.Count > 1 || debugEnabled))
            {
                runIsActive = true;
                foreach (NetworkUser nu in NetworkUser.readOnlyInstancesList)
                {
                    SendSingleGeneric(nu.connectionToClient, MessageType.GameStart, nu == directorUser);
                }
                PLog("Providirector has been set up for this run!");
            }
            
        }

        private void Run_BeginGameOver(On.RoR2.Run.orig_BeginGameOver orig, Run self, GameEndingDef gameEndingDef)
        {
            if (debugEnabled && runIsActive && !gameEndingDef.isWin) return;
            orig(self, gameEndingDef);
        }

        private void Run_onServerGameOver(Run run, GameEndingDef ending)
        {
            Run_onRunDestroyGlobal(run);
        }

        private void Run_onRunDestroyGlobal(Run obj)
        {
            if (activeHud) Destroy(activeHud);
            if (activeServerDirectorObject) Destroy(activeServerDirectorObject);
            if (activeClientDirectorObject) Destroy(activeClientDirectorObject);
            activeClientDirectorObject = null;
            clientModeDirector = null;
            activeServerDirectorObject = null;
            serverModeDirector = null;
            activeHud = null;
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

        private void MithrixPlayerSetup(On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.orig_PreEncounterBegin orig, EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState self)
        {
            orig(self);
            if ((self.phaseControllerChildString == "Phase2") || !runIsActive || !haveControlAuthority) return;
            scriptEncounterControlNext = true;
        }

        private void CombatDirector_Awake(On.RoR2.CombatDirector.orig_Awake orig, CombatDirector self)
        {
            if (runIsActive && haveControlAuthority)
            {
                self.creditMultiplier *= vanillaCreditScale;
            }
            orig(self);
        }

        private void RoR2Application_onUpdate()
        {
            if (!runIsActive) return;
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
            InputManager.ToggleHUD.PushState(Input.GetKey(KeyCode.Y));
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            if (directorUser && mainCamera)
            {
                pos = mainCamera.sceneCam.transform.position;
                rot = mainCamera.sceneCam.transform.rotation;
                pos += rot * new Vector3(0, 0, 5);
                
            }
            bool honorenabled = RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.EliteOnly);

            if (clientModeDirector == null) return;

            if (spectateTarget == null)
            {
                PLog("Attempting lock on...");
                ChangeNextTarget();
            }

            // Only Local Effects

            if ((localUser.eventSystem && localUser.eventSystem.isCursorVisible) || currentMaster != defaultMaster) return;

            if (InputManager.ToggleAffixRare.down) clientModeDirector.eliteTierIndex = EliteTierIndex.Tier2;
            else if (InputManager.ToggleAffixCommon.down && !honorenabled) clientModeDirector.eliteTierIndex = EliteTierIndex.Tier1;
            else if (honorenabled) clientModeDirector.eliteTierIndex = EliteTierIndex.Honor1;
            else clientModeDirector.eliteTierIndex = EliteTierIndex.Normal;

            if (InputManager.NextTarget.justPressed) ChangeNextTarget();
            if (InputManager.PrevTarget.justPressed) ChangePreviousTarget();
            if (InputManager.SwapPage.justPressed) clientModeDirector.secondPage = !clientModeDirector.secondPage;

            // Server interference required
            if (InputManager.Slot1.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(0), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot2.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(1), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot3.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(2), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot4.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(3), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot5.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(4), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot6.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(5), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.FocusTarget.justPressed) SendFocusCommand(localToServerConnection, spectateTargetMaster.GetComponent<CharacterMaster>());
            if (InputManager.BoostTarget.justPressed) SendBurstCommand(localToServerConnection);

            if (!(haveControlAuthority && directorUser)) return;

        }

        private void SetupDirectorUser(Run run, PlayerCharacterMasterController generatedPCMC)
        {
            NetworkUser user = generatedPCMC.networkUser;
            if (!directorUser) directorUser = localUser.currentNetworkUser;
            PLog("User Add Called for {0}", user.GetNetworkPlayerName().GetResolvedName());
            PLog("Current director user is {0}", directorUser ? directorUser.GetNetworkPlayerName().GetResolvedName() : "null");
            if (!user.master) { PLog(LogLevel.Warning, "No master found on the spawned player!"); return; }
            if (!modEnabled || user != directorUser || !haveControlAuthority) return;
            // At this point we know that the user being added is the player who will be the director, and that we have the authority to manage it.
            directorUser = user;
            defaultMaster = user.master;
#if !DEBUG
            defaultMaster.bodyPrefab = BodyCatalog.FindBodyPrefab("WispBody");
#else
            defaultMaster.bodyPrefab = BodyCatalog.FindBodyPrefab("LemurianBody");
#endif
            defaultMaster.godMode = true;
            
            defaultMaster.teamIndex = TeamIndex.Neutral;
            currentMaster = defaultMaster;
            currentMaster.inventory.GiveItem(RoR2Content.Items.TeleportWhenOob);
            
            currentAI = null;
            var bodysetupdel = (CharacterBody body) =>
            {
                if (!body)
                {
                    PLog(LogLevel.Warning, "No body object found!");
                    return;
                }
                body.AddBuff(RoR2Content.Buffs.Cloak);
                body.AddBuff(RoR2Content.Buffs.Intangible);
                body.AddBuff(RoR2Content.Buffs.Entangle);
                body.gameObject.layer = LayerIndex.noCollision.intVal;
                body.skillLocator.primary = null;
                body.skillLocator.secondary = null;
                body.master.inventory.GiveItem(RoR2Content.Items.Hoof, 100);
                body.master.inventory.GiveItem(RoR2Content.Items.Knurl, 100);
                body.master.inventory.GiveItem(RoR2Content.Items.Feather, 100);
                body.teamComponent.teamIndex = TeamIndex.Neutral;
                body.skillLocator.utility = null;
                body.skillLocator.special = null;
                body.master.preventGameOver = false;
                
                PLog("Setup complete!");
            };
            defaultMaster.onBodyStart += bodysetupdel;
        }

        private void Run_OnServerSceneChanged(On.RoR2.Run.orig_OnServerSceneChanged orig, Run self, string sceneName)
        {
            orig(self, sceneName);
            if (runIsActive)
            {
                if (activeServerDirectorObject == null)
                {
                    activeServerDirectorObject = Instantiate(serverDirectorPrefab);
                    serverModeDirector = activeServerDirectorObject.GetComponent<DirectorState>();
                }
                scriptEncounterControlCurrent = false;
                scriptEncounterControlNext = false;
                forceScriptEncounterAddControl = false;
                if (currentMaster != defaultMaster) DisengagePlayerControl();
                if (sceneName.Equals("voidraid"))
                {
                    forceScriptEncounterAddControl = true;
                    StartCoroutine(MoveCollisionAttempt(true, voidlingTriggerZone));
                } else if (sceneName.Equals("moon2"))
                {
                    StartCoroutine(MoveCollisionAttempt(true, moonFightTriggerZone));
                }

            }
        }

        private void RunCameraManager_Update(On.RoR2.RunCameraManager.orig_Update orig, RunCameraManager self)
        {
            if (!(runIsActive && directorIsLocal))
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
                        cameraRigController = Instantiate(LegacyResourcesAPI.Load<GameObject>("Prefabs/Main Camera")).GetComponent<CameraRigController>();
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
                    else if (networkUser == directorUser && isInPlayerControlMode)
                    {
                        cameraRigController.nextTarget = networkUserBodyObject;
                        cameraRigController.cameraMode = CameraModePlayerBasic.playerBasic;
                    } else if (directorIsLocal && !isInPlayerControlMode) {
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
                    if (reference != null)
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

        private void SetupSceneChange(On.RoR2.Networking.NetworkManagerSystem.orig_OnClientSceneChanged orig, NetworkManagerSystem self, NetworkConnection conn)
        {
            orig(self, conn);
            PLog("Client-side scene changed. Run is active: {0}", runIsActive);
            PLog("Current director is {0}. Director is local: {1}", directorUser ? directorUser.GetNetworkPlayerName().GetResolvedName() : "null", directorIsLocal);
            if (!runIsActive || !directorIsLocal) return;
            DirectorState.UpdateMonsterSelection();
            if (activeClientDirectorObject == null) {
                activeClientDirectorObject = Instantiate(clientDirectorPrefab);
                clientModeDirector = activeClientDirectorObject.GetComponent<DirectorState>();
            }
            if (activeHud == null) activeHud = Instantiate(hudPrefab);

            PLog("Attempting HUD afterinit");
            TrySetHUD();

            isInPlayerControlMode = false;
            if (RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.MonsterTeamGainsItems)) DirectorState.monsterInv = RoR2.Artifacts.MonsterTeamGainsItemsArtifactManager.monsterTeamInventory;
            else DirectorState.monsterInv = null;
            if (RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.EliteOnly) && clientModeDirector) clientModeDirector.eliteTierIndex = EliteTierIndex.Honor1;

            if (_modEnabled != null)
            {
                DirectorState.baseCreditGain = _directorCredInit.Value;
                DirectorState.creditGainPerLevel = _directorCredGain.Value;
                DirectorState.baseWalletSize = _directorWalletInit.Value;
                DirectorState.walletGainPerLevel = _directorWalletGain.Value;
            }
            DirectorState.snapToNearestNode = nearestNodeSnap;
            DirectorState.eliteTiers = CombatDirector.eliteTiers;
        }


        private void SetBaseUIVisible(bool value)
        {
            if (!mainCamera || !mainCamera.hud) return;
            Transform root = mainCamera.hud.mainContainer.transform;
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
            if (!clientModeDirector) return;
            ReadOnlyCollection<CharacterBody> readOnlyInstancesList = CharacterBody.readOnlyInstancesList;
            if (readOnlyInstancesList.Count == 0) return;
            CharacterBody characterBody = spectateTarget ? spectateTarget.GetComponent<CharacterBody>() : null;
            int num = (characterBody ? readOnlyInstancesList.IndexOf(characterBody) : 0);
            for (int i = num + 1; i < readOnlyInstancesList.Count; i++)
            {
                if ((readOnlyInstancesList[i].teamComponent && readOnlyInstancesList[i].teamComponent.teamIndex == TeamIndex.Player) || (debugEnabled && readOnlyInstancesList[i].teamComponent.teamIndex != TeamIndex.None))
                {
                    spectateTarget = readOnlyInstancesList[i].gameObject;
                    if (debugEnabled) PLog("Now spectating {0} on team {1}", readOnlyInstancesList[i].name, readOnlyInstancesList[i].teamComponent.teamIndex);
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
                    if (debugEnabled) PLog("Now spectating {0} on team {1}", readOnlyInstancesList[j].name, readOnlyInstancesList[j].teamComponent.teamIndex);
                    UpdateHUD();
                    CancelInvoke("ChangeNextTarget");
                    CancelInvoke("ChangePreviousTarget");
                    return;
                }
            }
        }

        private void ChangePreviousTarget()
        {
            if (!clientModeDirector) return;
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
                    if (debugEnabled) PLog("Now spectating {0} on team {1}", readOnlyInstancesList[i].name, readOnlyInstancesList[i].teamComponent.teamIndex);
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
                    if (debugEnabled) PLog("Now spectating {0} on team {1}", readOnlyInstancesList[j].name, readOnlyInstancesList[j].teamComponent.teamIndex);
                    UpdateHUD();
                    CancelInvoke("ChangeNextTarget");
                    CancelInvoke("ChangePreviousTarget");
                    return;
                }
            }
        }

        private void UpdateHUD()
        {
            if (spectateTarget && targetHealthbar) targetHealthbar.source = spectateTarget.GetComponent<CharacterBody>().healthComponent;
            if (spectateTarget && spectateLabel) spectateLabel.text = Util.GetBestBodyName(spectateTarget);

        }

        private void AddPlayerControl(CharacterMaster c)
        {
            if (!haveControlAuthority)
            {
                PLog("AddPlayerControl called on client. Cancelling.");
                return;
            }
            if (c == null || c == currentMaster)
            {
                PLog(LogLevel.Warning, "Attempt to switch control onto a nonexistent or already present character!");
                return;
            }
            if (!directorUser)
            {
                PLog("Attempt to call AddPlayerControl without established DU");
                return;
            }
            PLog("Attempting to take control of CharacterMaster {0}", c.name);
            if (currentMaster) DisengagePlayerControl(revertfallback: false);
            else PLog("No currently set master - we can proceed as normal.");
            currentMaster = c;
            currentAI = currentMaster.GetComponent<BaseAI>();
            currentMaster.playerCharacterMasterController = currentMaster.GetComponent<PlayerCharacterMasterController>();
            PlayerStatsComponent playerStatsComponent = currentMaster.GetComponent<PlayerStatsComponent>();
            if (!currentController)
            {
                PLog(LogLevel.Warning, "CharacterMaster {0} does not have a PCMC! Instantiating one now... though this will lead to desyncs between the client and server.", c.name);
                currentMaster.playerCharacterMasterController = c.gameObject.AddComponent<PlayerCharacterMasterController>();
            }
            if (!playerStatsComponent)
            {
                PLog(LogLevel.Warning, "CharacterMaster {0} does not have a PSC! Instantiating one now... though this will lead to desyncs between the client and server.", c.name);
                playerStatsComponent = c.gameObject.AddComponent<PlayerStatsComponent>();
            }
            GameObject oldprefab = c.bodyPrefab;
            currentController.LinkToNetworkUserServer(directorUser);
            currentController.master.bodyPrefab = oldprefab; // RESET
            currentMaster.preventGameOver = false;
            currentController.enabled = true;
            playerStatsComponent.enabled = true;
            Run.instance.userMasters[directorUser.id] = c;
            AIDisable();
            if (currentAI) currentAI.onBodyDiscovered += AIDisable;
            currentMaster.onBodyDeath.AddListener(onNewMasterDeath);
            currentMaster.onBodyStart += delegate(CharacterBody b) {
                b.master.preventGameOver = false;
            };
            SendSingleGeneric(directorUser.connectionToClient, MessageType.ModeUpdate, currentMaster == defaultMaster);
            SendSingleGeneric(directorUser.connectionToClient, MessageType.FPUpdate, forceScriptEncounterAddControl);


            PLog("{0} set as new master.", currentMaster);
            void onNewMasterDeath()
            {
                currentMaster.onBodyDeath.RemoveListener(onNewMasterDeath);
                PLog("Current Master has died, checking if we should disengage.");
                if (currentMaster.IsDeadAndOutOfLivesServer())
                {
                    DisengagePlayerControl();
                }
                else PLog("No need to disengage, we have a pending revive.");
            }
        }

        private void DisengagePlayerControl(bool revertfallback = true)
        {
            if (!NetworkServer.active)
            {
                PLog("DisengagePlayerControl called on client. Cancelling.");
                return;
            }
            PLog("Disengaging player control from {0}...", currentMaster);
            if (currentMaster)
            {
                if (currentMaster != defaultMaster)
                {
                    PLog("Non-default character, performing special remove...");
                    if (currentAI) currentAI.onBodyDiscovered -= AIDisable;
                    AIEnable();
                    currentAI = null;
                    if (currentBody && currentBody.networkIdentity) currentBody.networkIdentity.RemoveClientAuthority(directorUser.connectionToClient);
                    currentMaster.playerCharacterMasterController = null;
                }
                if (currentController) currentController.enabled = false;
                PLog("Characterbody disengaged! There are now {0} active PCMCs", PlayerCharacterMasterController.instances.Count);
                currentMaster = null;
            }
            if (revertfallback)
            {
                if (currentSquad)
                {
                    PLog("Swapping to next active master in current squad...");
                    foreach (CharacterMaster candidate in currentSquad.readOnlyMembersList)
                    {
                        if (!candidate.IsDeadAndOutOfLivesServer())
                        {
                            AddPlayerControl(candidate);
                            return;
                        }
                    }
                    PLog("No alive candidates. Proceeding.");
                }
                PLog("Reverting to default master...");
                AddPlayerControl(defaultMaster);
            }
        }

        private void AIDisable()
        {
            if (!NetworkServer.active)
            {
                PLog("AIDisable called on client. Cancelling.");
                return;
            }
            if (currentAI)
            {
                if (currentBody) currentAI.OnBodyLost(currentBody);
                currentAI.enabled = false;
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
            if (!NetworkServer.active)
            {
                PLog("AIEnable called on client. Cancelling.");
                return;
            }
            if (currentAI)
            {
                currentAI.enabled = true;
                if (currentBody) currentAI.OnBodyStart(currentBody);
                //Debug.Log("AI Enabled.");
            }
        }

        private void ToggleHUD(bool state)
        {
            if (activeHud)
            {
                activeHud.SetActive(state);
            }
        }

        private IEnumerator SetFirstPersonClient(bool state)
        {
            if (!(runIsActive && directorIsLocal)) yield break;
            PLog("SetFirstPersonClient called.");
            
            while (!directorUser.GetCurrentBody())
            {
                PLog("Could not find current body for DirectorUser. Retrying in 0.2s...");
                yield return new WaitForSeconds(0.2f);
            }
            GameObject bodyObject = directorUser.GetCurrentBody().gameObject;
            PLog("First Person change target: {0}", bodyObject.name);
            if (state)
            {
                PLog("Waiting 11 seconds...");
                yield return new WaitForSeconds(11f);
                if (fpHandle.isValid)
                {
                    bodyObject.GetComponent<CameraTargetParams>().RemoveParamsOverride(fpHandle);
                    fpHandle = default;
                }
                cpData = bodyObject.GetComponent<CameraTargetParams>().currentCameraParamsData;
                cpData.idealLocalCameraPos = frontalCamera;
                cpData.isFirstPerson = true;
                fpHandle = bodyObject.GetComponent<CameraTargetParams>().AddParamsOverride(new CameraTargetParams.CameraParamsOverrideRequest
                {
                    cameraParamsData = cpData,
                    priority = 0.5f
                });
            }
            else
            {
                if (!fpHandle.isValid) yield break;
                bodyObject.GetComponent<CameraTargetParams>().RemoveParamsOverride(fpHandle);
                fpHandle = default;
            }
        }

        // Network Communicators
        private void InterpretDirectorCommand(On.RoR2.Chat.orig_CCSay orig, ConCommandArgs args)
        {
            orig(args);
            if (!NetworkServer.active) return;
            if (args.sender == localUser.currentNetworkUser)
            {
                string text = args[0];
                string[] subargs = text.Split(null, 2);
                if (subargs.FirstOrDefault().Equals("%"))
                {
                    if (subargs.Length < 2)
                    {
                        Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                        {
                            baseToken = String.Format("Providirector -- Use '% [name]' to set the director. Default is the host.")
                        });
                        return;
                    }
                    string targetUser = subargs[1];
                    int index;
                    if (!int.TryParse(targetUser, out index))
                    {
                        index = 0;
                        foreach (NetworkUser nu in NetworkUser.instancesList)
                        {
                            if (nu.GetNetworkPlayerName().GetResolvedName() == targetUser) break;
                            index++;
                        }
                    }
                    if (index >= NetworkUser.instancesList.Count)
                    {
                        Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                        {
                            baseToken = String.Format("Providirector -- Error - player name or NU Index {0} not found", targetUser)
                        });
                        return;
                    }
                    NetworkUser targetnu = NetworkUser.instancesList[index];
                    ServerSendHandshake(targetnu.connectionToClient);
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = String.Format("Providirector -- Sent director request to {0}", targetnu.GetNetworkPlayerName().GetResolvedName())
                    });
                }
            }
        }

        public static void SendSingleGeneric(NetworkConnection connection, MessageType subtype, float value)
        {
            NetworkWriter writer = new NetworkWriter();
            writer.StartMessage(prvdChannel);
            writer.Write(new MessageSubIdentifier { returnValue = value, type = subtype});
            writer.FinishMessage();
            connection?.SendWriter(writer, 0); // Default Reliable Channel - nothing fancy
        }

        
        public static void SendSingleGeneric(NetworkConnection connection, MessageType subtype, bool value)
        {
            SendSingleGeneric(connection, subtype, value ? 1f : -1f);
        }

        public static MessageSubIdentifier ReadHeader(NetworkReader reader)
        {
            return reader.ReadMessage<MessageSubIdentifier>();
        }

        public void HandleCommsClient(NetworkMessage msg)
        {
            MessageSubIdentifier header = ReadHeader(msg.reader);
            PLog("Client Received Message {0}: {1}", header.type, header.booleanValue);
            switch (header.type)
            {
                case MessageType.Handshake:
                    HandleHandshakeClient(msg, header);
                    break;
                case MessageType.Burst:
                    HandleBurstClient(msg, header);
                    break;
                case MessageType.SpawnEnemy:
                    HandleSpawnClient(msg, header);
                    break;
                case MessageType.GameStart:
                    runIsActive = true;
                    directorUser = header.booleanValue ? localUser.currentNetworkUser : null;
                    break;
                case MessageType.ModeUpdate:
                    PLog("Mode update received: {0}", header.booleanValue);
                    ToggleHUD(header.booleanValue);
                    SetBaseUIVisible(!header.booleanValue);
                    isInPlayerControlMode = !header.booleanValue;
                    break;
                case MessageType.FPUpdate:
                    PLog("Starting FPUpdate Coroutine");
                    StartCoroutine(SetFirstPersonClient(header.booleanValue));
                    break;
                default:
                    PLog("Client: Invalid Message Received (Msg Subtype {0})", (int)header.type);
                    break;
            }
        }

        public void HandleCommsServer(NetworkMessage msg)
        {
            MessageSubIdentifier header = ReadHeader(msg.reader);
            PLog("Server Received Message {0}: {1}", header.type, header.booleanValue);
            switch (header.type)
            {
                case MessageType.Handshake:
                    HandleHandshakeServer(msg, header);
                    break;
                case MessageType.Burst:
                    HandleBurstServer(msg, header);
                    break;
                case MessageType.SpawnEnemy:
                    HandleSpawnServer(msg, header);
                    break;
                case MessageType.FocusEnemy:
                    HandleFocusServer(msg, header);
                    break;
                default:
                    PLog("Server: Invalid Message Received (Msg Subtype {0})", (int)header.type);
                    break;
            }
        }


        public void ServerSendHandshake(NetworkConnection connection)
        {
            if (!NetworkServer.active)
            {
                PLog("Can't initiate handshake from client.");
                return;
            }
            SendSingleGeneric(connection, MessageType.Handshake, true);
        }

        public void HandleHandshakeClient(NetworkMessage msg, MessageSubIdentifier sid)
        {
            PLog("RECEIVE Director Update");
            directorUser = sid.booleanValue && modEnabled ? localUser.currentNetworkUser : null;
            SendSingleGeneric(msg.conn, MessageType.Handshake, modEnabled);
        }

        public void HandleHandshakeServer(NetworkMessage msg, MessageSubIdentifier sid)
        {
            if (sid.booleanValue)
            {
                PLog("RECEIVE Director Confirmation on connection {0}", msg.conn.address);
                NetworkUser toChange = null;
                foreach (NetworkUser u in NetworkUser.instancesList)
                {
                    PLog("{0}: {1}", u.GetNetworkPlayerName().GetResolvedName(), u.connectionToClient.address);
                    if (u.connectionToClient == msg.conn)
                    {
                        PLog("Director identified as {0}", u.GetNetworkPlayerName().GetResolvedName());
                        toChange = u;
                        break;
                    }
                }
                if (toChange != null)
                {
                    if (directorUser) SendSingleGeneric(directorUser.connectionToClient, MessageType.Handshake, false);
                    directorUser = toChange;
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = String.Format("Providirector -- Director response OK, set to {0}", directorUser.GetNetworkPlayerName().GetResolvedName())
                    });
                }
                else PLog("Error: Cannot find NetworkUser associated with connection {0}", msg.conn.connectionId);
            } else
            {
                PLog("RECEIVE Director Refusal");
            }
        }

        public bool SendSpawnCommand(NetworkConnection connection, int slotIndex, EliteTierIndex eliteClassIndex, Vector3 position, Quaternion rotation)
        {
            var result = clientModeDirector.TrySpawn(slotIndex, position, rotation, eliteIndexOverride: eliteClassIndex);
            if (result.Item1 < 0)
            {
                clientModeDirector.DoPurchaseTrigger(-1);
                return false;
            }
            NetworkWriter writer = new NetworkWriter();
            writer.StartMessage(prvdChannel);
            writer.Write(new MessageSubIdentifier { type = MessageType.SpawnEnemy });
            writer.Write(new SpawnEnemy()
            {
                slotIndex = slotIndex,
                eliteClassIndex = eliteClassIndex,
                position = position,
                rotation = rotation
            });
            writer.FinishMessage();
            connection?.SendWriter(writer, prvdChannel);
            return true;
        }

        public void HandleSpawnClient(NetworkMessage msg, MessageSubIdentifier sid) // Receives boolean response
        {
            var k = msg.reader.ReadMessage<SpawnConfirm>();
            clientModeDirector.DoPurchaseTrigger(k.cost, k.spawned);
        }

        public void HandleSpawnServer(NetworkMessage msg, MessageSubIdentifier sid)
        {
            SpawnEnemy request = msg.reader.ReadMessage<SpawnEnemy>();
            if (request != null)
            {
                NetworkWriter writer = new NetworkWriter();
                writer.StartMessage(prvdChannel);
                writer.Write(new MessageSubIdentifier { type = MessageType.SpawnEnemy });
                try
                {
                    var result = serverModeDirector.TrySpawn(request.slotIndex, request.position, request.rotation, eliteIndexOverride: request.eliteClassIndex);

                    writer.Write(new SpawnConfirm()
                    {
                        cost = result.Item1,
                        spawned = result.Item2
                    });
                }
                catch
                {
                    writer.Write(new SpawnConfirm()
                    {
                        cost = -1f,
                        spawned = null
                    });
                }
                writer.FinishMessage();
                msg.conn?.SendWriter(writer, prvdChannel);
            }
        }

        public void SendBurstCommand(NetworkConnection connection)
        {
            if (!clientModeDirector.ApplyFrenzy())
            {
                clientModeDirector.DoBurstTrigger(false);
                return;
            }
            SendSingleGeneric(connection, MessageType.Burst, true);
        }

        public void HandleBurstClient(NetworkMessage msg, MessageSubIdentifier sid) // Receives boolean response
        {
            clientModeDirector.DoBurstTrigger(sid.booleanValue);
        }

        public void HandleBurstServer(NetworkMessage msg, MessageSubIdentifier sid)
        {
            SendSingleGeneric(msg.conn, MessageType.Burst, serverModeDirector.ApplyFrenzy());
        }

        public void SendFocusCommand(NetworkConnection connection, CharacterMaster target)
        {
            PLog("Send Focus Command! {0}", target);
            if (target == null) return;
            NetworkWriter writer = new NetworkWriter();
            writer.StartMessage(prvdChannel);
            writer.Write(new MessageSubIdentifier { type = MessageType.FocusEnemy });
            writer.Write(new FocusEnemy()
            {
                target = target
            });
            writer.FinishMessage();
            connection?.SendWriter(writer, prvdChannel);
        }

        public void HandleFocusServer(NetworkMessage msg, MessageSubIdentifier sid)
        {
            FocusEnemy focusEnemy = msg.reader.ReadMessage<FocusEnemy>();
            if (focusEnemy.target)
            {
                foreach (TeamComponent tc in TeamComponent.GetTeamMembers(TeamIndex.Monster))
                {
                    CharacterMaster c = tc.body.master;
                    if (focusEnemy.target == c) continue;
                    foreach (BaseAI ai in c.aiComponents)
                    {
                        ai.currentEnemy.gameObject = focusEnemy.target.bodyInstanceObject;
                        ai.enemyAttentionDuration = 10f;
                    }
                }
            } else
            {
                PLog("Received invalid focus target. Cancelling.");
            }
        }

        public bool VerifyDirectorSource(NetworkConnection conn)
        {
            return conn == directorUser?.connectionToClient;
        }

        // Utility Functions

        public static void PLog(LogLevel level, string message, params object[] fmt)
        {
            if (instance) instance.Logger.Log(level, String.Format(message, fmt));
        }
        public static void PLog(LogLevel level, string message)
        {
            if (instance) instance.Logger.Log(level, message);
        }
        public static void PLog(string message, params object[] fmt)
        {
            if (instance) instance.Logger.Log(LogLevel.Info, String.Format(message, fmt));
        }
        public static void PLog(string message)
        {
            if (instance) instance.Logger.Log(LogLevel.Info, message);
        }

        public void TrySetHUD()
        {
            if (!activeHud)
            {
                PLog("No HUD to set params for, cancelling.");
                return;
            }
            bool flag = true;
            if (mainCamera && mainCamera.uiCam) activeHud.GetComponent<Canvas>().worldCamera = mainCamera.uiCam;
            else
            {
                flag = false;
                PLog("Failed to acquire main camera");
            }
            ProvidirectorHUD mainHud = activeHud.GetComponent<ProvidirectorHUD>();
            if (mainHud)
            {
                mainHud.directorState = clientModeDirector;
                targetHealthbar = mainHud.spectateHealthBar;
                spectateLabel = mainHud.spectateNameText;
            }
            else
            {
                flag = false;
                PLog("Failed to setup main HUD components");
            }
            if (!flag)
            {
                PLog("HUD Setup incomplete, retrying in 0.2s");
                Invoke("TrySetHUD", 0.2f);
            }
            else
            {
                ToggleHUD(true);
                SetBaseUIVisible(false);
                SetFirstPersonClient(false);
                PLog("HUD setup complete");
            }
        }

        // Game Event Triggers

        private Action<CharacterMaster> NewPrespawnSetup(On.RoR2.CharacterSpawnCard.orig_GetPreSpawnSetupCallback orig, CharacterSpawnCard self)
        {
            return (CharacterMaster c) =>
            {
                PlayerCharacterMasterController cmc = c.GetComponent<PlayerCharacterMasterController>();
                PlayerStatsComponent psc = c.GetComponent<PlayerStatsComponent>();
                if (scriptEncounterControlCurrent || forceScriptEncounterAddControl)
                {
                    if (!cmc) cmc = c.gameObject.AddComponent<PlayerCharacterMasterController>();
                    cmc.enabled = false;
                    if (!psc) c.gameObject.AddComponent<PlayerStatsComponent>();
                    PLog("Added player controls to {0}", c.name);
                }
                orig(self)?.Invoke(c);
            };
        }

        private void MithrixPlayerExecute(On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.orig_OnMemberAddedServer orig, EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState self, CharacterMaster master)
        {
            orig(self, master);
            if ((self.phaseControllerChildString == "Phase2") || !runIsActive || (defaultMaster != currentMaster)) return;
            AddPlayerControl(master);
        }

        private void EscapeSequenceFinish(On.RoR2.EscapeSequenceController.orig_CompleteEscapeSequence orig, EscapeSequenceController self)
        {
            orig(self);
            if (!clientModeDirector) return;
            clientModeDirector.rateModifier = DirectorState.RateModifier.Locked;
        }

        private void EncounterFinish(On.EntityStates.Missions.BrotherEncounter.EncounterFinished.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.EncounterFinished self)
        {
            orig(self);
            if (clientModeDirector) clientModeDirector.rateModifier = DirectorState.RateModifier.TeleporterBoosted;
            StartCoroutine(MoveCollisionAttempt(true, rescueShipTriggerZone));
        }

        private void Phase3Ready(On.EntityStates.Missions.BrotherEncounter.Phase3.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.Phase3 self)
        {
            orig(self);
            if (clientModeDirector) clientModeDirector.rateModifier = DirectorState.RateModifier.Locked;
        }

        private void Phase2Ready(On.EntityStates.Missions.BrotherEncounter.Phase2.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.Phase2 self)
        {
            orig(self);
            if (clientModeDirector) clientModeDirector.rateModifier = DirectorState.RateModifier.TeleporterBoosted;
        }

        private void Phase1Ready(On.EntityStates.Missions.BrotherEncounter.Phase1.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.Phase1 self)
        {
            orig(self);
            if (clientModeDirector) clientModeDirector.rateModifier = DirectorState.RateModifier.Locked;
            if (haveControlAuthority) StartCoroutine(MoveCollisionAttempt(false, Vector3.zero));
        }

        private void SwapTargetAfterDeath(DamageReport obj)
        {
            if (!runIsActive) return;
            if (obj.victimMaster.GetBodyObject() == spectateTarget && clientModeDirector)
            {
                PLog("Current spectator target died, waiting to swap to the next target.");
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
            clientModeDirector.isDirty = true;
            clientModeDirector.rateModifier = DirectorState.RateModifier.None;
        }

        private void RoundEndLock(On.RoR2.ArenaMissionController.orig_EndRound orig, ArenaMissionController self)
        {
            orig(self);
            if (clientModeDirector) clientModeDirector.rateModifier = DirectorState.RateModifier.Locked;
        }

        private void SCEControlGate(On.RoR2.ScriptedCombatEncounter.orig_BeginEncounter orig, ScriptedCombatEncounter self)
        {
            if (!runIsActive) { orig(self); return; }
            if (scriptEncounterControlNext)
            {
                scriptEncounterControlNext = false;
                scriptEncounterControlCurrent = true;
                currentSquad = self.combatSquad;
                self.onBeginEncounter += delegate (ScriptedCombatEncounter _) {
                    scriptEncounterControlCurrent = false;
                };
                currentSquad.onDefeatedServer += delegate ()
                {
                    PLog("Combat squad defeated, reverting to null");
                    currentSquad = null;
                };
            }
            orig(self);
        }

        private void VoidlingReady(On.RoR2.VoidRaidGauntletController.orig_Start orig, VoidRaidGauntletController self)
        {
            orig(self);
            if (!runIsActive) return;
            if (clientModeDirector) clientModeDirector.rateModifier = DirectorState.RateModifier.Locked;
            PLog("VoidRaidGauntletController start, attempting control addition");
            if (haveControlAuthority)
            {
                StartCoroutine(MoveCollisionAttempt(true, voidlingTriggerZone));
                foreach (ScriptedCombatEncounter encounter in self.phaseEncounters)
                {
                    encounter.combatSquad.onMemberAddedServer += (CharacterMaster c) => {
                        AddPlayerControl(c);
                        StartCoroutine(MoveCollisionAttempt(false, Vector3.zero));
                    };
                }
            }
            PLog("VoidRaidGauntletController finished additions");
        }

        private void PreEncounterReady(On.EntityStates.Missions.BrotherEncounter.PreEncounter.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.PreEncounter self)
        {
            orig(self);
            if (runIsActive && defaultMaster && defaultMaster.GetBodyObject()) defaultMaster.GetBodyObject().layer = LayerIndex.noCollision.intVal;
            else PLog(LogLevel.Error, "Unable to find the default master for the director!");
        }

        private void MapZone_TeleportBody(On.RoR2.MapZone.orig_TeleportBody orig, MapZone self, CharacterBody characterBody)
        {
            // Special exception
            if (defaultMaster && characterBody == defaultMaster.GetBody())
            {
                PLog(LogLevel.Warning, "In-zone TP cancelled for the director.");
                return;
            }
            orig(self, characterBody);
        }
    }
}

