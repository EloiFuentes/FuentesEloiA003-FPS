using System;
using UnityEngine;

/// <summary>Input for one frame, gathered by the WeaponManager.</summary>
public struct WeaponInput
{
    public bool FirePressed;
    public bool FireHeld;
    public bool AimHeld;
    public bool ReloadPressed;
    public bool GrenadePressed;
}

/// <summary>
/// Runtime instance of a weapon the player carries: ammo, its first-person model and its state machine.
/// Created and driven by <see cref="WeaponManager"/>.
/// </summary>
public sealed partial class Weapon
{
    public enum StateId { Hidden, Equipping, Ready, Reloading, Holstering, ThrowingGrenade }

    public WeaponData Data { get; }
    public WeaponViewModel ViewModel { get; }
    public int Magazine { get; private set; }
    public int Reserve { get; private set; }
    /// <summary>0 = hip, 1 = fully aimed.</summary>
    public float AimAmount { get; private set; }
    public StateId State => fsm.CurrentId;
    public bool IsVisible => State != StateId.Hidden;
    public bool CanReload => Magazine < Data.magazineSize && (Data.infiniteReserveAmmo || Reserve > 0);
    public bool IsReserveFull => Data.infiniteReserveAmmo || Reserve >= Data.maxReserveAmmo;

    public event Action<Weapon> AmmoChanged;
    public event Action<Weapon> Fired;
    public event Action<StateId, StateId> StateChanged;

    private readonly WeaponManager owner;
    private readonly AnimationPlayer anim;
    private readonly StateMachine<StateId> fsm = new StateMachine<StateId>();

    private WeaponInput input;
    private bool wantsAim;
    private float nextFireTime;
    private float triggerBufferedUntil;
    private float fireAnimationUntil;
    private int burstShotsLeft;
    private Action onHolstered;

    private static readonly string[] IdleStates = { WeaponAnimStates.Idle };
    private static readonly string[] WalkStates = { WeaponAnimStates.Walk, WeaponAnimStates.Idle };
    private static readonly string[] RunStates = { WeaponAnimStates.Run, WeaponAnimStates.Walk, WeaponAnimStates.Idle };
    private static readonly string[] AimIdleStates = { WeaponAnimStates.AimIdle, WeaponAnimStates.Idle };
    private static readonly string[] AimWalkStates = { WeaponAnimStates.AimWalk, WeaponAnimStates.AimIdle, WeaponAnimStates.Walk, WeaponAnimStates.Idle };
    private static readonly string[] FireStates = { WeaponAnimStates.Fire };
    private static readonly string[] FireLastStates = { WeaponAnimStates.FireLast, WeaponAnimStates.Fire };
    private static readonly string[] AimFireStates = { WeaponAnimStates.AimFire, WeaponAnimStates.Fire };
    private static readonly string[] ReloadStates = { WeaponAnimStates.Reload };
    private static readonly string[] ReloadEmptyStates = { WeaponAnimStates.ReloadEmpty, WeaponAnimStates.Reload };

    public Weapon(WeaponManager owner, WeaponData data, WeaponViewModel viewModel, int magazine, int reserve)
    {
        this.owner = owner;
        Data = data;
        ViewModel = viewModel;
        anim = new AnimationPlayer(viewModel.Animator);

        Magazine = magazine < 0 ? data.magazineSize : Mathf.Clamp(magazine, 0, data.magazineSize);
        Reserve = reserve < 0 ? data.startingReserveAmmo : Mathf.Clamp(reserve, 0, data.maxReserveAmmo);

        fsm.Add(StateId.Hidden, new HiddenState(this))
           .Add(StateId.Equipping, new EquipState(this))
           .Add(StateId.Ready, new ReadyState(this))
           .Add(StateId.Reloading, new ReloadState(this))
           .Add(StateId.Holstering, new HolsterState(this))
           .Add(StateId.ThrowingGrenade, new GrenadeState(this));
        fsm.StateChanged += (previous, next) => StateChanged?.Invoke(previous, next);

        viewModel.SoundRequested += clip => owner.PlaySound(clip, 1f, 0.03f);
        fsm.ChangeState(StateId.Hidden);
    }

    // ---------------- Public API (used by WeaponManager) ----------------

    public void Tick(in WeaponInput frameInput, float deltaTime)
    {
        input = frameInput;
        wantsAim = false;
        fsm.Tick(deltaTime);

        float target = wantsAim ? 1f : 0f;
        AimAmount = Mathf.MoveTowards(AimAmount, target, Data.aimSpeed * deltaTime);
    }

    public void Equip()
    {
        if (State == StateId.Hidden || State == StateId.Holstering) fsm.ChangeState(StateId.Equipping);
    }

    /// <summary>Plays the holster animation, then hides the model and calls <paramref name="onDone"/>.</summary>
    public void Holster(Action onDone)
    {
        if (State == StateId.Hidden) { onDone?.Invoke(); return; }
        onHolstered = onDone;
        if (State != StateId.Holstering) fsm.ChangeState(StateId.Holstering);
    }

    public void HideImmediate()
    {
        onHolstered = null;
        fsm.ChangeState(StateId.Hidden);
        AimAmount = 0f;
    }

    public bool TryStartGrenadeThrow()
    {
        if (State != StateId.Ready) return false;
        fsm.ChangeState(StateId.ThrowingGrenade);
        return true;
    }

    /// <summary>Adds reserve ammo. Returns how much was actually taken.</summary>
    public int AddReserveAmmo(int amount)
    {
        if (amount <= 0 || Data.infiniteReserveAmmo) return 0;
        int taken = Mathf.Min(amount, Data.maxReserveAmmo - Reserve);
        if (taken <= 0) return 0;
        Reserve += taken;
        AmmoChanged?.Invoke(this);
        return taken;
    }

    // ---------------- Shooting ----------------

    private void HandleTrigger()
    {
        float now = Time.time;
        if (input.FirePressed) triggerBufferedUntil = now + Data.triggerBufferTime;
        bool buffered = now <= triggerBufferedUntil;

        if (!Data.canFireWhileSprinting && owner.LocomotionState == PlayerLocomotionState.Sprint) return;

        bool wantsShot;
        switch (Data.fireMode)
        {
            case FireMode.FullAuto:
                wantsShot = input.FireHeld || buffered;
                break;
            case FireMode.Burst:
                if (burstShotsLeft == 0 && buffered) burstShotsLeft = Data.burstCount;
                wantsShot = burstShotsLeft > 0;
                break;
            default:
                wantsShot = buffered;
                break;
        }

        if (!wantsShot || now < nextFireTime) return;
        triggerBufferedUntil = 0f;

        if (Magazine <= 0)
        {
            DryFire(now);
            burstShotsLeft = 0;
            if (Data.autoReloadWhenEmpty && CanReload) fsm.ChangeState(StateId.Reloading);
            return;
        }

        Shoot(now);

        if (Data.fireMode == FireMode.Burst && --burstShotsLeft <= 0)
        {
            burstShotsLeft = 0;
            nextFireTime += Data.burstCooldown;
        }
    }

    private void Shoot(float now)
    {
        // Keep the exact fire rate even if frames are uneven.
        float interval = Data.SecondsBetweenShots;
        nextFireTime = (now - nextFireTime > interval ? now : nextFireTime) + interval;

        Magazine--;

        bool aimed = AimAmount > 0.5f;
        anim.PlayAction(0f, aimed ? AimFireStates : Magazine == 0 ? FireLastStates : FireStates);
        fireAnimationUntil = now + Data.fireAnimationLength;

        owner.PlayRandomSound(Data.fireSounds, Data.fireVolume, Data.firePitchVariation);
        owner.SpawnMuzzleFlash(this);

        Transform view = owner.ViewTransform;
        float spread = CurrentSpread();
        for (int i = 0; i < Data.pelletsPerShot; i++)
            FirePellet(view, Hitscan.SpreadDirection(view, spread));

        float recoil = Mathf.Lerp(1f, Data.aimRecoilMultiplier, AimAmount);
        owner.ApplyRecoil(this, recoil);

        AmmoChanged?.Invoke(this);
        Fired?.Invoke(this);
    }

    private void FirePellet(Transform view, Vector3 direction)
    {
        if (!Hitscan.Cast(view.position, direction, Data.range, Data.hitMask, owner.Player, out RaycastHit hit)) return;

        float damage = Data.damage * Mathf.Max(0f, Data.damageFalloff.Evaluate(hit.distance / Data.range));
        IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(new DamageInfo
            {
                Amount = damage,
                Type = DamageType.Bullet,
                Point = hit.point,
                Normal = hit.normal,
                Direction = direction,
                Instigator = owner.gameObject
            });
        }

        if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
            hit.rigidbody.AddForceAtPosition(direction * Data.impactForce, hit.point, ForceMode.Impulse);

        owner.SpawnImpact(Data, hit);
    }

    private float CurrentSpread()
    {
        float spread = Mathf.Lerp(Data.hipSpread, Data.aimSpread, AimAmount);
        if (!owner.Player.IsGrounded) spread *= Data.airborneSpreadMultiplier;
        else if (owner.IsPlayerMoving) spread *= Data.movingSpreadMultiplier;
        return spread;
    }

    private void DryFire(float now)
    {
        nextFireTime = now + 0.25f;
        anim.PlayAction(WeaponAnimStates.DryFire, Data.actionCrossFade);
        owner.PlaySound(Data.dryFireSound, 1f, 0f);
    }

    private void FinishReload()
    {
        int needed = Data.magazineSize - Magazine;
        int taken = Data.infiniteReserveAmmo ? needed : Mathf.Min(needed, Reserve);
        Magazine += taken;
        if (!Data.infiniteReserveAmmo) Reserve -= taken;
        AmmoChanged?.Invoke(this);
    }

    // ---------------- Animation ----------------

    private void UpdateLocomotionAnimation()
    {
        if (Time.time < fireAnimationUntil) return;

        bool aimed = AimAmount > 0.5f;
        string[] states;
        switch (owner.LocomotionState)
        {
            case PlayerLocomotionState.Sprint:
                states = aimed ? AimWalkStates : RunStates;
                break;
            case PlayerLocomotionState.Walk:
            case PlayerLocomotionState.CrouchWalk:
                states = aimed ? AimWalkStates : WalkStates;
                break;
            default:
                states = aimed ? AimIdleStates : IdleStates;
                break;
        }
        anim.PlayLoop(Data.locomotionCrossFade, states);
    }
}
