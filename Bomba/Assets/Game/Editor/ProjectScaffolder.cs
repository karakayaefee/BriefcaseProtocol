using System.Collections.Generic;
using BriefcaseProtocol.Core;
using BriefcaseProtocol.Gameplay.Briefcases;
using BriefcaseProtocol.Gameplay.Modules;
using BriefcaseProtocol.Gameplay.Player;
using BriefcaseProtocol.Gameplay.Setup;
using BriefcaseProtocol.Gameplay.Traps;
using BriefcaseProtocol.Networking;
using BriefcaseProtocol.Services;
using BriefcaseProtocol.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BriefcaseProtocol.Editor
{
    public static class ProjectScaffolder
    {
        private const string GeneratedRoot = "Assets/Game/Generated";
        private const string SceneRoot = GeneratedRoot + "/Scenes";
        private const string PrefabRoot = GeneratedRoot + "/Prefabs";
        private const string DataRoot = GeneratedRoot + "/Data";

        [InitializeOnLoadMethod]
        private static void ScheduleInitialProjectSetup()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneRoot + "/Bootstrap.unity") != null)
            {
                return;
            }

            EditorApplication.delayCall += RunInitialProjectSetup;
        }

        private static void RunInitialProjectSetup()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunInitialProjectSetup;
                return;
            }

            var originalScene = SceneManager.GetActiveScene();
            if (originalScene.isDirty)
            {
                Debug.LogWarning("Briefcase Protocol setup paused because the active scene has unsaved changes. " +
                    "Save it, then use Briefcase Protocol/Setup Prototype Project.");
                return;
            }

            var originalPath = originalScene.path;
            SetupPrototypeProject();
            if (!string.IsNullOrEmpty(originalPath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(originalPath) != null)
            {
                EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
            }
        }

        [MenuItem("Briefcase Protocol/Setup Prototype Project")]
        public static void SetupPrototypeProject()
        {
            EnsureFolders();
            ConfigurePlayerSettings();
            var balance = CreateBalanceAsset();
            var playerPrefab = CreatePlayerPrefab();
            CreateBootstrapScene(balance, playerPrefab);
            CreatePresentationScene("MainMenu", new Color(0.025f, 0.035f, 0.05f));
            CreatePresentationScene("Lobby", new Color(0.06f, 0.035f, 0.035f));
            CreateGameScene();
            ConfigureBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Briefcase Protocol prototype project generated successfully.");
        }

        public static void SetupPrototypeProjectBatch()
        {
            SetupPrototypeProject();
            EditorApplication.Exit(0);
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Game", "Generated");
            EnsureFolder(GeneratedRoot, "Scenes");
            EnsureFolder(GeneratedRoot, "Prefabs");
            EnsureFolder(GeneratedRoot, "Data");
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "BugKnot Studios";
            PlayerSettings.productName = "Briefcase Protocol";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.bugknot.briefcaseprotocol");
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
        }

        private static GameBalanceConfig CreateBalanceAsset()
        {
            var path = DataRoot + "/GameBalance.asset";
            var existing = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(path);
            if (existing != null) return existing;
            var balance = ScriptableObject.CreateInstance<GameBalanceConfig>();
            AssetDatabase.CreateAsset(balance, path);
            return balance;
        }

        private static GameObject CreatePlayerPrefab()
        {
            var path = PrefabRoot + "/NetworkPlayer.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "NetworkPlayer";
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            player.AddComponent<NetworkObject>();

            var cameraObject = new GameObject("PlayerCamera");
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            cameraObject.AddComponent<AudioListener>();

            var playerController = player.AddComponent<NetworkPlayerController>();
            SetObjectReference(playerController, "playerCamera", camera);
            var prefab = PrefabUtility.SaveAsPrefabAsset(player, path);
            Object.DestroyImmediate(player);
            return prefab;
        }

        private static void CreateBootstrapScene(GameBalanceConfig balance, GameObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("BriefcaseProtocol");
            var manager = root.AddComponent<NetworkManager>();
            var transport = root.AddComponent<UnityTransport>();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.PlayerPrefab = playerPrefab;
            manager.NetworkConfig.EnableSceneManagement = true;

            root.AddComponent<ProjectBootstrap>();
            var sessions = root.AddComponent<SessionCoordinator>();
            var voice = root.AddComponent<VivoxVoiceService>();
            root.AddComponent<NetworkObject>();
            var match = root.AddComponent<NetworkMatchManager>();
            root.AddComponent<NetworkBudgetManager>();
            var ui = root.AddComponent<PrototypeUIController>();
            SetObjectReference(match, "balance", balance);
            SetObjectReference(ui, "sessions", sessions);
            SetObjectReference(ui, "voice", voice);

            CreateCamera(new Vector3(0f, 2f, -8f), Color.black);
            EditorSceneManager.SaveScene(scene, SceneRoot + "/Bootstrap.unity");
        }

        private static void CreatePresentationScene(string sceneName, Color background)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera(new Vector3(0f, 2f, -8f), background);
            CreateLight();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = sceneName + "_Backdrop";
            floor.transform.localScale = new Vector3(3f, 1f, 2f);
            ApplyColor(floor, background * 2f);
            EditorSceneManager.SaveScene(scene, $"{SceneRoot}/{sceneName}.unity");
        }

        private static void CreateGameScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateLight();
            CreateGreybox();

            var systems = new GameObject("GameSystems");
            systems.AddComponent<BriefcaseRegistry>();

            var real = CreateBriefcase("RealBriefcase", new Vector3(-5f, 0.6f, 4f), BriefcaseKind.Real,
                CombinationRuleKind.ColorTag, 43, new Color(0.16f, 0.18f, 0.2f));
            var fake = CreateBriefcase("FakeBriefcase", new Vector3(6f, 0.6f, -4f), BriefcaseKind.Fake,
                CombinationRuleKind.SerialNumber, 79, new Color(0.2f, 0.18f, 0.16f));
            var wire = CreateWireModule(real, new Vector3(-4.3f, 1.2f, 4f));
            var sequence = CreateSequenceModule(real, new Vector3(-5.7f, 1.2f, 4f));
            CreateModuleSet(wire, sequence);
            var sound = CreateSoundLure(new Vector3(5f, 0.5f, -2f));
            var door = CreateControlledDoor(new Vector3(0f, 1.5f, 1f));

            var validatorObject = new GameObject("SetupValidator");
            var validator = validatorObject.AddComponent<SetupValidator>();
            SetObjectReference(validator, "realBriefcase", real);
            SetObjectReference(validator, "fakeBriefcase", fake);
            SetObjectReference(validator, "wireModule", wire);
            SetObjectReference(validator, "sequenceModule", sequence);
            SetObjectReference(validator, "soundLure", sound);
            SetObjectReference(validator, "controlledDoor", door);

            CreateSpawnService();
            CreatePlacementSockets();
            EditorSceneManager.SaveScene(scene, SceneRoot + "/Game.unity");
        }

        private static BriefcaseController CreateBriefcase(string name, Vector3 position, BriefcaseKind kind,
            CombinationRuleKind rule, int seed, Color color)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            gameObject.transform.position = position;
            gameObject.transform.localScale = new Vector3(1.4f, 0.35f, 0.9f);
            ApplyColor(gameObject, color);
            gameObject.AddComponent<NetworkObject>();
            var briefcase = gameObject.AddComponent<BriefcaseController>();
            SetEnum(briefcase, "kind", (int)kind);
            SetEnum(briefcase, "combinationRule", (int)rule);
            SetInteger(briefcase, "seedOffset", seed);
            return briefcase;
        }

        private static WireLogicModule CreateWireModule(BriefcaseController parent, Vector3 position)
        {
            var root = new GameObject("WireLogicModule");
            root.transform.position = position;
            root.AddComponent<NetworkObject>();
            var module = root.AddComponent<WireLogicModule>();
            SetObjectReference(module, "parentBriefcase", parent);
            var colors = new[] { Color.red, Color.blue, Color.yellow, Color.green, Color.magenta };
            for (var i = 0; i < 5; i++)
            {
                var wire = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wire.name = $"Wire_{i}";
                wire.transform.SetParent(root.transform, false);
                wire.transform.localPosition = new Vector3((i - 2) * 0.14f, 0f, 0f);
                wire.transform.localScale = new Vector3(0.08f, 0.08f, 0.65f);
                ApplyColor(wire, colors[i]);
                wire.AddComponent<WireInteractionNode>().Configure(module, i);
            }
            return module;
        }

        private static SequenceButtonModule CreateSequenceModule(BriefcaseController parent, Vector3 position)
        {
            var root = new GameObject("SequenceButtonModule");
            root.transform.position = position;
            root.AddComponent<NetworkObject>();
            var module = root.AddComponent<SequenceButtonModule>();
            SetObjectReference(module, "parentBriefcase", parent);
            for (var i = 0; i < 4; i++)
            {
                var button = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                button.name = $"Button_{i}";
                button.transform.SetParent(root.transform, false);
                button.transform.localPosition = new Vector3((i % 2) * 0.3f, 0f, (i / 2) * 0.3f);
                button.transform.localScale = new Vector3(0.13f, 0.05f, 0.13f);
                ApplyColor(button, Color.HSVToRGB(i / 4f, 0.65f, 0.9f));
                button.AddComponent<SequenceInteractionNode>().Configure(module, i);
            }
            return module;
        }

        private static void CreateModuleSet(WireLogicModule wire, SequenceButtonModule sequence)
        {
            var root = new GameObject("BombModuleSet");
            root.AddComponent<NetworkObject>();
            var set = root.AddComponent<BombModuleSet>();
            var serialized = new SerializedObject(set);
            var property = serialized.FindProperty("requiredModules");
            property.arraySize = 2;
            property.GetArrayElementAtIndex(0).objectReferenceValue = wire;
            property.GetArrayElementAtIndex(1).objectReferenceValue = sequence;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SoundLureTrap CreateSoundLure(Vector3 position)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            root.name = "SoundLure";
            root.transform.position = position;
            root.transform.localScale = new Vector3(0.35f, 0.25f, 0.35f);
            ApplyColor(root, new Color(0.9f, 0.55f, 0.1f));
            root.AddComponent<NetworkObject>();
            var trap = root.AddComponent<SoundLureTrap>();
            var lightObject = new GameObject("Indicator");
            lightObject.transform.SetParent(root.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.color = Color.yellow;
            light.range = 4f;
            light.enabled = false;
            SetObjectReference(trap, "indicator", light);
            return trap;
        }

        private static ControlledDoorTrap CreateControlledDoor(Vector3 position)
        {
            var root = new GameObject("ControlledDoorTrap");
            root.transform.position = position;
            root.AddComponent<NetworkObject>();
            var zone = root.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.size = new Vector3(2.6f, 3f, 1.2f);
            var trap = root.AddComponent<ControlledDoorTrap>();
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "DoorPanel";
            panel.transform.SetParent(root.transform, false);
            panel.transform.localScale = new Vector3(2.4f, 2.8f, 0.2f);
            panel.transform.localPosition = new Vector3(0f, 3f, 0f);
            ApplyColor(panel, new Color(0.16f, 0.18f, 0.22f));
            var warningObject = new GameObject("WarningLight");
            warningObject.transform.SetParent(root.transform, false);
            warningObject.transform.localPosition = new Vector3(0f, 1.8f, -0.5f);
            var warning = warningObject.AddComponent<Light>();
            warning.color = Color.red;
            warning.range = 4f;
            warning.enabled = false;
            SetObjectReference(trap, "doorPanel", panel.transform);
            SetObjectReference(trap, "warningLight", warning);
            return trap;
        }

        private static void CreateSpawnService()
        {
            var serviceObject = new GameObject("PlayerSpawnService");
            var service = serviceObject.AddComponent<PlayerSpawnService>();
            var positions = new[]
            {
                new Vector3(-7f, 1f, -7f), new Vector3(-5f, 1f, -7f),
                new Vector3(5f, 1f, 7f), new Vector3(7f, 1f, 7f)
            };
            var serialized = new SerializedObject(service);
            var property = serialized.FindProperty("spawnPoints");
            property.arraySize = positions.Length;
            for (var i = 0; i < positions.Length; i++)
            {
                var spawn = new GameObject($"Spawn_{i}").transform;
                spawn.SetParent(serviceObject.transform);
                spawn.position = positions[i];
                property.GetArrayElementAtIndex(i).objectReferenceValue = spawn;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreatePlacementSockets()
        {
            var root = new GameObject("PlacementSockets");
            for (var i = 0; i < 8; i++)
            {
                var socketObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                socketObject.name = $"BriefcaseSocket_{i}";
                socketObject.transform.SetParent(root.transform);
                socketObject.transform.position = new Vector3(-7f + (i % 4) * 4.5f, 0.05f, -5f + (i / 4) * 10f);
                socketObject.transform.localScale = new Vector3(0.8f, 0.04f, 0.8f);
                socketObject.GetComponent<Collider>().isTrigger = true;
                ApplyColor(socketObject, new Color(0.05f, 0.65f, 0.8f));
                socketObject.AddComponent<NetworkObject>();
                socketObject.AddComponent<PlacementSocket>();
            }
        }

        private static void CreateGreybox()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, -0.25f, 0f);
            floor.transform.localScale = new Vector3(20f, 0.5f, 20f);
            ApplyColor(floor, new Color(0.16f, 0.17f, 0.19f));

            CreateWall(new Vector3(0f, 2f, -10f), new Vector3(20f, 4f, 0.25f));
            CreateWall(new Vector3(0f, 2f, 10f), new Vector3(20f, 4f, 0.25f));
            CreateWall(new Vector3(-10f, 2f, 0f), new Vector3(0.25f, 4f, 20f));
            CreateWall(new Vector3(10f, 2f, 0f), new Vector3(0.25f, 4f, 20f));
            CreateWall(new Vector3(-3.3f, 2f, -3f), new Vector3(0.2f, 4f, 8f));
            CreateWall(new Vector3(3.3f, 2f, 3f), new Vector3(0.2f, 4f, 8f));
            CreateWall(new Vector3(0f, 2f, 0f), new Vector3(6f, 4f, 0.2f));
        }

        private static void CreateWall(Vector3 position, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "GreyboxWall";
            wall.transform.position = position;
            wall.transform.localScale = scale;
            ApplyColor(wall, new Color(0.28f, 0.3f, 0.33f));
        }

        private static Camera CreateCamera(Vector3 position, Color background)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = position;
            cameraObject.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            cameraObject.AddComponent<AudioListener>();
            return camera;
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
        }

        private static void ApplyColor(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            renderer.sharedMaterial = material;
        }

        private static void ConfigureBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new(SceneRoot + "/Bootstrap.unity", true),
                new(SceneRoot + "/MainMenu.unity", true),
                new(SceneRoot + "/Lobby.unity", true),
                new(SceneRoot + "/Game.unity", true)
            };
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void SetObjectReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInteger(Object target, string field, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string field, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
