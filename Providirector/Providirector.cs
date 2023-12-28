using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RoR2;
using RoR2.CameraModes;
using RoR2.CharacterAI;
using RoR2.UI;
using RoR2.Networking;
using R2API.Utils;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine.Networking;
using ProvidirectorGame;
using Providirector.NetworkCommands;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using BepInEx.Logging;

#pragma warning disable Publicizer001

namespace Providirector
{
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync)]
    [BepInDependency("com.bepis.r2api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.DacityP.Providirector", "Providirector", "1.0.2")]
    public class Providirector : BaseUnityPlugin
    {
        #region Variables
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
        private static ConfigEntry<int> _directorSpawnCap;

        // General
        public static bool runIsActive = false;
        private SyncDirector _currentDirectorConfig;
        public SyncDirector currentDirectorConfig
        {
            get { return _currentDirectorConfig; }
            set
            {
                _currentDirectorConfig = value;
                if (_currentDirectorConfig != null)
                {
                    if (!_currentDirectorConfig.serverData) DirectorState.snapToNearestNode = _currentDirectorConfig.snapToNearestNode;
                    else
                    {
                        DirectorState.baseCreditGain = _currentDirectorConfig.creditInit;
                        DirectorState.creditGainPerLevel = _currentDirectorConfig.creditGain;
                        DirectorState.baseWalletSize = _currentDirectorConfig.walletInit;
                        DirectorState.walletGainPerLevel = _currentDirectorConfig.walletGain;
                        DirectorState.directorSelfSpawnCap = _currentDirectorConfig.spawnCap;
                    }
                }
            }
        }

        public DebugInfoPanel panel;

        // Server mode director things
        private static Harmony harmonyInstance;
        private static LocalUser localUser => LocalUserManager.readOnlyLocalUsersList[0];
        private CharacterMaster currentMaster;
        private CharacterMaster defaultMaster;
        private ServerState serverModeDirector;
        private ClientState clientModeDirector;
        private CombatSquad currentSquad;
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
        private GameObject focusTargetPersistent;
        private float relockTargetServerTimer;
        private bool forceScriptEncounterAddControl = false;
        private bool currentlyAddingControl = false;
        private bool firstControllerAlreadySet = false;
        private bool enableFirstPerson = false;

        // Client mode director things
        private CameraRigController mainCamera => directorUser?.cameraRigController;
        private GameObject activeHud;
        private ProvidirectorHUD hud;
        private GameObject activeClientDirectorObject;
        private HealthBar targetHealthbar;
        private GameObject spectateTarget;
        private GameObject spectateTargetMaster => spectateTarget.GetComponent<CharacterBody>().masterObject;
        private CharacterMaster clientDefaultMaster;
        private float newCharacterMasterSpawnGrace = 0f;

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
                // PLog("Director User set locally to {0}", _directorUser?.GetNetworkPlayerName().GetResolvedName());
            }
        }
        private static bool directorIsLocal => runIsActive && directorUser == localUser.currentNetworkUser;
        private static bool haveControlAuthority => NetworkServer.active;

        // Other locally defined constants
        private const short prvdChannel = 12345;
        private readonly Vector3 rescueShipTriggerZone = new Vector3(303f, -120f, 393f);
        private readonly Vector3 moonFightTriggerZone = new Vector3(-47.0f, 524.0f, -23.0f);
        private readonly Vector3 voidlingTriggerZone = new Vector3(-81.0f, 50.0f, 82.0f);
        private readonly Vector3 defaultZone = new Vector3(0, -99999f, 0); // Lmfao
        private const float gracePeriodLength = 5f;

        private NetworkConnection serverDirectorConnection => directorUser.connectionToClient;
        private NetworkConnection localToServerConnection => localUser.currentNetworkUser.connectionToServer;

        #endregion Variables

        #region Initialisation Methods
        public void Awake()
        {
            RoR2Application.isModded = true;
            var path = System.IO.Path.GetDirectoryName(Info.Location);
            ProvidirectorResources.Load(path);
            harmonyInstance = new Harmony(Info.Metadata.GUID);
            if (Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions")) SetupRiskOfOptions();
            RunHookSetup();
        }

        void OnEnable()
        {
            instance = this;
        }

        void OnDisable()
        {
            instance = null;
        }

        private void RunHookSetup()
        {
            RoR2.Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
            RoR2Application.onUpdate += RoR2Application_onUpdate;
            Stage.onStageStartGlobal += GlobalStageStart;
            On.RoR2.Run.Start += StartRun;
            On.RoR2.Run.OnServerSceneChanged += Run_OnServerSceneChanged;
            On.RoR2.RunCameraManager.Update += RunCameraManager_Update;
            Run.onPlayerFirstCreatedServer += SetupDirectorUser;
            On.RoR2.Run.BeginGameOver += Run_BeginGameOver;
            On.RoR2.CombatDirector.Awake += CombatDirector_Awake;
            On.RoR2.MapZone.TryZoneStart += TryInterceptMasterTP;
            On.RoR2.GlobalEventManager.OnPlayerCharacterDeath += PreventDeathCallsIfDirector;
            On.RoR2.VoidRaidGauntletController.Start += VoidlingReady;
            On.RoR2.VoidRaidGauntletController.OnBeginEncounter += SafeBeginEncounter;
            On.RoR2.ScriptedCombatEncounter.BeginEncounter += SCEControlGate;
            On.RoR2.ArenaMissionController.BeginRound += FieldCardUpdate;
            On.RoR2.ArenaMissionController.EndRound += RoundEndLock;
            On.RoR2.Chat.CCSay += InterpretDirectorCommand;
            On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.OnEnter += BeginMoonPhase;
            On.EntityStates.Missions.BrotherEncounter.EncounterFinished.OnEnter += EncounterFinish;
            On.RoR2.HoldoutZoneController.Start += MoveToEscapeZone;
            On.RoR2.LocalUser.RebuildControlChain += DirectorControlChainMod;
            NetworkManagerSystem.onStartClientGlobal += LogClientMessageHandlers;
            NetworkManagerSystem.onStartServerGlobal += LogServerMessageHandlers;
            On.RoR2.Networking.NetworkManagerSystem.OnClientSceneChanged += SetupSceneChange;
            On.RoR2.CharacterMaster.SpawnBody += SpawnBodyClientForced;
            On.EntityStates.VoidRaidCrab.EscapeDeath.FixedUpdate += EscapeDeath_FixedUpdate;
            On.EntityStates.VoidRaidCrab.BaseSpinBeamAttackState.OnEnter += SetVelocityZeroSpin;
            On.EntityStates.VoidRaidCrab.BaseVacuumAttackState.OnEnter += SetVelocityZeroVacuum;
            On.EntityStates.VoidRaidCrab.EscapeDeath.OnExit += SendStartNextDonutMessage;
            On.RoR2.Artifacts.DoppelgangerInvasionManager.CreateDoppelganger += DisableDoppelgangerControl;
            
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
            On.RoR2.CameraRigController.Update += CameraRigController_Update;
#endif
            if (harmonyInstance != null) harmonyInstance.PatchAll(typeof(HarmonyPatches));
        }

        private void SetupRiskOfOptions()
        {
            _modEnabled = Config.Bind<bool>("General", "Mod Enabled", true, "If checked, the mod is enabled and will be started in any multiplayer games where there are 2 or more players, and you are the host.");
            _nearestNodeSnap = Config.Bind<bool>("Director", "Snap to Terrain", true, "If checked, grounded enemies will snap to nearby terrain when spawned. Otherwise, they will spawn directly in front of the camera, like flying enemies.");
            _directorCredInit = Config.Bind<float>("Director", "Initial Credit", DirectorState.baseCreditGain, String.Format("The amount of credits gained by the player director per second. Default value is {0}.", DirectorState.baseCreditGain));
            _directorCredGain = Config.Bind<float>("Director", "Credit Gain Per Level", DirectorState.creditGainPerLevel, String.Format("The amount credit gain increases with level. Default value is {0}.", DirectorState.creditGainPerLevel));
            _directorWalletInit = Config.Bind<int>("Director", "Initial Capacity", (int)DirectorState.baseWalletSize, String.Format("The base maximum capacity of the player director wallet. Default value is {0}.", (int)DirectorState.baseWalletSize));
            _directorWalletGain = Config.Bind<int>("Director", "Capacity Gain Per Level", (int)DirectorState.walletGainPerLevel, String.Format("The amount wallet size increases with level. Default value is {0}.", (int)DirectorState.walletGainPerLevel));
            _vanillaCreditScale = Config.Bind<float>("Vanilla Config", "Vanilla Director Credit", 0.4f, "How strong the vanilla directors are. Default value is 40%.");
            _directorSpawnCap = Config.Bind<int>("Director", "Spawn Cap", DirectorState.directorSelfSpawnCap, string.Format("The maximum amount of characters spawnable by the director at any given time. Default value is {0}", DirectorState.directorSelfSpawnCap));

            ModSettingsManager.AddOption(new CheckBoxOption(_modEnabled));
            ModSettingsManager.AddOption(new CheckBoxOption(_nearestNodeSnap));
            ModSettingsManager.AddOption(new IntSliderOption(_directorSpawnCap, new IntSliderConfig { min = 20, max = 80 }));
            ModSettingsManager.AddOption(new SliderOption(_directorCredInit, new SliderConfig { min = 0f, max = 5f, formatString = "{0:G2}" }));
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
        #endregion

        #region General Server-side Methods
        private CharacterBody SpawnBodyClientForced(On.RoR2.CharacterMaster.orig_SpawnBody orig, CharacterMaster self, Vector3 position, Quaternion rotation)
        {
            if (!runIsActive) return orig(self, position, rotation);
            if (!NetworkServer.active)
            {
                Debug.LogWarning("[Server] function 'RoR2.CharacterBody RoR2.CharacterMaster::SpawnBody(UnityEngine.Vector3,UnityEngine.Quaternion)' called on client");
                return null;
            }
            if ((bool)self.bodyInstanceObject)
            {
                Debug.LogError("Character cannot have more than one body at this time.");
                return null;
            }
            if (!self.bodyPrefab)
            {
                Debug.LogErrorFormat("Attempted to spawn body of character master {0} with no body prefab.", self.gameObject);
            }
            if (!self.bodyPrefab.GetComponent<CharacterBody>())
            {
                Debug.LogErrorFormat("Attempted to spawn body of character master {0} with a body prefab that has no {1} component attached.", self.gameObject, typeof(CharacterBody).Name);
            }
            bool flag = self.bodyPrefab.GetComponent<CharacterDirection>();
            GameObject gameObject = Instantiate(self.bodyPrefab, position, flag ? Quaternion.identity : rotation);
            CharacterBody component = gameObject.GetComponent<CharacterBody>();
            component.masterObject = self.gameObject;
            component.teamComponent.teamIndex = self.teamIndex;
            component.SetLoadoutServer(self.loadout);
            if (flag)
            {
                CharacterDirection component2 = gameObject.GetComponent<CharacterDirection>();
                float y = rotation.eulerAngles.y;
                component2.yaw = y;
            }
            NetworkConnection clientAuthorityOwner = self.GetComponent<NetworkIdentity>().clientAuthorityOwner;
            // We want to force a spawn if we're currently in a combat encounter
            bool prefabHasLPA = component.GetComponent<NetworkIdentity>().localPlayerAuthority;
            if (currentlyAddingControl)
            {
                PLog("Current combat encounter is forced to be client-controlled, adding client control to body {0}", component);
                clientAuthorityOwner = firstControllerAlreadySet ? NetworkServer.connections[0] : directorUser.connectionToClient;
            }
            if (clientAuthorityOwner != null && prefabHasLPA)
            {
                // PLog("Spawning {0} with client authority", component);
                clientAuthorityOwner.isReady = true;
                NetworkServer.SpawnWithClientAuthority(gameObject, clientAuthorityOwner);
                self.inventory.GiveItem(RoR2Content.Items.TeleportWhenOob);
            }
            else
            {
                // PLog("Spawning {0} without client authority, either because no client is set or because the prefab does not have client authority.", component);
                NetworkServer.Spawn(gameObject);
            }
            self.bodyInstanceObject = gameObject;
            if (!firstControllerAlreadySet && currentlyAddingControl)
            {
                component.GetComponent<NetworkIdentity>().localPlayerAuthority = true;
                AddPlayerControl(self);
                firstControllerAlreadySet = true;
            }
            Run.instance.OnServerCharacterBodySpawned(component);
            return component;
        }

        private void StartRun(On.RoR2.Run.orig_Start orig, Run self)
        {
            runIsActive = modEnabled && (NetworkUser.readOnlyInstancesList.Count > 1 || debugEnabled);
            orig(self);
            if (runIsActive)
            {
                runIsActive = true;
                if (_modEnabled != null)
                {
                    DirectorState.baseCreditGain = _directorCredInit.Value;
                    DirectorState.creditGainPerLevel = _directorCredGain.Value;
                    DirectorState.baseWalletSize = _directorWalletInit.Value;
                    DirectorState.walletGainPerLevel = _directorWalletGain.Value;
                    DirectorState.directorSelfSpawnCap = _directorSpawnCap.Value;
                }
                PLog(@"Director User is currently set to {0}", directorUser.GetNetworkPlayerName().GetResolvedName());
                foreach (NetworkUser nu in NetworkUser.readOnlyInstancesList)
                {
                    SendNetMessageSingle(nu.connectionToClient, MessageType.GameStart, 1, new GameStart()
                    {
                        gameobject = directorUser.gameObject
                    });
                    if (nu == directorUser) SendNetMessageSingle(nu.connectionToClient, MessageType.DirectorSync, 1, new SyncDirector(true));
                }
                PLog("Providirector has been set up for this run!");
            }
        }

        private void PreventDeathCallsIfDirector(On.RoR2.GlobalEventManager.orig_OnPlayerCharacterDeath orig, GlobalEventManager self, DamageReport damageReport, NetworkUser victimNetworkUser)
        {
            if (!runIsActive || victimNetworkUser == directorUser) return;
            orig(self, damageReport, victimNetworkUser); // Stop death messages and other hooked things like Refightilization
        }

        private void SetupDirectorUser(Run run, PlayerCharacterMasterController generatedPCMC)
        {
            NetworkUser user = generatedPCMC.networkUser;
            if (!(modEnabled && (NetworkUser.readOnlyInstancesList.Count > 1 || debugEnabled))) return;
            if (!directorUser) directorUser = localUser.currentNetworkUser;
            if (!user.master) { PLog(LogLevel.Warning, "No master found on the spawned player!"); return; }
            if (!modEnabled || user != directorUser || !haveControlAuthority) return;
            // At this point we know that the user being added is the player who will be the director, and that we have the authority to manage it.
            directorUser = user;
            defaultMaster = user.master;
            defaultMaster.bodyPrefab = BodyCatalog.FindBodyPrefab("WispBody");
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
                body.teamComponent.teamIndex = TeamIndex.Neutral;
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
                    activeServerDirectorObject = Instantiate(ProvidirectorResources.serverDirectorPrefab);
                    serverModeDirector = activeServerDirectorObject.GetComponent<ServerState>();
                    Invoke("PostSceneChangeServer", 0.1f);
                }
                forceScriptEncounterAddControl = sceneName.Equals("voidraid") || sceneName.Equals("moon2");
                enableFirstPerson = sceneName.Equals("voidraid");
                currentlyAddingControl = false;
                focusTargetPersistent = null;
                if (currentMaster != defaultMaster) DisengagePlayerControl();
            }
        }

        private void PostSceneChangeServer()
        {
            PLog("Added {0} monstercards server-side.", serverModeDirector.UpdateMonsterSelection());
        }

        private void CombatDirector_Awake(On.RoR2.CombatDirector.orig_Awake orig, CombatDirector self)
        {
            if (runIsActive && haveControlAuthority)
            {
                self.creditMultiplier *= vanillaCreditScale;
            }
            orig(self);
        }

        private void AddPlayerControl(CharacterMaster target)
        {
            if (!haveControlAuthority)
            {
                PLog("AddPlayerControl called on client. Cancelling.");
                return;
            }
            if (target == null || target == currentMaster)
            {
                PLog(LogLevel.Warning, "Attempt to switch control onto a nonexistent or already present character!");
                return;
            }
            if (!directorUser)
            {
                PLog("Attempt to call AddPlayerControl without established DU");
                return;
            }
            if (!defaultMaster)
            {
                PLog(LogLevel.Error, "Can't add player control without an established default master to fall back on.");
                return;
            }
            // Void infestor moment
            PLog("Attempting to take control of CharacterMaster {0}", target.name);
            if (currentMaster) DisengagePlayerControl(revertfallback: false);
            else PLog("No currently set master - we can proceed as normal.");
            currentMaster = target;
            target.playerCharacterMasterController = defaultMaster.playerCharacterMasterController;
            target.playerStatsComponent = defaultMaster.playerStatsComponent;
            currentAI = currentMaster.GetComponent<BaseAI>();
            target.preventGameOver = false;
            Run.instance.userMasters[directorUser.id] = target;
            AIDisable();
            if (currentAI) currentAI.onBodyDiscovered += AIDisable;
            target.onBodyDeath.AddListener(onNewMasterDeath);
            GameObject bodyobj = target.GetBodyObject();
            directorUser.masterObject = target.gameObject;
            directorUser.connectionToClient.isReady = true;
            target.networkIdentity.localPlayerAuthority = true;
            target.networkIdentity.AssignClientAuthority(directorUser.connectionToClient);
            newCharacterMasterSpawnGrace = gracePeriodLength;
            SendSingleGeneric(directorUser.connectionToClient, MessageType.ModeUpdate, currentMaster == defaultMaster);
            SendNetMessageSingle(directorUser.connectionToClient, MessageType.NotifyNewMaster, 1, new NotifyNewMaster { target = target });
            SendSingleGeneric(directorUser.connectionToClient, MessageType.FPUpdate, enableFirstPerson);
            PLog("{0} set as new master.", currentMaster);
            void onNewMasterDeath()
            {
                PLog("Current Master has died, checking if we should disengage.");
                if (currentMaster.IsDeadAndOutOfLivesServer())
                {
                    currentMaster.onBodyDeath.RemoveListener(onNewMasterDeath);
                    DisengagePlayerControl();
                }
                else PLog("No need to disengage, we have a pending revive.");
            }
        }

        private IEnumerator SetBodyAuthorityPersistent(GameObject bodyobj)
        {
            if (!(bodyobj && bodyobj.GetComponent<CharacterBody>()))
            {
                PLog("Body is undefined. Stopping authority set.");
                yield break;
            }
            if (!directorUser)
            {
                PLog("No currently set network user. Stopping authority set.");
                yield break;
            }
            PLog("Preparing to set body authority...");
            NetworkIdentity nid = bodyobj.GetComponent<NetworkIdentity>();
            directorUser.connectionToClient.isReady = true;
            nid.localPlayerAuthority = true;
            bodyobj.GetComponent<CharacterBody>().master.preventGameOver = false;
            if (nid.clientAuthorityOwner != null && nid.clientAuthorityOwner != directorUser.connectionToClient)
            {
                PLog("Removing current authority from {0}", nid.clientAuthorityOwner);
                nid.RemoveClientAuthority(nid.clientAuthorityOwner);
            }
            while (!nid.AssignClientAuthority(directorUser.connectionToClient))
            {
                PLog("Failed to assign authority to {0}. Retrying in 1 second...", directorUser.GetNetworkPlayerName().GetResolvedName());
                yield return new WaitForSeconds(1f);
            }
            bodyobj.GetComponent<CharacterBody>().master.preventGameOver = false;
            PLog("{0} authority given to {1}", bodyobj, nid.clientAuthorityOwner);

        }

        private void DisengagePlayerControl(bool revertfallback = true)
        {
            if (!NetworkServer.active)
            {
                PLog("DisengagePlayerControl called on client. Cancelling.");
                return;
            }
            PLog("Reverting main player control from {0}...", currentMaster);
            currentMaster = null;
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

        private void SCEControlGate(On.RoR2.ScriptedCombatEncounter.orig_BeginEncounter orig, ScriptedCombatEncounter self)
        {
            if (!runIsActive) { orig(self); return; }
            if (forceScriptEncounterAddControl)
            {
                // PLog("We are going to take control of {0}", self);
                currentlyAddingControl = true;
                firstControllerAlreadySet = false;
                currentSquad = self.combatSquad;
                currentSquad.onDefeatedServer += delegate ()
                {
                    // PLog("Combat squad defeated, reverting to null");
                    currentSquad = null;
                };
            }
            orig(self);
            currentlyAddingControl = false;
        }
        #endregion

        #region General Client-side Methods
        private void DirectorControlChainMod(On.RoR2.LocalUser.orig_RebuildControlChain orig, LocalUser self)
        {
            if (!(runIsActive && directorIsLocal)) { orig(self); return; }
            self.cachedMasterObject = null;
            self.cachedMasterObject = self.currentNetworkUser?.masterObject;
            self.cachedMaster = self.cachedMasterObject?.GetComponent<CharacterMaster>();
            self.cachedMasterController = self.cachedMaster?.playerCharacterMasterController;
            self.cachedStatsComponent = self.cachedMaster?.playerStatsComponent;
            self.cachedBody = self.cachedMaster?.GetBody();
            self.cachedBodyObject = self.cachedBody?.gameObject;
        }

        private void CameraRigController_Update(On.RoR2.CameraRigController.orig_Update orig, CameraRigController self)
        {
            orig(self);
            if (panel && panel.isActiveAndEnabled && self.cameraModeContext.viewerInfo.localUser == localUser)
            {
                string toset = string.Format("CameraRigController (Viewer {0}) ==============\n", self.cameraModeContext.viewerInfo.localUser?.currentNetworkUser?.GetNetworkPlayerName().GetResolvedName());
                toset += string.Format("Mode {0}\n", self.cameraMode);
                if (self.cameraModeContext.targetInfo.master) toset += string.Format("Target Master: {0} <{1}>\nLinked NU: {2}\n",
                    self.cameraModeContext.targetInfo.master,
                    self.cameraModeContext.targetInfo.master.hasEffectiveAuthority,
                    self.cameraModeContext.targetInfo.networkUser?.GetNetworkPlayerName().GetResolvedName());
                if (self.cameraModeContext.targetInfo.networkedViewAngles)
                    toset += string.Format("P {0} Y {1} <{2}>\n",
                        self.cameraModeContext.targetInfo.networkedViewAngles?.viewAngles.pitch, self.cameraModeContext.targetInfo.networkedViewAngles?.viewAngles.yaw, self.cameraModeContext.targetInfo.networkedViewAngles.hasEffectiveAuthority);
                if (self.cameraModeContext.targetInfo.body)
                {
                    toset += string.Format(@"Target Body Data ---
State - {0}
Pos - {1}
",
                        self.cameraModeContext.targetInfo.body.GetComponent<EntityStateMachine>()?.state != null ? self.cameraModeContext.targetInfo.body.GetComponent<EntityStateMachine>()?.state : "Unknown",
                        self.cameraModeContext.targetInfo.body.transform.position
                );
                }
                if (newCharacterMasterSpawnGrace > 0) toset += string.Format("[[ GRACE {0}s ]]\n", newCharacterMasterSpawnGrace);
                if (self.cameraModeContext.viewerInfo.localUser != null)
                    toset += string.Format(@"Viewer Local User Data ---
MasterObj - {0}
PCMC - {1} <{2}> linked to {3}
Master -  {4} <{5}>
Body - {6} <{7}>",
                        self.cameraModeContext.viewerInfo.localUser?.cachedMasterObject,
                        self.cameraModeContext.viewerInfo.localUser?.cachedMasterController,
                        self.cameraModeContext.viewerInfo.localUser?.cachedMasterController?.hasEffectiveAuthority,
                        self.cameraModeContext.viewerInfo.localUser?.cachedMasterController?.master,
                        self.cameraModeContext.viewerInfo.localUser?.cachedMaster,
                        self.cameraModeContext.viewerInfo.localUser?.cachedMaster?.hasEffectiveAuthority,
                        self.cameraModeContext.viewerInfo.localUser?.cachedBody,
                        self.cameraModeContext.viewerInfo.localUser?.cachedBody?.hasEffectiveAuthority);
                toset += "\nAdditional Debug Data ---\n";
                if (PhaseCounter.instance != null) toset += string.Format(@"Phase {0}", PhaseCounter.instance.phase);
                else toset += "Phase N/A";
                panel.SetDebugInfo(toset);
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
                    else if (networkUserBodyObject && networkUser.master != clientDefaultMaster)
                    {
                        cameraRigController.nextTarget = networkUserBodyObject;
                        cameraRigController.cameraMode = CameraModePlayerBasic.playerBasic;
                    }
                    else if (networkUserBodyObject && networkUser.master == clientDefaultMaster)
                    {
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
                            Destroy(cameras[num].gameObject);
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
                    Destroy(cameras[m].gameObject);
                }
            }
        }

        private void SetupSceneChange(On.RoR2.Networking.NetworkManagerSystem.orig_OnClientSceneChanged orig, NetworkManagerSystem self, NetworkConnection conn)
        {
            orig(self, conn);

            if (!runIsActive) return;
            if (RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.MonsterTeamGainsItems)) DirectorState.monsterInv = RoR2.Artifacts.MonsterTeamGainsItemsArtifactManager.monsterTeamInventory;
            else DirectorState.monsterInv = null;
            if (RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.EliteOnly) && clientModeDirector) clientModeDirector.eliteTierIndex = EliteTierIndex.Honor1;

            DirectorState.snapToNearestNode = nearestNodeSnap;
            DirectorState.eliteTiers = CombatDirector.eliteTiers;

            if (_modEnabled != null)
            {
                DirectorState.baseCreditGain = _directorCredInit.Value;
                DirectorState.creditGainPerLevel = _directorCredGain.Value;
                DirectorState.baseWalletSize = _directorWalletInit.Value;
                DirectorState.walletGainPerLevel = _directorWalletGain.Value;
                DirectorState.directorSelfSpawnCap = _directorSpawnCap.Value;
            }

            if (currentDirectorConfig != null)
            {
                if (!currentDirectorConfig.serverData) DirectorState.snapToNearestNode = currentDirectorConfig.snapToNearestNode;
                else
                {
                    DirectorState.baseCreditGain = currentDirectorConfig.creditInit;
                    DirectorState.creditGainPerLevel = currentDirectorConfig.creditGain;
                    DirectorState.baseWalletSize = currentDirectorConfig.walletInit;
                    DirectorState.walletGainPerLevel = currentDirectorConfig.walletGain;
                    DirectorState.directorSelfSpawnCap = currentDirectorConfig.spawnCap;
                }
            }

#if DEBUG
            panel = Instantiate(ProvidirectorResources.debugPanelPrefab).GetComponent<DebugInfoPanel>();
            if (panel) PLog("Instantiated Debug Panel.");
            else PLog(LogLevel.Error, "Failed to instantiate debug panel.");
#endif

            if (directorIsLocal)
            {
                if (directorIsLocal && activeClientDirectorObject == null)
                {
                    activeClientDirectorObject = Instantiate(ProvidirectorResources.clientDirectorPrefab);
                    clientModeDirector = activeClientDirectorObject.GetComponent<ClientState>();
                    PLog("Added {0} monster cards to the client director.", ClientState.UpdateMonsterSelection());
                    clientModeDirector.OnCachedLimitReached += ClientModeDirector_OnCachedLimitReached;
                }

                SendNetMessageSingle(directorUser.connectionToServer, MessageType.DirectorSync, 1, new SyncDirector(false));
                if (activeHud == null) activeHud = Instantiate(ProvidirectorResources.hudPrefab);
            }
            PLog("Attempting HUD afterinit");
            TrySetHUD();
        }

        private void ClientModeDirector_OnCachedLimitReached(int obj)
        {
            SendSingleGeneric(localToServerConnection, MessageType.CachedCredits, (uint) obj);
        }

        public void TrySetHUD()
        {
            if (!activeHud)
            {
                //PLog("No HUD to set params for, cancelling.");
                return;
            }
            bool flag = true;
            if (mainCamera && mainCamera.uiCam)
            {
                if (activeHud) activeHud.GetComponent<Canvas>().worldCamera = mainCamera.uiCam;
            }
            else
            {
                flag = false;
                //PLog("Failed to acquire main camera");
            }
            if (directorIsLocal)
            {
                hud = activeHud.GetComponent<ProvidirectorHUD>();
                if (hud)
                {
                    hud.clientState = clientModeDirector;
                    targetHealthbar = hud.spectateHealthBar;
                    targetHealthbar.style = ProvidirectorResources.GenerateHBS();
                }
                else
                {
                    flag = false;
                    //PLog("Failed to setup main HUD components");
                }
                if (directorUser.master) clientDefaultMaster = directorUser.master;
                else
                {
                    flag = false;
                    //PLog("Failed to acquire local master");
                }
            }
            if (!flag)
            {
                PLog("HUD Setup incomplete, retrying in 0.5s");
                Invoke("TrySetHUD", 0.5f);
            }

            else
            {
#if DEBUG
                panel.GetComponent<Canvas>().worldCamera = mainCamera.uiCam;
#endif
                if (!directorIsLocal) return;
                ToggleHUD(true);
                SetBaseUIVisible(false);
                SetFirstPersonClient(false);
                PLog("HUD setup complete");
                if (NetworkManager.networkSceneName.Equals("voidraid"))
                    StartCoroutine(MoveCollisionAttempt(voidlingTriggerZone, NetworkManager.networkSceneName));
                else if (NetworkManager.networkSceneName.Equals("moon2"))
                    StartCoroutine(MoveCollisionAttempt(moonFightTriggerZone, NetworkManager.networkSceneName));
                else
                    StartCoroutine(MoveCollisionAttempt(defaultZone, NetworkManager.networkSceneName));

            }
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

        private void ChangeTarget(bool direction = true)
        {
            if (!clientModeDirector) return;
            List<CharacterBody> instanceList = new List<CharacterBody>(CharacterBody.readOnlyInstancesList);
            if (instanceList.Count == 0) return;
            CharacterBody characterBody = spectateTarget ? spectateTarget.GetComponent<CharacterBody>() : null;
            if (direction) instanceList.Reverse();
            int num = characterBody ? instanceList.IndexOf(characterBody) : 0;
            for (int i = num + 1; i < instanceList.Count; i++)
            {
                if (IsTargetable(instanceList[i]) || debugEnabled)
                {
                    spectateTarget = instanceList[i].gameObject;
                    if (clientModeDirector) clientModeDirector.spectateTarget = spectateTarget;
                    return;
                }
            }
            for (int j = 0; j <= num; j++)
            {
                if (IsTargetable(instanceList[j]) || debugEnabled)
                {
                    spectateTarget = instanceList[j].gameObject;
                    if (clientModeDirector) clientModeDirector.spectateTarget = spectateTarget;
                    return;
                }
            }
        }

        private void ToggleHUD(bool state)
        {
            if (activeHud)
            {
                activeHud.SetActive(state);
            }
        }

        private IEnumerator SetFirstPersonClient(bool state, float delay = 0f)
        {
            if (!(runIsActive && directorIsLocal)) yield break;
            // PLog("SetFirstPersonClient called.");

            while (!(directorUser.GetCurrentBody() && clientDefaultMaster && directorUser.GetCurrentBody() != clientDefaultMaster.GetBody()))
            {
                PLog("Could not find non-default body for DirectorUser. Retrying in 0.1s...");
                if (!(runIsActive && directorIsLocal && !activeHud.activeSelf)) yield break;
                yield return new WaitForSeconds(0.1f);
            }
            GameObject bodyObject = directorUser.GetCurrentBody().gameObject;
            // PLog("First Person change target: {0}", bodyObject.name);
            bodyObject.GetComponent<NetworkIdentity>().localPlayerAuthority = true;
            SendSingleGeneric(directorUser.connectionToServer, MessageType.RequestBodyResync, true);
            if (state)
            {
                // PLog("Waiting 5 seconds...");
                yield return new WaitForSeconds(delay);
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

        private IEnumerator MoveCollisionAttempt(Vector3 position, string intendedSceneName) // Must be called on the end of the director
        {
            if (!(runIsActive && directorIsLocal))
            {
                PLog("Run is not active, or the director is not local. Cancelling the move collision attempt.");
                yield break;
            }
            while (!(clientDefaultMaster && NetworkManager.networkSceneName.Equals(intendedSceneName) && clientDefaultMaster.GetBodyObject() && Run.instance.livingPlayerCount >= NetworkUser.readOnlyInstancesList.Count))
            {
                PLog("Cannot move yet. Scene {0}, CDM {1}, Players {2}/{3}", NetworkManager.networkSceneName, clientDefaultMaster == null ? "null" : clientDefaultMaster, Run.instance.livingPlayerCount, NetworkUser.readOnlyInstancesList.Count);
                yield return new WaitForSeconds(1f);
            }
            GameObject g = clientDefaultMaster.GetBodyObject();
            TeleportHelper.TeleportGameObject(g, position);
            PLog("Moved {0} MoveCollisionAttempt --> {1} (in {2})", clientDefaultMaster, position, NetworkManager.networkSceneName);
        }
        #endregion

        #region Universal Methods

        private void GlobalStageStart(Stage obj)
        {
            if (!runIsActive) return;
            string sceneName = obj.sceneDef.baseSceneName;
            if (sceneName.Equals("arena"))
            {
                Debug.Log("Void Field setup called");
                if (clientModeDirector)
                {
                    clientModeDirector.rateModifier = ClientState.RateModifier.Locked;
                    ClientState.spawnableCharacters.Clear();
                }
                
            }
        }

        private void TryInterceptMasterTP(On.RoR2.MapZone.orig_TryZoneStart orig, MapZone self, Collider other)
        {
            if (!runIsActive) { orig(self, other); return; }
            CharacterMaster master = other.GetComponent<CharacterBody>()?.master;
            if ((master == clientDefaultMaster || master == currentMaster) && newCharacterMasterSpawnGrace > 0)
            {
                PLog("Cancelling Zone Start for current master.");
            }
            else orig(self, other);
        }

        private void Run_BeginGameOver(On.RoR2.Run.orig_BeginGameOver orig, Run self, GameEndingDef gameEndingDef)
        {
            if (debugEnabled && runIsActive && !gameEndingDef.isWin) return;
            orig(self, gameEndingDef);
            if (runIsActive) Run_onRunDestroyGlobal(self);
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
            clientDefaultMaster = null;
            currentDirectorConfig = null;
            if (panel) Destroy(panel);
            panel = null;
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

            if (newCharacterMasterSpawnGrace > 0) newCharacterMasterSpawnGrace -= Time.deltaTime;
            else newCharacterMasterSpawnGrace = 0;
            bool honorenabled = RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.EliteOnly);

#if DEBUG
            if (panel && InputManager.DebugSpawn.justPressed) panel.gameObject.SetActive(!panel.gameObject.activeSelf);
#endif
            if (serverModeDirector)
            {
                relockTargetServerTimer -= Time.deltaTime;
                if (relockTargetServerTimer < 0)
                {
                    relockTargetServerTimer = 4f;
                    RelockFocus();
                }
            }

            if (clientModeDirector == null) return;

            if (spectateTarget == null) ChangeTarget();

            // Only Local Effects

            if ((localUser.eventSystem && localUser.eventSystem.isCursorVisible) || !activeHud.activeSelf) return;

            if (InputManager.ToggleAffixRare.down) clientModeDirector.eliteTierIndex = EliteTierIndex.Tier2;
            else if (InputManager.ToggleAffixCommon.down && !honorenabled) clientModeDirector.eliteTierIndex = EliteTierIndex.Tier1;
            else if (honorenabled) clientModeDirector.eliteTierIndex = EliteTierIndex.Honor1;
            else clientModeDirector.eliteTierIndex = EliteTierIndex.Normal;

            if (InputManager.NextTarget.justPressed) ChangeTarget();
            if (InputManager.PrevTarget.justPressed) ChangeTarget(false);
            if (InputManager.SwapPage.justPressed)
            {
                PLog("Attempted to swap page to {0}", clientModeDirector.page + 1);
                clientModeDirector.page++;
            }

            // Server interference required
            if (InputManager.Slot1.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(0), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot2.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(1), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot3.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(2), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot4.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(3), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot5.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(4), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.Slot6.justPressed) SendSpawnCommand(localToServerConnection, clientModeDirector.GetTrueIndex(5), clientModeDirector.eliteTierIndex, pos, rot);
            if (InputManager.FocusTarget.justPressed)
            {
                CharacterMaster target = spectateTargetMaster.GetComponent<CharacterMaster>();
                if (clientModeDirector.ActivateFocus(target)) SendFocusCommand(localToServerConnection, target);
            }
            if (InputManager.BoostTarget.justPressed) SendBurstCommand(localToServerConnection);

            if (!(haveControlAuthority && directorUser)) return;

        }

        private void RelockFocus()
        {
            if (focusTargetPersistent == null) return;
            CharacterMaster victim = focusTargetPersistent.GetComponent<CharacterBody>()?.master;
            if (victim == null) return;
            foreach (TeamComponent tc in TeamComponent.GetTeamMembers(TeamIndex.Monster))
            {
                CharacterMaster c = tc.body?.master;
                if (victim == c || c == null) continue;
                foreach (BaseAI ai in c.aiComponents)
                {
                    ai.currentEnemy.gameObject = focusTargetPersistent;
                    ai.enemyAttentionDuration = 10f;
                }
            }
        }
        #endregion

        #region Network Receive
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

        public static MessageSubIdentifier ReadHeader(NetworkReader reader)
        {
            return reader.ReadMessage<MessageSubIdentifier>();
        }

        public void HandleCommsClient(NetworkMessage msg)
        {
            MessageSubIdentifier header = ReadHeader(msg.reader);
            // PLog("Client Received Message {0}: {1}", header.type, header.booleanValue);
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
                    HandleGameStartClient(msg, header);
                    break;
                case MessageType.ModeUpdate:
                    // PLog("Mode update received: {0}", header.booleanValue);
                    ToggleHUD(header.booleanValue);
                    SetBaseUIVisible(!header.booleanValue);
                    break;
                case MessageType.FPUpdate:
                    // PLog("Starting FPUpdate Coroutine");
                    StartCoroutine(SetFirstPersonClient(header.booleanValue, 5f));
                    break;
                case MessageType.DirectorSync:
                    // PLog("Syncing director config for client");
                    currentDirectorConfig = msg.reader.ReadMessage<SyncDirector>();
                    break;
                case MessageType.MovePosition:
                    var data = msg.reader.ReadMessage<MovePosition>();
                    if (directorIsLocal) StartCoroutine(MoveCollisionAttempt(data.position, data.intendedSceneName));
                    break;
                case MessageType.VoidFieldDirectorSync:
                    // PLog("Syncing Void Fields");
                    VoidFieldCardSync sync = msg.reader.ReadMessage<VoidFieldCardSync>();
                    VFStateUpdate(header.booleanValue, sync);
                    break;
                case MessageType.NotifyNewMaster:
                    CharacterMaster newm = msg.reader.ReadMessage<NotifyNewMaster>().target;
                    // PLog("Client hooking into new master {0}", newm ? newm : "null");
                    clientDefaultMaster.playerCharacterMasterController.master = newm;
                    newm.playerCharacterMasterController = clientDefaultMaster.playerCharacterMasterController;
                    newm.playerStatsComponent = clientDefaultMaster.playerStatsComponent;
                    directorUser.masterObject = newm.gameObject;
                    newCharacterMasterSpawnGrace = gracePeriodLength; // Grace period begins
                    break;
                    
                default:
                    // PLog("Client: Invalid Message Received (Msg Subtype {0})", (int)header.type);
                    break;
            }
        }

        public void HandleCommsServer(NetworkMessage msg)
        {
            MessageSubIdentifier header = ReadHeader(msg.reader);
            // PLog("Server Received Message {0}: {1}", header.type, header.booleanValue);
            switch (header.type)
            {
                case MessageType.HandshakeResponse:
                    HandleHandshakeServer(msg, header);
                    break;
                case MessageType.DirectorSync:
                    // PLog("Syncing director config for server");
                    currentDirectorConfig = msg.reader.ReadMessage<SyncDirector>();
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
                case MessageType.RequestBodyResync:
                    if (currentMaster)
                    {
                        // PLog("Current master is set, sending body resync");
                        if (currentMaster.GetBodyObject()) StartCoroutine(SetBodyAuthorityPersistent(currentMaster.GetBodyObject()));
                        currentMaster.onBodyStart += OnBodyFound;
                    }
                    break;
                case MessageType.VoidRaidOnDeath:
                    VoidRaidGauntletUpdate vrgu = msg.reader.ReadMessage<VoidRaidGauntletUpdate>();
                    VoidRaidGauntletController.instance?.TryOpenGauntlet(vrgu.position, vrgu.nid);
                    break;
                case MessageType.CachedCredits:
                    if (CombatDirector.instancesList.Count > 0)
                    {
                        float transferCredits = 0.4f * header.returnValue;
                        CombatDirector combatDirector = CombatDirector.instancesList[0];
                        combatDirector.monsterCredit += transferCredits;
                        PLog("Sent {0} unused monster credits to the first director", transferCredits);
                    }
                    break;
                default:
                    // PLog("Server: Invalid Message Received (Msg Subtype {0})", (int)header.type);
                    break;
            }
            void OnBodyFound(CharacterBody c)
            {
                StartCoroutine(SetBodyAuthorityPersistent(c.gameObject));
                c.master.onBodyStart -= OnBodyFound;
            }
        }

        public void HandleGameStartClient(NetworkMessage msg, MessageSubIdentifier header)
        {
            GameStart gs = msg.ReadMessage<GameStart>();
            runIsActive = true;
            directorUser = gs.user;
        }

        public void HandleHandshakeClient(NetworkMessage msg, MessageSubIdentifier sid)
        {
            if (!NetworkServer.active) directorUser = sid.booleanValue && modEnabled ? localUser.currentNetworkUser : null;
            if (sid.booleanValue) SendSingleGeneric(msg.conn, MessageType.HandshakeResponse, modEnabled);
        }

        public void HandleHandshakeServer(NetworkMessage msg, MessageSubIdentifier sid)
        {
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
            if (sid.booleanValue)
            {
                if (toChange != null)
                {
                    if (directorUser) SendSingleGeneric(directorUser.connectionToClient, MessageType.Handshake, false);
                    directorUser = toChange;
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = String.Format("Providirector -- Director response OK, set to {0}", directorUser.GetNetworkPlayerName().GetResolvedName())
                    });
                    foreach (NetworkUser nu in NetworkUser.instancesList)
                    {
                        if (nu != directorUser) SendSingleGeneric(nu.connectionToClient, MessageType.Handshake, false);
                    }
                }
                else PLog("Error: Cannot find NetworkUser associated with connection {0}", msg.conn.connectionId);
            }
            else
            {
                PLog("RECEIVE Director Refusal");
                if (toChange == directorUser)
                {
                    directorUser = null;
                }
            }
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
                bool result = serverModeDirector.TrySpawn(request.slotIndex, request.position, request.rotation, request.eliteClassIndex, request.enableSnap, out CharacterMaster spawned, out int cost);
                // PLog("Server Spawn Result: {0}", result);
                writer.Write(new SpawnConfirm()
                {
                    cost = cost,
                    spawned = spawned
                });
                writer.FinishMessage();
                msg.conn?.SendWriter(writer, Channels.DefaultReliable);
            }
        }

        public void HandleBurstClient(NetworkMessage msg, MessageSubIdentifier sid) // Receives boolean response
        {
            clientModeDirector.DoBurstTrigger(sid.booleanValue);
        }

        public void HandleBurstServer(NetworkMessage msg, MessageSubIdentifier sid)
        {
            SendSingleGeneric(msg.conn, MessageType.Burst, serverModeDirector.ActivateBurst());
        }

        public void HandleFocusServer(NetworkMessage msg, MessageSubIdentifier sid)
        {
            FocusEnemy focusEnemy = msg.reader.ReadMessage<FocusEnemy>();
            if (focusEnemy.target && focusEnemy.target.bodyInstanceObject != focusTargetPersistent) focusTargetPersistent = focusEnemy.target.bodyInstanceObject;
            else focusTargetPersistent = null;
            RelockFocus();
        }

        private void VFStateUpdate(bool state, VoidFieldCardSync sync)
        {
            if (!(runIsActive && clientModeDirector)) return;
            ClientState.spawnableCharacters.Clear();
            if (state)
            {
                clientModeDirector.rateModifier = ClientState.RateModifier.TeleporterBoosted;
                PLog("{0} Cards to sync", sync.cardDisplayDatas);
                foreach (SpawnCardDisplayData card in sync.cardDisplayDatas) ClientState.spawnableCharacters.Add(card);
            }
            else clientModeDirector.rateModifier = ClientState.RateModifier.Locked;
        }
        #endregion

        #region Network Send
        public static void SendSingleGeneric(NetworkConnection connection, MessageType subtype, uint value)
        {
            NetworkWriter writer = new NetworkWriter();
            writer.StartMessage(prvdChannel);
            writer.Write(new MessageSubIdentifier { returnValue = value, type = subtype});
            writer.FinishMessage();
            connection?.SendWriter(writer, Channels.DefaultReliable); // Default Reliable Channel - nothing fancy
        }

        public static void SendSingleGeneric(NetworkConnection connection, MessageType subtype, bool value)
        {
            SendSingleGeneric(connection, subtype, value ? (uint)1 : 0);
        }

        public void SendNetMessageSingle(NetworkConnection connection, MessageType type, uint value, MessageBase message)
        {
            NetworkWriter writer = new NetworkWriter();
            writer.StartMessage(prvdChannel);
            writer.Write(new MessageSubIdentifier()
            {
                returnValue = value,
                type = type
            });
            writer.Write(message);
            writer.FinishMessage();
            connection?.SendWriter(writer, Channels.DefaultReliable);
        }

        public bool SendSpawnCommand(NetworkConnection connection, int slotIndex, EliteTierIndex eliteClassIndex, Vector3 position, Quaternion rotation)
        {
            if (!clientModeDirector.IsAbleToSpawn(slotIndex, position, rotation, out int _, eliteIndexOverride: eliteClassIndex)) return false;
            NetworkWriter writer = new NetworkWriter();
            writer.StartMessage(prvdChannel);
            writer.Write(new MessageSubIdentifier { type = MessageType.SpawnEnemy });
            writer.Write(new SpawnEnemy()
            {
                slotIndex = slotIndex,
                eliteClassIndex = eliteClassIndex,
                position = position,
                rotation = rotation,
                enableSnap = nearestNodeSnap
            });
            writer.FinishMessage();
            connection?.SendWriter(writer, Channels.DefaultReliable);
            return true;
        }

        public void SendBurstCommand(NetworkConnection connection)
        {
            if (!clientModeDirector.canBurst)
            {
                clientModeDirector.DoBurstTrigger(false);
                return;
            }
            SendSingleGeneric(connection, MessageType.Burst, true);
        }

        public void SendFocusCommand(NetworkConnection connection, CharacterMaster target)
        {
            // PLog("Send Focus Command! {0}", target);
            if (target == null) return;
            NetworkWriter writer = new NetworkWriter();
            writer.StartMessage(prvdChannel);
            writer.Write(new MessageSubIdentifier { type = MessageType.FocusEnemy });
            writer.Write(new FocusEnemy()
            {
                target = target
            });
            writer.FinishMessage();
            connection?.SendWriter(writer, Channels.DefaultReliable);
        }

        public IEnumerator SendPositionUpdate(Vector3 position, string intendedSceneName, float delay = 0)
        {
            if (!(directorUser && haveControlAuthority))
            {
                PLog("Unable to send position update as either no directorUser is set or we don't have server permissions");
                yield break;
            }
            if (delay > 0) yield return new WaitForSeconds(delay);
            SendNetMessageSingle(directorUser.connectionToClient, MessageType.MovePosition, 1, new MovePosition()
            {
                position = position,
                intendedSceneName = intendedSceneName
            });
        }
        #endregion

        #region Network Util and Implement
        private void InterpretDirectorCommand(On.RoR2.Chat.orig_CCSay orig, ConCommandArgs args)
        {
            orig(args);
            if (!NetworkServer.active || runIsActive) return;
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
                            baseToken = string.Format("Providirector -- Use '% [name]' to set the director. Default is the host.")
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
                            baseToken = string.Format("Providirector -- Error - player name or NU Index {0} not found", targetUser)
                        });
                        return;
                    }
                    NetworkUser targetnu = NetworkUser.instancesList[index];
                    SendSingleGeneric(targetnu.connectionToClient, MessageType.Handshake, true);
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = string.Format("Providirector -- Sent director request to {0}", targetnu.GetNetworkPlayerName().GetResolvedName())
                    });
                }
            }
        }

        public bool VerifyDirectorSource(NetworkConnection conn)
        {
            return conn == directorUser?.connectionToClient;
        }
        #endregion

        #region General Util
        public static bool IsTargetable(CharacterBody body)
        {
            if (!body) return false;
            TeamComponent tc = body.teamComponent;
            if (!tc) return false;
            return tc.teamIndex == TeamIndex.Player || tc.teamIndex == TeamIndex.Void;
        }
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
        #endregion

        #region Event or Stage-Specific
        private void EncounterFinish(On.EntityStates.Missions.BrotherEncounter.EncounterFinished.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.EncounterFinished self)
        {
            orig(self);
            if (clientModeDirector) clientModeDirector.rateModifier = ClientState.RateModifier.TeleporterBoosted;
        }

        private void BeginMoonPhase(On.EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState.orig_OnEnter orig, EntityStates.Missions.BrotherEncounter.BrotherEncounterPhaseBaseState self)
        {
            if (runIsActive)
            {
                bool characterControlState = self.phaseControllerChildString != "Phase2";
                if (haveControlAuthority)
                {
                    forceScriptEncounterAddControl = characterControlState;
                    StartCoroutine(SendPositionUpdate(defaultZone, "moon2"));
                }
                if (clientModeDirector) clientModeDirector.rateModifier = characterControlState ? ClientState.RateModifier.Locked : ClientState.RateModifier.TeleporterBoosted;
            }
            orig(self);
        }

        private void FieldCardUpdate(On.RoR2.ArenaMissionController.orig_BeginRound orig, ArenaMissionController self)
        {
            orig(self);
            if (runIsActive && directorUser && serverModeDirector)
            {
                ServerState.spawnCardTemplates.Clear();
                var toSend = new SpawnCardDisplayData[ArenaMissionController.instance.activeMonsterCards.Count];
                int i = 0;
                foreach (DirectorCard c in ArenaMissionController.instance.activeMonsterCards) {
                    ServerState.spawnCardTemplates.Add(c.spawnCard);
                    toSend[i] = ClientState.ExtractDisplayData(c.spawnCard);
                    i++;
                }
                // PLog("AMC contained {0} active monster cards.", ArenaMissionController.instance.activeMonsterCards.Count);
                DirectorState.monsterInv = ArenaMissionController.instance.inventory;
                SendNetMessageSingle(directorUser.connectionToClient, MessageType.VoidFieldDirectorSync , 1, new VoidFieldCardSync()
                {
                    cardDisplayDatas = toSend
                });
                // PLog("Sent VF director sync");
            } else
            {
                PLog("No director User found! Canceling send");
            }
            
        }

        private void RoundEndLock(On.RoR2.ArenaMissionController.orig_EndRound orig, ArenaMissionController self)
        {
            orig(self);
            if (runIsActive && directorUser)
            {
                ServerState.spawnCardTemplates.Clear();
                // PLog("Template List Cleared.");
                DirectorState.monsterInv = ArenaMissionController.instance.inventory;
                SendNetMessageSingle(directorUser.connectionToClient, MessageType.VoidFieldDirectorSync, 0, new VoidFieldCardSync()
                {
                    cardDisplayDatas = new SpawnCardDisplayData[0]
                });
                PLog("Sent VF director sync");
            }
            else
            {
                PLog("No director User found! Canceling send");
            }
        }

        private void VoidlingReady(On.RoR2.VoidRaidGauntletController.orig_Start orig, VoidRaidGauntletController self)
        {
            orig(self);
            if (!runIsActive) return;
            if (clientModeDirector) clientModeDirector.rateModifier = ClientState.RateModifier.Locked;
            if (haveControlAuthority)
            {
                foreach (ScriptedCombatEncounter sce in self.phaseEncounters)
                    sce.onBeginEncounter += (ScriptedCombatEncounter _) => StartCoroutine(SendPositionUpdate(defaultZone, "voidraid"));
            }
        }

        private void MoveToEscapeZone(On.RoR2.HoldoutZoneController.orig_Start orig, HoldoutZoneController self)
        {
            // PLog("Holdout zone activated! Token: {0}", self.inBoundsObjectiveToken);
            orig(self);
            if (self.inBoundsObjectiveToken.Equals("OBJECTIVE_MOON_CHARGE_DROPSHIP"))
            {
                PLog("EscapeSequence now active.");
                StartCoroutine(SendPositionUpdate(rescueShipTriggerZone, "moon2", 3f));
            }
        }

        private void SetVelocityZeroVacuum(On.EntityStates.VoidRaidCrab.BaseVacuumAttackState.orig_OnEnter orig, EntityStates.VoidRaidCrab.BaseVacuumAttackState self)
        {
            orig(self);
            RigidbodyMotor motor = self.outer.commonComponents.rigidbodyMotor;
            if (runIsActive && motor && motor.hasEffectiveAuthority) motor.moveVector = Vector3.zero;
        }

        private void SetVelocityZeroSpin(On.EntityStates.VoidRaidCrab.BaseSpinBeamAttackState.orig_OnEnter orig, EntityStates.VoidRaidCrab.BaseSpinBeamAttackState self)
        {
            orig(self);
            RigidbodyMotor motor = self.outer.commonComponents.rigidbodyMotor;
            if (runIsActive && motor && motor.hasEffectiveAuthority) motor.moveVector = Vector3.zero;
        }

        private void DisableDoppelgangerControl(On.RoR2.Artifacts.DoppelgangerInvasionManager.orig_CreateDoppelganger orig, CharacterMaster srcCharacterMaster, Xoroshiro128Plus rng)
        {
            bool forceScriptEncounterAddControlCached = forceScriptEncounterAddControl;
            bool firstPersonEnabledCached = enableFirstPerson;
            forceScriptEncounterAddControl = false;
            firstPersonEnabledCached = false;
            orig(srcCharacterMaster, rng);
            forceScriptEncounterAddControl = forceScriptEncounterAddControlCached;
            enableFirstPerson = firstPersonEnabledCached;
        }

        private void EscapeDeath_FixedUpdate(On.EntityStates.VoidRaidCrab.EscapeDeath.orig_FixedUpdate orig, EntityStates.VoidRaidCrab.EscapeDeath self)
        {
            orig(self);
            if (!self.isAuthority && self.fixedAge >= self.duration && NetworkServer.active)
            {
                //PLog("Destroying the new body ASAP.");
                self.DestroyBodyAsapServer();
            }
        }

        private void SafeBeginEncounter(On.RoR2.VoidRaidGauntletController.orig_OnBeginEncounter orig, VoidRaidGauntletController self, ScriptedCombatEncounter encounter, int encounterIndex)
        {
            if (self.currentDonut == null)
            {
                //PLog("No donut currently set. Returning...");
                return;
            }
            while (self.gauntletIndex < encounterIndex)
            {
                //PLog("Calling TOG...");
                self.TryOpenGauntlet(self.currentDonut.crabPosition.position, NetworkInstanceId.Invalid);
            }

        }

        private void SendStartNextDonutMessage(On.EntityStates.VoidRaidCrab.EscapeDeath.orig_OnExit orig, EntityStates.VoidRaidCrab.EscapeDeath self)
        {
            orig(self);
            if (runIsActive && directorIsLocal && !haveControlAuthority)
            {
                //PLog("Sending update request for next donut.");
                SendNetMessageSingle(directorUser.connectionToServer, MessageType.VoidRaidOnDeath, 1, new VoidRaidGauntletUpdate()
                {
                    nid = self.netId,
                    position = self.gauntletEntrancePosition
                });
            }
        }

        #endregion
    }
}