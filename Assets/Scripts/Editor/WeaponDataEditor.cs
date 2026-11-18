using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// WeaponData inspector: reads equip/reload/fire/... timings from the view model's Animator clips,
/// so gameplay timings always match your animations.
/// </summary>
[CustomEditor(typeof(WeaponData))]
public class WeaponDataEditor : Editor
{
    private string lastReport;

    private void OnEnable()
    {
        var data = (WeaponData)target;
        if (data.syncTimingsWithAnimator) lastReport = WeaponTimingSync.Sync(data);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Sync Timings From Animator Clips"))
            lastReport = WeaponTimingSync.Sync((WeaponData)target);

        if (!string.IsNullOrEmpty(lastReport))
            EditorGUILayout.HelpBox(lastReport, MessageType.Info);
    }
}

public static class WeaponTimingSync
{
    /// <summary>Returns a short report of what was found.</summary>
    public static string Sync(WeaponData data)
    {
        if (data.viewModelPrefab == null) return "Assign a view model prefab to sync timings.";

        Animator animator = data.viewModelPrefab.Animator != null
            ? data.viewModelPrefab.Animator
            : data.viewModelPrefab.GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController runtime = animator != null ? animator.runtimeAnimatorController : null;
        if (runtime == null) return "The view model has no Animator Controller. Timings are used as typed.";

        var overrides = runtime as AnimatorOverrideController;
        var controller = (overrides != null ? overrides.runtimeAnimatorController : runtime) as AnimatorController;
        if (controller == null) return "Unsupported Animator Controller.";

        Undo.RecordObject(data, "Sync Weapon Timings");
        bool changed = false;
        int found = 0;

        Set(ref data.equipTime, Length(controller, overrides, WeaponAnimStates.Equip), ref changed, ref found);
        Set(ref data.holsterTime, Length(controller, overrides, WeaponAnimStates.Holster), ref changed, ref found);
        Set(ref data.reloadTime, Length(controller, overrides, WeaponAnimStates.Reload), ref changed, ref found);

        float reloadEmpty = Length(controller, overrides, WeaponAnimStates.ReloadEmpty);
        Set(ref data.reloadEmptyTime, reloadEmpty > 0f ? reloadEmpty : Length(controller, overrides, WeaponAnimStates.Reload),
            ref changed, ref found);

        Set(ref data.fireAnimationLength, Length(controller, overrides, WeaponAnimStates.Fire), ref changed, ref found);
        Set(ref data.grenadeThrowTime, Length(controller, overrides, WeaponAnimStates.GrenadeThrow), ref changed, ref found);
        Set(ref data.grenadeReleaseTime, EventTime(controller, overrides, WeaponAnimStates.GrenadeThrow, nameof(WeaponAnimationEvents.ReleaseGrenade)),
            ref changed, ref found);

        if (changed) EditorUtility.SetDirty(data);
        return found > 0
            ? $"Timings synced from '{controller.name}' ({found} values read from clips)."
            : $"No clips found in '{controller.name}' states yet. Timings are used as typed.";
    }

    private static void Set(ref float field, float value, ref bool changed, ref int found)
    {
        if (value <= 0f) return;
        found++;
        if (Mathf.Abs(field - value) > 0.0001f)
        {
            field = value;
            changed = true;
        }
    }

    private static float Length(AnimatorController controller, AnimatorOverrideController overrides, string stateName)
    {
        AnimatorState state = FindState(controller, stateName);
        AnimationClip clip = ClipOf(state, overrides);
        return clip == null ? -1f : clip.length / Speed(state);
    }

    private static float EventTime(AnimatorController controller, AnimatorOverrideController overrides, string stateName, string function)
    {
        AnimatorState state = FindState(controller, stateName);
        AnimationClip clip = ClipOf(state, overrides);
        if (clip == null) return -1f;
        foreach (AnimationEvent e in AnimationUtility.GetAnimationEvents(clip))
            if (e.functionName == function) return e.time / Speed(state);
        return -1f;
    }

    private static AnimationClip ClipOf(AnimatorState state, AnimatorOverrideController overrides)
    {
        if (state == null || !(state.motion is AnimationClip clip)) return null;
        if (overrides != null && overrides[clip] != null) clip = overrides[clip];
        return clip;
    }

    private static float Speed(AnimatorState state) => Mathf.Abs(state.speed) < 1e-4f ? 1f : Mathf.Abs(state.speed);

    private static AnimatorState FindState(AnimatorController controller, string name)
    {
        return controller.layers.Length == 0 ? null : FindState(controller.layers[0].stateMachine, name);
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
            if (child.state.name == name) return child.state;
        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
        {
            AnimatorState s = FindState(sub.stateMachine, name);
            if (s != null) return s;
        }
        return null;
    }
}
