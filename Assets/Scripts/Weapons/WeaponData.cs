using UnityEngine;

/// <summary>Primary = big weapon (rifles, shotguns...), Secondary = small weapon (pistols...).</summary>
public enum WeaponSlot { Primary = 0, Secondary = 1 }

public enum FireMode { SemiAuto, FullAuto, Burst }

/// <summary>
/// Everything that defines a weapon. Create with Assets > Create > FPS > Weapon Data,
/// then add it to a WeaponDatabase.
/// </summary>
[CreateAssetMenu(menuName = "FPS/Weapon Data", fileName = "NewWeapon", order = 0)]
public class WeaponData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Unique key used by the WeaponDatabase, e.g. \"rifle_ak\".")]
    public string id;
    public string displayName = "Weapon";
    [Tooltip("Primary = big weapon, Secondary = small weapon. You carry one of each.")]
    public WeaponSlot slot = WeaponSlot.Primary;

    [Header("Prefabs")]
    [Tooltip("First-person prefab (arms + gun + camera bone) with a WeaponViewModel component.")]
    public WeaponViewModel viewModelPrefab;
    [Tooltip("World prefab spawned when the weapon is dropped. Needs a WeaponPickup component and a collider.")]
    public WeaponPickup pickupPrefab;

    [Header("Firing")]
    public FireMode fireMode = FireMode.SemiAuto;
    [Tooltip("Rounds per minute.")]
    [Min(1f)] public float fireRate = 600f;
    [Min(2)] public int burstCount = 3;
    [Tooltip("Pause after a burst (seconds).")]
    public float burstCooldown = 0.25f;
    [Tooltip("A trigger press this long before the gun is ready still fires. Makes semi-auto feel responsive.")]
    public float triggerBufferTime = 0.1f;
    public bool canFireWhileSprinting = true;

    [Header("Hitscan")]
    public float damage = 25f;
    public float range = 200f;
    [Tooltip("Rays per shot (1 for rifles, 8+ for shotguns).")]
    [Min(1)] public int pelletsPerShot = 1;
    [Tooltip("Damage multiplier over distance (X: 0 = muzzle, 1 = max range).")]
    public AnimationCurve damageFalloff = AnimationCurve.Constant(0f, 1f, 1f);
    [Tooltip("Impulse applied to rigidbodies that are hit.")]
    public float impactForce = 5f;
    public LayerMask hitMask = ~0;

    [Header("Spread (degrees)")]
    public float hipSpread = 1.5f;
    public float aimSpread = 0.1f;
    public float movingSpreadMultiplier = 1.5f;
    public float airborneSpreadMultiplier = 2.5f;

    [Header("Ammo")]
    [Min(1)] public int magazineSize = 30;
    public int maxReserveAmmo = 180;
    public int startingReserveAmmo = 90;
    public bool infiniteReserveAmmo;
    public bool autoReloadWhenEmpty = true;
    [Tooltip("Pressing fire during a reload cancels it (only if there are rounds left).")]
    public bool fireCancelsReload = true;

    [Header("Timings (seconds)")]
    [Tooltip("Editor: when you open this asset, timings are read from the view model's Animator clips (by state name).")]
    public bool syncTimingsWithAnimator = true;
    public float equipTime = 0.4f;
    public float holsterTime = 0.3f;
    public float reloadTime = 2f;
    public float reloadEmptyTime = 2.4f;
    [Tooltip("How long the Fire animation blocks Idle/Walk animations.")]
    public float fireAnimationLength = 0.2f;
    public float grenadeThrowTime = 0.8f;
    [Tooltip("When the grenade leaves the hand. A 'ReleaseGrenade' animation event overrides this.")]
    public float grenadeReleaseTime = 0.35f;

    [Header("Aiming")]
    [Tooltip("Camera field of view while aiming.")]
    [Range(10f, 120f)] public float aimFov = 50f;
    [Tooltip("How fast aiming in/out blends (1/seconds).")]
    public float aimSpeed = 10f;
    [Range(0f, 1f)] public float aimSwayMultiplier = 0.3f;
    [Range(0f, 1f)] public float aimBobMultiplier = 0.25f;
    [Tooltip("Player move speed multiplier while aiming. Aiming also blocks sprint.")]
    [Range(0.1f, 1f)] public float aimMoveSpeedMultiplier = 0.65f;

    [Header("Recoil")]
    [Tooltip("Visual camera kick per shot (spring impulse, deg/s). Doesn't move your aim.")]
    public float cameraKickPitch = 25f;
    public float cameraKickYaw = 8f;
    public float cameraKickRoll = 6f;
    [Tooltip("Real aim climb per shot (degrees). The player has to compensate.")]
    public float viewRecoilPitch = 0.25f;
    public float viewRecoilYaw = 0.08f;
    [Range(0f, 1f)] public float aimRecoilMultiplier = 0.6f;
    [Tooltip("Procedural view model kick per shot (meters back / degrees up). Adds to the Fire animation.")]
    public float weaponKickBack = 0.02f;
    public float weaponKickPitch = 1.5f;

    [Header("Sway")]
    public float swayMultiplier = 1f;

    [Header("Audio")]
    public AudioClip[] fireSounds;
    [Range(0f, 1f)] public float fireVolume = 1f;
    public float firePitchVariation = 0.05f;
    public AudioClip dryFireSound;
    public AudioClip equipSound;
    [Tooltip("Optional. For precise reload sounds use 'PlaySound' animation events instead.")]
    public AudioClip reloadSound;

    [Header("Effects")]
    [Tooltip("Spawned at the muzzle on each shot (e.g. a particle system).")]
    public GameObject muzzleFlashPrefab;
    public float muzzleFlashLifetime = 1f;
    [Tooltip("Spawned where bullets hit, facing the surface normal.")]
    public GameObject impactEffectPrefab;
    public float impactEffectLifetime = 10f;

    [Header("Animation Blending")]
    public float locomotionCrossFade = 0.15f;
    public float actionCrossFade = 0.05f;

    public float SecondsBetweenShots => 60f / Mathf.Max(1f, fireRate);

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(id)) id = name;
        startingReserveAmmo = Mathf.Clamp(startingReserveAmmo, 0, Mathf.Max(0, maxReserveAmmo));
    }
}
