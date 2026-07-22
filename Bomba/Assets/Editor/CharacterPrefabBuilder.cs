using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CharacterPrefabBuilder
{
    private const string PrefabFolder = "Assets/Prefabs";
    private const string MaterialFolder = "Assets/Materials";
    private const string PrefabPath = PrefabFolder + "/Character.prefab";
    private const string MaterialPath = MaterialFolder + "/CharacterCapsule.mat";
    private const string CharacterModelPath = "Assets/Characters/Swat/Model/Swat.fbx";
    private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";

    [MenuItem("Tools/Briefcase Protocol/Rebuild Character Prefab and Lobby")]
    public static void BuildCharacterPrefabAndLobby()
    {
        EnsureFolder(PrefabFolder);
        EnsureFolder(MaterialFolder);

        Material material = CreateOrUpdateCharacterMaterial();
        GameObject prefab = CreateCharacterPrefab(material);
        ValidatePrefab(prefab);
        AddCharacterToLobby(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Character prefab created and added to the Lobby scene.");
    }

    private static GameObject CreateCharacterPrefab(Material material)
    {
        GameObject root = new GameObject("Character");

        try
        {
            CharacterController characterController = root.AddComponent<CharacterController>();
            characterController.radius = 0.3f;
            characterController.height = 1.8f;
            characterController.center = new Vector3(0f, 0.9f, 0f);
            characterController.slopeLimit = 50f;
            characterController.stepOffset = 0.3f;
            characterController.skinWidth = 0.03f;
            characterController.minMoveDistance = 0f;

            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Capsule";
            capsule.transform.SetParent(root.transform, false);
            capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            capsule.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            UnityEngine.Object.DestroyImmediate(capsule.GetComponent<CapsuleCollider>());
            capsule.GetComponent<MeshRenderer>().sharedMaterial = material;

            GameObject modelRoot = new GameObject("CharacterModelRoot");
            modelRoot.transform.SetParent(root.transform, false);

            GameObject characterModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPath);
            if (characterModelPrefab != null)
            {
                GameObject characterModel = (GameObject)PrefabUtility.InstantiatePrefab(characterModelPrefab);
                characterModel.name = "Swat";
                characterModel.transform.SetParent(modelRoot.transform, false);
            }

            GameObject cameraPivot = new GameObject("CameraPivot");
            cameraPivot.transform.SetParent(root.transform, false);
            cameraPivot.transform.localPosition = new Vector3(0f, 1.65f, 0f);

            GameObject cameraObject = new GameObject("PlayerCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraPivot.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 1000f;
            camera.fieldOfView = 60f;
            cameraObject.AddComponent<AudioListener>();

            FirstPersonCharacterController controller = root.AddComponent<FirstPersonCharacterController>();
            controller.SetPrefabReferences(cameraPivot.transform, capsule.transform, modelRoot.transform);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (savedPrefab == null)
            {
                throw new InvalidOperationException("Character prefab could not be saved.");
            }

            return savedPrefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void AddCharacterToLobby(GameObject prefab)
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(LobbyScenePath);
        bool openedByBuilder = !scene.IsValid() || !scene.isLoaded;
        if (openedByBuilder)
        {
            scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Additive);
        }

        try
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Character")
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<Camera>() != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "Character";
            instance.transform.SetPositionAndRotation(new Vector3(0f, 0.02f, -10f), Quaternion.identity);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateLobbyScene(scene);
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }

            if (openedByBuilder && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static Material CreateOrUpdateCharacterMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
        {
            throw new InvalidOperationException("A compatible character shader could not be found.");
        }

        if (material == null)
        {
            material = new Material(shader)
            {
                name = "CharacterCapsule"
            };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.color = new Color(0.12f, 0.45f, 0.78f, 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ValidatePrefab(GameObject prefab)
    {
        if (prefab == null || prefab.GetComponent<CharacterController>() == null ||
            prefab.GetComponent<FirstPersonCharacterController>() == null)
        {
            throw new InvalidOperationException("Character prefab validation failed.");
        }

        Camera prefabCamera = prefab.GetComponentInChildren<Camera>(true);
        if (prefabCamera == null || prefabCamera.transform.parent == null ||
            !Mathf.Approximately(prefabCamera.transform.parent.localPosition.y, 1.65f))
        {
            throw new InvalidOperationException("Character eye-level camera validation failed.");
        }

        if (prefab.transform.Find("CharacterModelRoot") == null)
        {
            throw new InvalidOperationException("Character model root validation failed.");
        }

    }

    private static void ValidateLobbyScene(Scene scene)
    {
        GameObject character = null;
        int cameraCount = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Character")
            {
                character = root;
            }

            cameraCount += root.GetComponentsInChildren<Camera>(true).Length;
        }

        if (character == null || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(character) != PrefabPath)
        {
            throw new InvalidOperationException("Lobby character prefab instance validation failed.");
        }

        Camera characterCamera = character.GetComponentInChildren<Camera>(true);
        if (cameraCount != 1 || characterCamera == null)
        {
            throw new InvalidOperationException("Lobby must contain exactly the character camera.");
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = folderPath.Substring(0, folderPath.LastIndexOf('/'));
        string name = folderPath.Substring(folderPath.LastIndexOf('/') + 1);
        AssetDatabase.CreateFolder(parent, name);
    }
}
