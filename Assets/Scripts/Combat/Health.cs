using UnityEngine;
using UnityEngine.Events;

/// <summary>Simple health component that receives bullet and explosion damage.</summary>
public class Health : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private bool destroyOnDeath = true;
    [SerializeField] private float destroyDelay = 0f;

    public UnityEvent<float> onDamaged;
    public UnityEvent onDied;

    public float Current { get; private set; }
    public float Max => maxHealth;
    public bool IsDead => Current <= 0f;

    private void Awake() => Current = maxHealth;

    public void TakeDamage(in DamageInfo damage)
    {
        if (IsDead || damage.Amount <= 0f) return;

        Current = Mathf.Max(0f, Current - damage.Amount);
        onDamaged?.Invoke(damage.Amount);

        if (IsDead)
        {
            onDied?.Invoke();
            if (destroyOnDeath) Destroy(gameObject, destroyDelay);
        }
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        Current = Mathf.Min(maxHealth, Current + amount);
    }
}
