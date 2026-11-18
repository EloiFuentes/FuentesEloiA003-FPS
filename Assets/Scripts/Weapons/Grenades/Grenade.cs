using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Thrown grenade: bounces, explodes after its fuse, damages <see cref="IDamageable"/>s
/// (with line-of-sight check), pushes rigidbodies and shakes the camera.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Grenade : MonoBehaviour
{
    [Header("Fuse")]
    [SerializeField] private float fuseTime = 2.5f;
    [SerializeField] private bool explodeOnImpact;

    [Header("Explosion")]
    [SerializeField] private float radius = 7f;
    [SerializeField] private float maxDamage = 150f;
    [SerializeField] private float minDamage = 10f;
    [SerializeField] private float explosionForce = 12f;
    [SerializeField] private float upwardsModifier = 1f;
    [SerializeField] private LayerMask affectedLayers = ~0;
    [Tooltip("Walls block damage.")]
    [SerializeField] private bool requireLineOfSight = true;

    [Header("Feedback")]
    [SerializeField] private GameObject explosionEffect;
    [SerializeField] private float explosionEffectLifetime = 5f;
    [SerializeField] private AudioClip explosionSound;
    [SerializeField] private float explosionVolume = 1f;
    [SerializeField] private AudioClip bounceSound;
    [SerializeField] private float bounceVolume = 0.5f;
    [Tooltip("Camera shake reaches up to this distance.")]
    [SerializeField] private float shakeRadius = 25f;
    [SerializeField, Range(0f, 1f)] private float maxShake = 0.9f;

    private Rigidbody body;
    private GameObject instigator;
    private float timer;
    private bool exploded;

    private static readonly Collider[] OverlapBuffer = new Collider[128];
    private static readonly HashSet<IDamageable> Damaged = new HashSet<IDamageable>();
    private static readonly HashSet<Rigidbody> Pushed = new HashSet<Rigidbody>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    public void Launch(Vector3 velocity, Vector3 angularVelocity, GameObject thrower, Collider ignoreCollider)
    {
        instigator = thrower;
        body.linearVelocity = velocity;
        body.angularVelocity = angularVelocity;

        if (ignoreCollider != null)
            foreach (Collider c in GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(c, ignoreCollider);
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= fuseTime) Explode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (bounceSound != null && collision.relativeVelocity.sqrMagnitude > 4f)
            AudioSource.PlayClipAtPoint(bounceSound, transform.position, bounceVolume);
        if (explodeOnImpact) Explode();
    }

    public void Explode()
    {
        if (exploded) return;
        exploded = true;

        Vector3 center = transform.position;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        Damaged.Clear();
        Pushed.Clear();
        int count = Physics.OverlapSphereNonAlloc(center, radius, OverlapBuffer, affectedLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider target = OverlapBuffer[i];
            Vector3 closest = target.bounds.ClosestPoint(center);
            float distance = Vector3.Distance(center, closest);
            if (requireLineOfSight && IsBlocked(center, target)) continue;

            float t = 1f - Mathf.Clamp01(distance / radius);

            IDamageable damageable = target.GetComponentInParent<IDamageable>();
            if (damageable != null && Damaged.Add(damageable))
            {
                Vector3 direction = (target.bounds.center - center).normalized;
                damageable.TakeDamage(new DamageInfo
                {
                    Amount = Mathf.Lerp(minDamage, maxDamage, t * t),
                    Type = DamageType.Explosion,
                    Point = closest,
                    Normal = -direction,
                    Direction = direction,
                    Instigator = instigator
                });
            }

            Rigidbody rb = target.attachedRigidbody;
            if (rb != null && !rb.isKinematic && Pushed.Add(rb))
                rb.AddExplosionForce(explosionForce, center, radius, upwardsModifier, ForceMode.Impulse);
        }

        if (explosionEffect != null)
            EffectPool.Spawn(explosionEffect, center, Quaternion.identity, explosionEffectLifetime);
        if (explosionSound != null)
            AudioSource.PlayClipAtPoint(explosionSound, center, explosionVolume);

        FirstPersonCameraEffects cameraEffects = FirstPersonCameraEffects.Active;
        if (cameraEffects != null)
        {
            float d = Vector3.Distance(cameraEffects.transform.position, center);
            float shake = 1f - Mathf.Clamp01(d / shakeRadius);
            if (shake > 0f) cameraEffects.AddShake(maxShake * shake * shake);
        }

        Destroy(gameObject);
    }

    private bool IsBlocked(Vector3 center, Collider target)
    {
        Vector3 to = target.bounds.center - center;
        float distance = to.magnitude;
        if (distance < 0.05f) return false;
        if (!Physics.Raycast(center, to / distance, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            return false;
        // Blocked if we hit something that isn't part of the target.
        return hit.collider != target && hit.collider.transform.root != target.transform.root;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
