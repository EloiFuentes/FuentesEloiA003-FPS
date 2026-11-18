using UnityEngine;

/// <summary>
/// A throwable type. Create with Assets > Create > FPS > Grenade Data.
/// The explosion itself is configured on the projectile prefab's Grenade component.
/// </summary>
[CreateAssetMenu(menuName = "FPS/Grenade Data", fileName = "NewGrenade", order = 1)]
public class GrenadeData : ScriptableObject
{
    public string displayName = "Frag Grenade";
    [Tooltip("Prefab with Rigidbody, collider and a Grenade component.")]
    public Grenade projectilePrefab;
    [Min(1)] public int maxCarried = 4;

    [Header("Throw")]
    public float throwSpeed = 18f;
    public float throwUpSpeed = 3f;
    [Tooltip("Fraction of the player's velocity added to the throw.")]
    [Range(0f, 1f)] public float inheritPlayerVelocity = 0.5f;
    [Tooltip("Random spin (rad/s).")]
    public float spin = 10f;
    [Tooltip("Cooldown between throws when no weapon is equipped (seconds).")]
    public float unarmedThrowCooldown = 0.6f;
}
