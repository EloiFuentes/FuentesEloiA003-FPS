using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's weapons: one Primary (big) and one Secondary (small) slot, grenades,
/// switching, pickup/drop, weapon sway and the camera animation from weapon camera bones.
///
/// Typical use from code:
///   weapons.GiveWeapon("rifle_ak");          // by id from the WeaponDatabase
///   weapons.EquipSlot(WeaponSlot.Secondary);
///   weapons.SetWeaponsEnabled(false);         // e.g. in a safe zone / cutscene
/// </summary>
[DefaultExecutionOrder(150)] // after PlayerController/PlayerStateMachine; LateUpdate after FirstPersonCameraEffects
[RequireComponent(typeof(PlayerController))]
public class WeaponManager : MonoBehaviour, ICameraAnimationSource
{
    [Header("Setup")]
    [SerializeField] private WeaponDatabase database;
    [Tooltip("Parent for first-person weapons. Created under the camera if empty.")]
    [SerializeField] private Transform weaponHolder;
    [Tooltip("2D audio source for weapon sounds. Created on the camera if empty.")]
    [SerializeField] private AudioSource weaponAudio;
    [Tooltip("Camera near clip plane, so arms close to the camera aren't cut off. 0 = leave unchanged.")]
    [SerializeField] private float cameraNearClip = 0.01f;

    [Header("Loadout")]
    [Tooltip("Weapon ids (from the database) given at start.")]
    [SerializeField] private List<string> startingWeapons = new List<string>();
    [SerializeField] private bool weaponsEnabled = true;

    [Header("Grenades")]
    [SerializeField] private GrenadeData grenadeType;
    [SerializeField] private int startingGrenades;

    [Header("Ledge Grab")]
    [Tooltip("Holster the weapon while mantling / hanging on a ledge, and draw it again afterwards.")]
    [SerializeField] private bool lowerWeaponOnLedgeGrab = true;

    [Header("Switching")]
    [Tooltip("Minimum time between scroll-wheel switches.")]
    [SerializeField] private float scrollSwitchCooldown = 0.2f;

    [Header("Dropping")]
    [SerializeField] private float dropForwardSpeed = 4f;
    [SerializeField] private float dropUpSpeed = 2f;
    [SerializeField] private float dropSpin = 6f;

    [Header("Sway")]
    [SerializeField] private WeaponSway sway = new WeaponSway();

    // ---- Events (for UI later) ----
    public event Action<Weapon> ActiveWeaponChanged;
    public event Action<Weapon> AmmoChanged;
    public event Action<int> GrenadesChanged;

    // ---- State ----
    public Weapon ActiveWeapon { get; private set; }
    public int Grenades { get; private set; }
    public GrenadeData GrenadeType => grenadeType;
    public WeaponDatabase Database => database;
    public bool WeaponsEnabled => weaponsEnabled;
    /// <summary>The weapon is temporarily put away (e.g. during a ledge grab).</summary>
    public bool WeaponLowered => weaponLowered;

    // ---- Services used by Weapon ----
    public PlayerController Player { get; private set; }
    public GameInputs Inputs { get; private set; }
    public Transform ViewTransform => cam != null ? cam.transform : transform;
    public PlayerLocomotionState LocomotionState => locomotion != null ? locomotion.State : PlayerLocomotionState.Idle;
    public bool IsPlayerMoving => locomotion != null ? locomotion.IsMoving : Player.HorizontalSpeed > 0.5f;
    /// <summary>Weapons can't shoot/reload/aim (climbing, disabled, controller busy...).</summary>
    public bool IsActionBlocked => !weaponsEnabled || Player.IsExternallyControlled || (locomotion != null && locomotion.IsBusy);
    public bool CanThrowGrenade => Grenades > 0 && grenadeType != null && grenadeType.projectilePrefab != null && !IsActionBlocked;

    private readonly Weapon[] slots = new Weapon[2];
    private PlayerStateMachine locomotion;
    private LedgeGrab ledgeGrab;
    private bool weaponLowered;
    private FirstPersonCameraEffects effects;
    private Camera cam;

    private WeaponSlot? pendingSlot;
    private float nextScrollTime;
    private float nextUnarmedThrowTime;

    // ---------------- Unity ----------------

    private void Awake()
    {
        Player = GetComponent<PlayerController>();
        Inputs = GetComponent<GameInputs>();
        locomotion = GetComponent<PlayerStateMachine>();
        ledgeGrab = GetComponent<LedgeGrab>();
    }

    private void OnEnable()
    {
        if (ledgeGrab == null) return;
        ledgeGrab.MantleStarted += OnMantleStarted;
        ledgeGrab.LedgeGrabbed += OnLedgeGrabbed;
        ledgeGrab.MantleFinished += OnLedgeEnded;
        ledgeGrab.LedgeReleased += OnLedgeEnded;
    }

    private void OnDisable()
    {
        if (ledgeGrab == null) return;
        ledgeGrab.MantleStarted -= OnMantleStarted;
        ledgeGrab.LedgeGrabbed -= OnLedgeGrabbed;
        ledgeGrab.MantleFinished -= OnLedgeEnded;
        ledgeGrab.LedgeReleased -= OnLedgeEnded;
    }

    private void OnMantleStarted(float height) { if (lowerWeaponOnLedgeGrab) SetWeaponLowered(true); }
    private void OnLedgeGrabbed() { if (lowerWeaponOnLedgeGrab) SetWeaponLowered(true); }
    private void OnLedgeEnded() { if (lowerWeaponOnLedgeGrab) SetWeaponLowered(false); }

    private void Start()
    {
        cam = Player.PlayerCam;
        if (cam == null)
        {
            Debug.LogError("[WeaponManager] The PlayerController has no camera.", this);
            enabled = false;
            return;
        }
        if (cameraNearClip > 0f) cam.nearClipPlane = cameraNearClip;

        effects = cam.GetComponent<FirstPersonCameraEffects>();
        if (effects != null) effects.CameraAnimationSource = this;

        if (weaponHolder == null)
        {
            weaponHolder = new GameObject("WeaponHolder").transform;
            weaponHolder.SetParent(cam.transform, false);
        }
        if (weaponAudio == null)
        {
            weaponAudio = cam.gameObject.AddComponent<AudioSource>();
            weaponAudio.playOnAwake = false;
            weaponAudio.spatialBlend = 0f;
        }

        foreach (string id in startingWeapons)
            GiveWeapon(id, false);
        if (ActiveWeapon == null) EquipFirstAvailable();

        Grenades = grenadeType != null ? Mathf.Clamp(startingGrenades, 0, grenadeType.maxCarried) : 0;
        GrenadesChanged?.Invoke(Grenades);
    }

    private void Update()
    {
        WeaponInput input = ReadInput();

        HandleSwitching();
        HandlePendingSwitch();

        // Throw grenades even with no weapon in hands.
        if (input.GrenadePressed && (ActiveWeapon == null || !ActiveWeapon.IsVisible)
            && CanThrowGrenade && Time.time >= nextUnarmedThrowTime)
        {
            nextUnarmedThrowTime = Time.time + grenadeType.unarmedThrowCooldown;
            ReleaseGrenade(null);
        }

        ActiveWeapon?.Tick(input, Time.deltaTime);

        // Aiming: FOV, no sprint, slower movement.
        float aim = ActiveWeapon != null ? ActiveWeapon.AimAmount : 0f;
        if (effects != null) effects.SetAim(aim, ActiveWeapon != null ? ActiveWeapon.Data.aimFov : 0f);
        Player.SprintBlocked = aim > 0.1f;
        Player.SpeedMultiplier = ActiveWeapon != null ? Mathf.Lerp(1f, ActiveWeapon.Data.aimMoveSpeedMultiplier, aim) : 1f;
    }

    private void LateUpdate()
    {
        if (weaponHolder == null) return;

        WeaponData data = ActiveWeapon?.Data;
        float aim = ActiveWeapon != null ? ActiveWeapon.AimAmount : 0f;

        var context = new WeaponSway.Context
        {
            DeltaTime = Time.deltaTime,
            LookDelta = Inputs.LookDelta,
            LocalVelocity = Player.LocalVelocity,
            WalkSpeed = Player.WalkSpeed,
            SprintSpeed = Player.SprintSpeed,
            StridePhase = Player.StridePhase,
            Grounded = Player.IsGrounded,
            SwayMultiplier = (data != null ? data.swayMultiplier : 1f) * Mathf.Lerp(1f, data != null ? data.aimSwayMultiplier : 1f, aim),
            BobMultiplier = Mathf.Lerp(1f, data != null ? data.aimBobMultiplier : 1f, aim)
        };
        sway.Evaluate(context, out Vector3 swayPosition, out Quaternion swayRotation);

        // The camera moved by the weapon's camera bone this frame: cancel that on the holder so the
        // arms stay exactly where the animator put them while the view shakes/tilts.
        Vector3 animPosition = Vector3.zero;
        Quaternion animRotation = Quaternion.identity;
        if (effects != null)
        {
            animPosition = effects.AppliedAnimationPosition;
            animRotation = effects.AppliedAnimationRotation;
        }
        Quaternion inverse = Quaternion.Inverse(animRotation);
        weaponHolder.localRotation = inverse * swayRotation;
        weaponHolder.localPosition = inverse * (swayPosition - animPosition);
    }

    // ---------------- Public API ----------------

    public Weapon GetWeapon(WeaponSlot slot) => slots[(int)slot];

    /// <summary>Gives a weapon by its database id.</summary>
    public bool GiveWeapon(string id, bool equip = true)
    {
        if (database == null)
        {
            Debug.LogWarning("[WeaponManager] No WeaponDatabase assigned.", this);
            return false;
        }
        WeaponData data = database.Get(id);
        return data != null && GiveWeapon(data, -1, -1, equip);
    }

    /// <summary>
    /// Gives a weapon. If the slot holds a different weapon it is dropped on the floor.
    /// If it's the same weapon, its ammo is added instead. -1 ammo = defaults.
    /// </summary>
    public bool GiveWeapon(WeaponData data, int magazine = -1, int reserve = -1, bool equip = true)
    {
        if (data == null || data.viewModelPrefab == null)
        {
            Debug.LogWarning($"[WeaponManager] Weapon '{(data != null ? data.name : "null")}' has no view model prefab.", this);
            return false;
        }

        Weapon existing = slots[(int)data.slot];
        if (existing != null && existing.Data == data)
        {
            int ammo = (magazine < 0 ? data.magazineSize : magazine) + (reserve < 0 ? data.startingReserveAmmo : reserve);
            return existing.AddReserveAmmo(ammo) > 0;
        }

        bool wasActive = existing != null && existing == ActiveWeapon;
        if (existing != null) DropWeapon(data.slot);

        WeaponViewModel viewModel = Instantiate(data.viewModelPrefab, weaponHolder, false);
        viewModel.name = data.viewModelPrefab.name;
        var weapon = new Weapon(this, data, viewModel, magazine, reserve);
        weapon.AmmoChanged += w => AmmoChanged?.Invoke(w);
        slots[(int)data.slot] = weapon;

        // Create effect copies now so the first shots don't hitch.
        EffectPool.Prewarm(data.muzzleFlashPrefab, 2);
        EffectPool.Prewarm(data.impactEffectPrefab, 8);

        if (equip || wasActive || ActiveWeapon == null) EquipSlot(data.slot);
        return true;
    }

    /// <summary>Switches to the weapon in a slot (holster current, then equip).</summary>
    public void EquipSlot(WeaponSlot slot)
    {
        Weapon target = slots[(int)slot];
        if (target == null) return;

        if (!weaponsEnabled || weaponLowered)
        {
            // Remember the choice for when weapons are enabled / raised again.
            ActiveWeapon?.HideImmediate();
            SetActive(target);
            return;
        }

        // Nothing in hands (or re-selecting the current weapon): equip right away.
        if (ActiveWeapon == null || !ActiveWeapon.IsVisible || ActiveWeapon == target)
        {
            pendingSlot = null;
            if (ActiveWeapon != null && ActiveWeapon != target) ActiveWeapon.HideImmediate();
            SetActive(target);
            target.Equip();
            return;
        }

        // Otherwise holster the current weapon first (see HandlePendingSwitch).
        pendingSlot = slot;
    }

    public void SwitchToNext()
    {
        WeaponSlot current = ActiveWeapon != null ? ActiveWeapon.Data.slot : WeaponSlot.Primary;
        WeaponSlot other = current == WeaponSlot.Primary ? WeaponSlot.Secondary : WeaponSlot.Primary;
        if (slots[(int)other] != null) EquipSlot(other);
    }

    /// <summary>Drops the weapon in a slot as a pickup in front of the player.</summary>
    public bool DropWeapon(WeaponSlot slot)
    {
        Weapon weapon = slots[(int)slot];
        if (weapon == null) return false;

        SpawnDroppedPickup(weapon);

        slots[(int)slot] = null;
        if (weapon.ViewModel != null) Destroy(weapon.ViewModel.gameObject);
        if (weapon == ActiveWeapon)
        {
            pendingSlot = null;
            SetActive(null);
        }
        return true;
    }

    /// <summary>Enable/disable all weapons (holsters the current one).</summary>
    public void SetWeaponsEnabled(bool value)
    {
        if (weaponsEnabled == value) return;
        weaponsEnabled = value;

        if (!value)
        {
            pendingSlot = null;
            ActiveWeapon?.Holster(null);
        }
        else if (weaponLowered)
        {
            // Drawn when the weapon is raised again.
        }
        else if (ActiveWeapon != null)
        {
            ActiveWeapon.Equip();
        }
        else
        {
            EquipFirstAvailable();
        }
    }

    /// <summary>
    /// Temporarily puts the weapon away (true) and draws it again (false), e.g. for a ledge grab.
    /// Independent of SetWeaponsEnabled.
    /// </summary>
    public void SetWeaponLowered(bool value)
    {
        if (weaponLowered == value) return;
        weaponLowered = value;
        if (!weaponsEnabled) return;

        if (value)
        {
            pendingSlot = null;
            ActiveWeapon?.Holster(null);
        }
        else if (ActiveWeapon != null)
        {
            ActiveWeapon.Equip();
        }
        else
        {
            EquipFirstAvailable();
        }
    }

    /// <summary>Adds grenades. A different grenade type replaces the current one. Returns how many were taken.</summary>
    public int AddGrenades(GrenadeData type, int amount)
    {
        if (type == null || amount <= 0) return 0;
        if (type != grenadeType)
        {
            grenadeType = type;
            Grenades = 0;
        }
        int taken = Mathf.Min(amount, type.maxCarried - Grenades);
        if (taken <= 0) return 0;
        Grenades += taken;
        GrenadesChanged?.Invoke(Grenades);
        return taken;
    }

    public bool CanTakeGrenades(GrenadeData type) => type != null && (type != grenadeType || Grenades < type.maxCarried);

    // ---------------- Used by Weapon ----------------

    internal void ReleaseGrenade(Transform spawnPoint)
    {
        if (Grenades <= 0 || grenadeType == null || grenadeType.projectilePrefab == null) return;

        Transform view = ViewTransform;
        Vector3 desired = spawnPoint != null ? spawnPoint.position : view.position + view.forward * 0.4f - view.up * 0.1f;
        Vector3 position = SafeSpawnPosition(view.position, desired);

        Vector3 velocity = view.forward * grenadeType.throwSpeed + Vector3.up * grenadeType.throwUpSpeed
                           + Player.Velocity * grenadeType.inheritPlayerVelocity;

        Grenade grenade = Instantiate(grenadeType.projectilePrefab, position, view.rotation);
        grenade.Launch(velocity, UnityEngine.Random.insideUnitSphere * grenadeType.spin, gameObject, Player.Capsule);

        Grenades--;
        GrenadesChanged?.Invoke(Grenades);
    }

    internal void ApplyRecoil(Weapon weapon, float multiplier)
    {
        WeaponData d = weapon.Data;
        if (effects != null)
        {
            effects.AddRecoil(-d.cameraKickPitch * multiplier,
                              UnityEngine.Random.Range(-1f, 1f) * d.cameraKickYaw * multiplier,
                              UnityEngine.Random.Range(-1f, 1f) * d.cameraKickRoll * multiplier);
        }
        Inputs.AddLookRotation(UnityEngine.Random.Range(-1f, 1f) * d.viewRecoilYaw * multiplier, -d.viewRecoilPitch * multiplier);
        sway.AddKick(d.weaponKickBack * multiplier, d.weaponKickPitch * multiplier);
    }

    internal void SpawnMuzzleFlash(Weapon weapon)
    {
        GameObject prefab = weapon.Data.muzzleFlashPrefab;
        if (prefab == null) return;
        EffectPool.Spawn(prefab, weapon.ViewModel.Muzzle, weapon.Data.muzzleFlashLifetime);
    }

    internal void SpawnImpact(WeaponData data, RaycastHit hit)
    {
        if (data.impactEffectPrefab == null) return;
        EffectPool.Spawn(data.impactEffectPrefab, hit.point + hit.normal * 0.005f, Quaternion.LookRotation(hit.normal),
                         data.impactEffectLifetime);
    }

    internal void PlaySound(AudioClip clip, float volume, float pitchVariation)
    {
        if (clip == null || weaponAudio == null) return;
        weaponAudio.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
        weaponAudio.PlayOneShot(clip, volume);
    }

    internal void PlayRandomSound(AudioClip[] clips, float volume, float pitchVariation)
    {
        if (clips == null || clips.Length == 0) return;
        PlaySound(clips[UnityEngine.Random.Range(0, clips.Length)], volume, pitchVariation);
    }

    // ---------------- ICameraAnimationSource ----------------

    public bool TryGetCameraAnimation(out Vector3 localPosition, out Quaternion localRotation)
    {
        localPosition = Vector3.zero;
        localRotation = Quaternion.identity;
        if (ActiveWeapon == null || !ActiveWeapon.IsVisible || ActiveWeapon.ViewModel == null) return false;
        if (!ActiveWeapon.ViewModel.TryGetCameraAnimation(out Vector3 position, out Quaternion rotation)) return false;

        // The view model's space is the holder's space; convert into camera-local space.
        Transform vm = ActiveWeapon.ViewModel.transform;
        Quaternion toHolder = vm.localRotation;
        localPosition = toHolder * position;
        localRotation = toHolder * rotation * Quaternion.Inverse(toHolder);
        return true;
    }

    // ---------------- Internals ----------------

    private WeaponInput ReadInput()
    {
        var input = new WeaponInput();
        if (Cursor.lockState != CursorLockMode.Locked) return input; // don't shoot while using UI
        input.FirePressed = Inputs.fire != null && Inputs.fire.WasPressedThisFrame();
        input.FireHeld = Inputs.fire != null && Inputs.fire.IsPressed();
        input.AimHeld = Inputs.aim != null && Inputs.aim.IsPressed();
        input.ReloadPressed = Inputs.reload != null && Inputs.reload.WasPressedThisFrame();
        input.GrenadePressed = Inputs.grenade != null && Inputs.grenade.WasPressedThisFrame();
        return input;
    }

    private void HandleSwitching()
    {
        if (!weaponsEnabled || IsActionBlocked) return;

        if (Inputs.primaryWeapon != null && Inputs.primaryWeapon.WasPressedThisFrame()) EquipSlot(WeaponSlot.Primary);
        else if (Inputs.secondaryWeapon != null && Inputs.secondaryWeapon.WasPressedThisFrame()) EquipSlot(WeaponSlot.Secondary);
        else if (Inputs.switchWeapon != null && Time.time >= nextScrollTime
                 && Mathf.Abs(Inputs.switchWeapon.ReadValue<float>()) > 0.01f)
        {
            nextScrollTime = Time.time + scrollSwitchCooldown;
            SwitchToNext();
        }
    }

    private void HandlePendingSwitch()
    {
        if (pendingSlot == null) return;

        Weapon target = slots[(int)pendingSlot.Value];
        if (target == null)
        {
            pendingSlot = null;
            return;
        }

        if (ActiveWeapon != null && ActiveWeapon.IsVisible)
        {
            // Let a grenade throw finish; otherwise start holstering (once).
            if (ActiveWeapon.State != Weapon.StateId.Holstering && ActiveWeapon.State != Weapon.StateId.ThrowingGrenade)
                ActiveWeapon.Holster(null);
            return;
        }

        pendingSlot = null;
        SetActive(target);
        target.Equip();
    }

    private void EquipFirstAvailable()
    {
        if (slots[(int)WeaponSlot.Primary] != null) EquipSlot(WeaponSlot.Primary);
        else if (slots[(int)WeaponSlot.Secondary] != null) EquipSlot(WeaponSlot.Secondary);
    }

    private void SetActive(Weapon weapon)
    {
        if (ActiveWeapon == weapon) return;
        ActiveWeapon = weapon;
        sway.Reset();
        ActiveWeaponChanged?.Invoke(weapon);
    }

    private void SpawnDroppedPickup(Weapon weapon)
    {
        WeaponPickup prefab = weapon.Data.pickupPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[WeaponManager] '{weapon.Data.name}' has no pickup prefab, so it can't be dropped on the floor.", this);
            return;
        }

        Transform view = ViewTransform;
        Vector3 position = SafeSpawnPosition(view.position, view.position + view.forward * 0.7f - Vector3.up * 0.2f);
        WeaponPickup pickup = Instantiate(prefab, position, Quaternion.LookRotation(Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized + Vector3.up * 0.01f));
        pickup.SetWeapon(weapon.Data);
        pickup.SetAmmo(weapon.Magazine, weapon.Reserve);
        pickup.IgnorePlayer(Player.Capsule);

        if (pickup.TryGetComponent(out Rigidbody body) && !body.isKinematic)
        {
            body.linearVelocity = Player.Velocity + view.forward * dropForwardSpeed + Vector3.up * dropUpSpeed;
            body.angularVelocity = UnityEngine.Random.insideUnitSphere * dropSpin;
        }
    }

    /// <summary>Moves a spawn point back toward the camera if it would be inside a wall.</summary>
    private Vector3 SafeSpawnPosition(Vector3 from, Vector3 desired)
    {
        Vector3 delta = desired - from;
        float distance = delta.magnitude;
        if (distance < 1e-4f) return desired;
        if (Hitscan.Cast(from, delta / distance, distance + 0.1f, ~0, Player, out RaycastHit hit))
            return from + delta / distance * Mathf.Max(0f, hit.distance - 0.15f);
        return desired;
    }
}
