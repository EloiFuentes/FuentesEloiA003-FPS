using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Creates Animator Controllers that already contain every default state name, with no transitions.
/// Just drop your clips into the states.
/// </summary>
public static class FPSAnimatorTemplates
{
    [MenuItem("Assets/Create/FPS/Weapon Animator Controller", priority = 10)]
    private static void CreateWeaponController()
    {
        Create("WeaponAnimator", WeaponAnimStates.All, WeaponAnimStates.Idle);
    }

    [MenuItem("Assets/Create/FPS/Player Animator Controller", priority = 11)]
    private static void CreatePlayerController()
    {
        Create("PlayerAnimator", PlayerAnimStates.All, nameof(PlayerLocomotionState.Idle));
    }

    [MenuItem("Assets/FPS/Add Missing Weapon States To Controller")]
    private static void AddMissingWeaponStates()
    {
        var controller = (AnimatorController)Selection.activeObject;
        int added = AddStates(controller, WeaponAnimStates.All, WeaponAnimStates.Idle);
        Debug.Log($"[FPS] Added {added} missing weapon states to '{controller.name}'.", controller);
    }

    [MenuItem("Assets/FPS/Add Missing Weapon States To Controller", true)]
    private static bool AddMissingWeaponStatesValidate() => Selection.activeObject is AnimatorController;

    public static AnimatorController Create(string fileName, string[] states, string defaultState, string folder = null)
    {
        folder ??= SelectedFolder();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.controller");
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        AddStates(controller, states, defaultState);

        AssetDatabase.SaveAssets();
        Selection.activeObject = controller;
        EditorGUIUtility.PingObject(controller);
        return controller;
    }

    private static int AddStates(AnimatorController controller, string[] states, string defaultState)
    {
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        int added = 0;
        for (int i = 0; i < states.Length; i++)
        {
            AnimatorState state = Find(machine, states[i]);
            if (state == null)
            {
                Vector3 position = new Vector3(300f + (i % 4) * 230f, (i / 4) * 70f, 0f);
                state = machine.AddState(states[i], position);
                added++;
            }
            if (states[i] == defaultState) machine.defaultState = state;
        }
        EditorUtility.SetDirty(controller);
        return added;
    }

    private static AnimatorState Find(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
            if (child.state.name == name) return child.state;
        return null;
    }

    private static string SelectedFolder()
    {
        string path = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : string.Empty;
        if (string.IsNullOrEmpty(path)) return "Assets";
        return AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path)?.Replace('\\', '/');
    }
}
