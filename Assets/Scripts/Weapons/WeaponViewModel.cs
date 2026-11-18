using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Root component of a first-person weapon prefab (arms + gun + camera bone).
///
/// Camera animation (Modern Warfare style): animate a bone/empty named e.g. "Camera" in Blender together
/// with the arms (tilt and shake in reloads...) and assign it to Camera Bone. Its motion relative
/// to its pose in the prefab is applied to the real camera every frame, while the arms stay put
/// on screen exactly as you animated them.
/// Keep the camera bone at its neutral pose in the prefab, at the camera position (prefab origin).
/// </summary>
[DisallowMultipleComponent]
public class WeaponViewModel : MonoBehaviour
{
    [Tooltip("Animator with the weapon states (Idle, Fire, Reload...). Found automatically if empty.")]
    [SerializeField] private Animator animator;
    [Tooltip("Where muzzle flashes spawn. Uses this transform if empty.")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Animated camera bone from Blender (optional).")]
    [SerializeField] private Transform cameraBone;
    [Tooltip("Optional point grenades are thrown from (e.g. the hand).")]
    [SerializeField] private Transform grenadeSpawnPoint;
    [Tooltip("Scales the camera bone animation (0 = off, 1 = as animated).")]
    [SerializeField, Range(0f, 2f)] private float cameraAnimationWeight = 1f;
    [Tooltip("First-person models usually shouldn't cast shadows.")]
    [SerializeField] private bool disableShadows = true;

    /// <summary>Raised by 'PlaySound' animation events.</summary>
    public event Action<AudioClip> SoundRequested;
    /// <summary>Raised by 'ReleaseGrenade' animation events.</summary>
    public event Action GrenadeReleaseRequested;

    public Animator Animator => animator;
    public Transform Muzzle => muzzle != null ? muzzle : transform;
    public Transform GrenadeSpawnPoint => grenadeSpawnPoint;
    public Transform CameraBone => cameraBone;

    private Vector3 restBonePosition;
    private Quaternion restBoneRotation = Quaternion.identity;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            // Keep animating when culled, so the camera bone never freezes.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (animator.GetComponent<WeaponAnimationEvents>() == null)
                animator.gameObject.AddComponent<WeaponAnimationEvents>();
        }

        if (cameraBone != null)
        {
            restBonePosition = transform.InverseTransformDirection(cameraBone.position - transform.position);
            restBoneRotation = Quaternion.Inverse(transform.rotation) * cameraBone.rotation;
        }

        if (disableShadows)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }

    /// <summary>Camera bone motion relative to its rest pose, in this prefab's space.</summary>
    public bool TryGetCameraAnimation(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (cameraBone == null || cameraAnimationWeight <= 0f || !isActiveAndEnabled) return false;

        Vector3 currentPosition = transform.InverseTransformDirection(cameraBone.position - transform.position);
        Quaternion currentRotation = Quaternion.Inverse(transform.rotation) * cameraBone.rotation;

        position = (currentPosition - restBonePosition) * cameraAnimationWeight;
        rotation = Quaternion.SlerpUnclamped(Quaternion.identity,
                                             currentRotation * Quaternion.Inverse(restBoneRotation),
                                             cameraAnimationWeight);
        return true;
    }

    internal void RaiseSound(AudioClip clip) => SoundRequested?.Invoke(clip);
    internal void RaiseGrenadeRelease() => GrenadeReleaseRequested?.Invoke();
}
