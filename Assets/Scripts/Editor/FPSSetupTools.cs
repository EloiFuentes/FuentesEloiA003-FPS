using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Tools > FPS menu: one-click player setup and placeholder example weapons for testing.</summary>
public static class FPSSetupTools
{
    private const string ExamplesFolder = "Assets/FPS Examples";

    // ------------------------------------------------------------------ Player setup

    [MenuItem("Tools/FPS/Setup Player In Open Scene", priority = 0)]
    public static void SetupPlayer()
    {
        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("FPS Setup", "No PlayerController found in the open scene.", "OK");
            return;
        }

        GameObject body = player.gameObject;
        Ensure<PlayerStateMachine>(body);
        Ensure<LedgeGrab>(body);
        Ensure<PlayerInteractor>(body);
        WeaponManager manager = Ensure<WeaponManager>(body);

        Camera cam = FindPlayerCamera(player);
        if (cam != null)
        {
            Ensure<FirstPersonCameraEffects>(cam.gameObject);

            Transform holder = cam.transform.Find("WeaponHolder");
            if (holder == null)
            {
                holder = new GameObject("WeaponHolder").transform;
                Undo.RegisterCreatedObjectUndo(holder.gameObject, "Create WeaponHolder");
                Undo.SetTransformParent(holder, cam.transform, "Create WeaponHolder");
                holder.localPosition = Vector3.zero;
                holder.localRotation = Quaternion.identity;
                holder.localScale = Vector3.one;
            }

            var so = new SerializedObject(manager);
            so.FindProperty("weaponHolder").objectReferenceValue = holder;
            so.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("[FPS] Couldn't find the player camera; WeaponHolder will be created at runtime.");
        }

        EditorSceneManager.MarkSceneDirty(body.scene);
        Selection.activeGameObject = body;
        Debug.Log("[FPS] Player set up: PlayerStateMachine, LedgeGrab, PlayerInteractor, WeaponManager, " +
                  "FirstPersonCameraEffects and WeaponHolder. Save the scene.", body);
    }

    // ------------------------------------------------------------------ Example weapons

    [MenuItem("Tools/FPS/Create Example Weapons (placeholders)", priority = 20)]
    public static void CreateExamples()
    {
        if (!AssetDatabase.IsValidFolder(ExamplesFolder)) AssetDatabase.CreateFolder("Assets", "FPS Examples");

        WeaponData rifle = CreateWeapon("Rifle", "rifle", WeaponSlot.Primary, FireMode.FullAuto,
                                        new Vector3(0.06f, 0.09f, 0.6f), new Vector3(0.16f, -0.17f, 0.42f), d =>
        {
            d.fireRate = 650f; d.damage = 22f; d.magazineSize = 30; d.maxReserveAmmo = 210; d.startingReserveAmmo = 120;
            d.reloadTime = 2.1f; d.reloadEmptyTime = 2.6f; d.aimFov = 48f; d.hipSpread = 1.6f; d.aimSpread = 0.08f;
        });
        WeaponData pistol = CreateWeapon("Pistol", "pistol", WeaponSlot.Secondary, FireMode.SemiAuto,
                                         new Vector3(0.04f, 0.1f, 0.22f), new Vector3(0.14f, -0.15f, 0.36f), d =>
        {
            d.fireRate = 450f; d.damage = 30f; d.magazineSize = 12; d.maxReserveAmmo = 96; d.startingReserveAmmo = 48;
            d.reloadTime = 1.4f; d.reloadEmptyTime = 1.7f; d.aimFov = 55f; d.hipSpread = 1.2f; d.aimSpread = 0.15f;
            d.cameraKickPitch = 35f; d.viewRecoilPitch = 0.6f;
        });
        WeaponData shotgun = CreateWeapon("Shotgun", "shotgun", WeaponSlot.Primary, FireMode.SemiAuto,
                                          new Vector3(0.07f, 0.09f, 0.7f), new Vector3(0.16f, -0.18f, 0.42f), d =>
        {
            d.fireRate = 80f; d.damage = 12f; d.pelletsPerShot = 9; d.hipSpread = 5f; d.aimSpread = 3.5f; d.range = 40f;
            d.magazineSize = 6; d.maxReserveAmmo = 36; d.startingReserveAmmo = 18; d.reloadTime = 2.8f; d.reloadEmptyTime = 3.2f;
            d.cameraKickPitch = 80f; d.viewRecoilPitch = 2f; d.weaponKickBack = 0.06f; d.weaponKickPitch = 5f; d.impactForce = 3f;
        });
        GrenadeData frag = CreateGrenade();

        string dbPath = $"{ExamplesFolder}/ExampleWeaponDatabase.asset";
        var database = AssetDatabase.LoadAssetAtPath<WeaponDatabase>(dbPath);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<WeaponDatabase>();
            AssetDatabase.CreateAsset(database, dbPath);
        }
        var dbSo = new SerializedObject(database);
        SerializedProperty list = dbSo.FindProperty("weapons");
        list.ClearArray();
        foreach (WeaponData w in new[] { rifle, pistol, shotgun })
        {
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = w;
        }
        dbSo.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();

        WeaponManager manager = Object.FindAnyObjectByType<WeaponManager>();
        if (manager != null)
        {
            var so = new SerializedObject(manager);
            so.FindProperty("database").objectReferenceValue = database;
            so.FindProperty("grenadeType").objectReferenceValue = frag;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }

        if (manager != null && EditorUtility.DisplayDialog("FPS Examples",
                "Example weapons created in 'Assets/FPS Examples'.\n\nPlace pickups and a shootable target in front of the player?",
                "Place them", "No"))
        {
            PlaceTestObjects(manager.transform, rifle, pistol, shotgun, frag);
        }

        Selection.activeObject = database;
        EditorGUIUtility.PingObject(database);
    }

    private static WeaponData CreateWeapon(string displayName, string id, WeaponSlot slot, FireMode mode,
                                           Vector3 gunSize, Vector3 gunPosition, System.Action<WeaponData> configure)
    {
        string dataPath = $"{ExamplesFolder}/{displayName}.asset";
        var data = AssetDatabase.LoadAssetAtPath<WeaponData>(dataPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<WeaponData>();
            AssetDatabase.CreateAsset(data, dataPath);
        }
        data.id = id;
        data.displayName = displayName;
        data.slot = slot;
        data.fireMode = mode;
        data.syncTimingsWithAnimator = false; // placeholders have no animations
        configure(data);

        // ---- View model: a box "gun" in front of the camera + muzzle point ----
        var root = new GameObject($"VM_{displayName}");
        WeaponViewModel vm = root.AddComponent<WeaponViewModel>();
        Primitive(PrimitiveType.Cube, "Gun", root.transform, gunPosition, gunSize, false);
        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(root.transform, false);
        muzzle.localPosition = gunPosition + Vector3.forward * (gunSize.z * 0.5f + 0.02f);
        var vmSo = new SerializedObject(vm);
        vmSo.FindProperty("muzzle").objectReferenceValue = muzzle;
        vmSo.ApplyModifiedPropertiesWithoutUndo();
        WeaponViewModel vmPrefab = PrefabUtility.SaveAsPrefabAsset(root, $"{ExamplesFolder}/VM_{displayName}.prefab")
                                                .GetComponent<WeaponViewModel>();
        Object.DestroyImmediate(root);

        // ---- Pickup: physical box with WeaponPickup ----
        var pickupRoot = new GameObject($"Pickup_{displayName}");
        Primitive(PrimitiveType.Cube, "Model", pickupRoot.transform, Vector3.zero, gunSize * 1.4f, true);
        var rb = pickupRoot.AddComponent<Rigidbody>();
        rb.mass = 3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        WeaponPickup pickup = pickupRoot.AddComponent<WeaponPickup>();
        var pSo = new SerializedObject(pickup);
        pSo.FindProperty("weapon").objectReferenceValue = data;
        pSo.ApplyModifiedPropertiesWithoutUndo();
        WeaponPickup pickupPrefab = PrefabUtility.SaveAsPrefabAsset(pickupRoot, $"{ExamplesFolder}/Pickup_{displayName}.prefab")
                                                 .GetComponent<WeaponPickup>();
        Object.DestroyImmediate(pickupRoot);

        data.viewModelPrefab = vmPrefab;
        data.pickupPrefab = pickupPrefab;
        EditorUtility.SetDirty(data);
        return data;
    }

    private static GrenadeData CreateGrenade()
    {
        var root = new GameObject("Grenade_Frag");
        Primitive(PrimitiveType.Sphere, "Model", root.transform, Vector3.zero, Vector3.one * 0.12f, true);
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 0.4f;
        rb.angularDamping = 1f;
        root.AddComponent<Grenade>();
        Grenade projectile = PrefabUtility.SaveAsPrefabAsset(root, $"{ExamplesFolder}/Grenade_Frag.prefab").GetComponent<Grenade>();
        Object.DestroyImmediate(root);

        string path = $"{ExamplesFolder}/FragGrenade.asset";
        var data = AssetDatabase.LoadAssetAtPath<GrenadeData>(path);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<GrenadeData>();
            AssetDatabase.CreateAsset(data, path);
        }
        data.displayName = "Frag Grenade";
        data.projectilePrefab = projectile;
        EditorUtility.SetDirty(data);

        var pickupRoot = new GameObject("Pickup_Grenades");
        Primitive(PrimitiveType.Sphere, "Model", pickupRoot.transform, Vector3.zero, Vector3.one * 0.2f, true);
        GrenadePickup pickup = pickupRoot.AddComponent<GrenadePickup>();
        var so = new SerializedObject(pickup);
        so.FindProperty("grenade").objectReferenceValue = data;
        so.FindProperty("amount").intValue = 2;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(pickupRoot, $"{ExamplesFolder}/Pickup_Grenades.prefab");
        Object.DestroyImmediate(pickupRoot);
        return data;
    }

    private static void PlaceTestObjects(Transform playerBody, WeaponData rifle, WeaponData pistol, WeaponData shotgun, GrenadeData frag)
    {
        Vector3 forward = Vector3.ProjectOnPlane(playerBody.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 basePos = playerBody.position + forward * 3f;

        var group = new GameObject("FPS Test Objects");
        Undo.RegisterCreatedObjectUndo(group, "Place FPS Test Objects");

        Place(rifle.pickupPrefab.gameObject, basePos - right * 1.2f, group.transform);
        Place(pistol.pickupPrefab.gameObject, basePos, group.transform);
        Place(shotgun.pickupPrefab.gameObject, basePos + right * 1.2f, group.transform);
        var grenades = AssetDatabase.LoadAssetAtPath<GameObject>($"{ExamplesFolder}/Pickup_Grenades.prefab");
        Place(grenades, basePos + right * 2.4f, group.transform);

        for (int i = 0; i < 3; i++)
        {
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = $"Target {i + 1}";
            target.transform.SetParent(group.transform);
            target.transform.position = basePos + forward * (6f + i * 2f) + right * (i - 1) * 2f + Vector3.up * 0.5f;
            target.AddComponent<Rigidbody>();
            target.AddComponent<Health>();
        }

        EditorSceneManager.MarkSceneDirty(group.scene);
    }

    private static void Place(GameObject prefab, Vector3 position, Transform parent)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.position = position + Vector3.up * 0.3f;
    }

    // ------------------------------------------------------------------ Helpers

    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    private static Camera FindPlayerCamera(PlayerController player)
    {
        var so = new SerializedObject(player);
        var holder = so.FindProperty("playerCamera").objectReferenceValue as GameObject;
        return holder != null ? holder.GetComponentInChildren<Camera>(true) : null;
    }
}
