using UnityEngine;

/// <summary>
/// Provides an animated camera offset (e.g. a camera bone animated in Blender) that
/// FirstPersonCameraEffects layers on top of head bob, kicks and shake.
/// </summary>
public interface ICameraAnimationSource
{
    /// <summary>Offset relative to the camera's rest pose, in camera-parent space.</summary>
    bool TryGetCameraAnimation(out Vector3 localPosition, out Quaternion localRotation);
}
