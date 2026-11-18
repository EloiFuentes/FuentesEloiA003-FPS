/// <summary>
/// Default Animator state names. Name your Animator states exactly like this and the scripts
/// play them automatically. No transitions needed. Every state is optional.
/// Use Assets > Create > FPS > Weapon Animator Controller to generate a controller with all of them.
/// </summary>
public static class WeaponAnimStates
{
    // Locomotion (looping)
    public const string Idle = "Idle";
    public const string Walk = "Walk";
    public const string Run = "Run";
    public const string AimIdle = "AimIdle";
    public const string AimWalk = "AimWalk";

    // Actions (one-shot)
    public const string Equip = "Equip";
    public const string Holster = "Holster";
    public const string Fire = "Fire";
    public const string FireLast = "FireLast";       // last round in the magazine (e.g. slide lock)
    public const string AimFire = "AimFire";
    public const string DryFire = "DryFire";
    public const string Reload = "Reload";
    public const string ReloadEmpty = "ReloadEmpty"; // reload from an empty magazine
    public const string GrenadeThrow = "GrenadeThrow";

    public static readonly string[] All =
    {
        Idle, Walk, Run, AimIdle, AimWalk,
        Equip, Holster, Fire, FireLast, AimFire, DryFire, Reload, ReloadEmpty, GrenadeThrow
    };

    public static readonly string[] Looping = { Idle, Walk, Run, AimIdle, AimWalk };
}

/// <summary>Default state names for an optional full-body / third-person player Animator.</summary>
public static class PlayerAnimStates
{
    public static readonly string[] All =
        System.Enum.GetNames(typeof(PlayerLocomotionState));
}
