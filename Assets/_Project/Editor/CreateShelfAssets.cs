using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates the standalone shelf prefab and a test scene from the existing GLB.
/// Run from Tools > Supermarket > Create Shelf Scene + Prefab.
/// </summary>
public static class CreateShelfAssets
{
    private const string ModelPath = "Assets/_Project/Models/Furniture/Single_Shelf.glb";
    private const string PrefabFolder = "Assets/_Project/Prefabs/Furniture";
    private const string PrefabPath = PrefabFolder + "/Shelf.prefab";
    private const string SceneFolder = "Assets/_Project/Scenes";
    private const string ScenePath = SceneFolder + "/ShelfScene.unity";

    [MenuItem("Tools/Supermarket/Create Shelf Scene + Prefab")]
    public static void Create()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog("Shelf setup", "Could not find the shelf model at:\n" + ModelPath, "OK");
            return;
        }

        if ((File.Exists(PrefabPath) || File.Exists(ScenePath)) &&
            !EditorUtility.DisplayDialog("Replace shelf assets?",
                "Shelf.prefab and/or ShelfScene.unity already exist. Replace the generated assets?", "Replace", "Cancel"))
            return;

        EnsureFolder("Assets/_Project", "Editor");
        EnsureFolder("Assets/_Project", "Prefabs");
        EnsureFolder(PrefabFolder.Substring(0, PrefabFolder.LastIndexOf('/')), "Furniture");
        EnsureFolder("Assets/_Project", "Scenes");

        GameObject root = new GameObject("Shelf");
        GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        if (modelInstance == null) modelInstance = Object.Instantiate(model);
        modelInstance.name = "ShelfModel";
        modelInstance.transform.SetParent(root.transform, false);

        Bounds localBounds;
        Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 corner = new Vector3(
                    x == 0 ? worldBounds.min.x : worldBounds.max.x,
                    y == 0 ? worldBounds.min.y : worldBounds.max.y,
                    z == 0 ? worldBounds.min.z : worldBounds.max.z);
                Vector3 local = root.transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
            localBounds = new Bounds((min + max) * 0.5f, max - min);
        }
        else
        {
            localBounds = new Bounds(Vector3.zero, Vector3.one);
            Debug.LogWarning("Shelf model has no Renderer components; using a 1x1x1 placement collider.");
        }

        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        BoxCollider placementBox = root.AddComponent<BoxCollider>();
        placementBox.center = localBounds.center;
        placementBox.size = Vector3.Max(localBounds.size, Vector3.one * 0.05f);

        ShelfPlacement placement = root.AddComponent<ShelfPlacement>();
        placement.placementCollider = placementBox;

        PlacementChecker checker = root.AddComponent<PlacementChecker>();
        checker.shelf = placement;
        checker.floorTag = "ShelfFloor";
        checker.wallTag = "ShelfWall";
        checker.blockingLayers = ~0;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject shelfInScene = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
        shelfInScene.name = "Shelf";
        shelfInScene.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        EditorSceneManager.SaveScene(scene, ScenePath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        EditorUtility.DisplayDialog("Shelf assets created",
            "Created:\n" + PrefabPath + "\n" + ScenePath +
            "\n\nThe original MainScene was not changed. Confirm the collider fit and floor/wall Tags in Unity before using the prefab in gameplay.",
            "Great");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }
}