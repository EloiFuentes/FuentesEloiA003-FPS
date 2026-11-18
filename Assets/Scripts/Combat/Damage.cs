using UnityEngine;

public enum DamageType { Bullet, Explosion, Other }

public struct DamageInfo
{
    public float Amount;
    public DamageType Type;
    public Vector3 Point;
    public Vector3 Normal;
    /// <summary>Direction the damage travels (bullet direction / away from the explosion).</summary>
    public Vector3 Direction;
    public GameObject Instigator;
}

/// <summary>Implement on anything that can be shot or blown up.</summary>
public interface IDamageable
{
    void TakeDamage(in DamageInfo damage);
}
