using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Recycles short-lived effect prefabs (muzzle flashes, impacts, explosions) instead of
/// Instantiate/Destroy on every use. One pool per prefab, created on first use.
///
///   EffectPool.Spawn(prefab, position, rotation, lifetime);          // world space
///   EffectPool.Spawn(prefab, muzzleTransform, lifetime);             // attached to a parent
///   EffectPool.Prewarm(prefab, 8);                                   // create copies up front
///
/// Each prefab has at most <see cref="MaxActivePerPrefab"/> copies alive at once; when that's
/// reached the oldest one is reused, so heavy fire never makes the instance count grow.
/// The pool lives in the current scene and is rebuilt after a scene load.
/// </summary>
[DefaultExecutionOrder(-50)]
public class EffectPool : MonoBehaviour
{
    /// <summary>Maximum number of copies of one prefab visible at the same time.</summary>
    public static int MaxActivePerPrefab = 64;

    private static EffectPool instance;
    private static bool quitting;

    private struct Active
    {
        public PooledEffect Effect;
        public float ExpireTime;
    }

    private class Pool
    {
        public GameObject Prefab;
        public readonly Stack<PooledEffect> Free = new Stack<PooledEffect>();
        public readonly List<Active> Active = new List<Active>(); // oldest first
    }

    private readonly Dictionary<GameObject, Pool> pools = new Dictionary<GameObject, Pool>();
    private readonly List<Pool> poolList = new List<Pool>();

    // ---------------- Public API ----------------

    /// <summary>Spawns an effect in world space. lifetime &lt;= 0 = worked out from its particle systems.</summary>
    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime = 0f)
    {
        return Spawn(prefab, null, position, rotation, lifetime);
    }

    /// <summary>Spawns an effect as a child of a parent, keeping the prefab's local offset (like Instantiate(prefab, parent, false)).</summary>
    public static GameObject Spawn(GameObject prefab, Transform parent, float lifetime = 0f)
    {
        if (prefab == null) return null;
        Transform pt = prefab.transform;
        return Spawn(prefab, parent, pt.localPosition, pt.localRotation, lifetime);
    }

    /// <summary>Creates copies of a prefab ahead of time so the first shots don't hitch.</summary>
    public static void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0) return;
        EffectPool pool = Instance;
        if (pool == null) return;
        Pool p = pool.GetPool(prefab);
        count = Mathf.Min(count, MaxActivePerPrefab);
        while (p.Free.Count + p.Active.Count < count)
            p.Free.Push(pool.Create(p));
    }

    // ---------------- Internals ----------------

    private static EffectPool Instance
    {
        get
        {
            if (instance == null && !quitting)
                instance = new GameObject("[EffectPool]").AddComponent<EffectPool>();
            return instance;
        }
    }

    /// <summary>With a parent, position/rotation are local to it; without one they're world space.</summary>
    private static GameObject Spawn(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, float lifetime)
    {
        EffectPool pool = Instance;
        if (prefab == null || pool == null) return null;

        Pool p = pool.GetPool(prefab);
        PooledEffect effect = pool.Take(p);

        // Place it before activating, so world-space particles/trails don't streak from the old spot.
        Transform t = effect.transform;
        t.SetParent(parent, false);
        if (parent != null) t.SetLocalPositionAndRotation(position, rotation);
        else t.SetPositionAndRotation(position, rotation);
        effect.gameObject.SetActive(true);
        effect.Restart();

        float duration = lifetime > 0f ? lifetime : effect.NaturalDuration;
        p.Active.Add(new Active { Effect = effect, ExpireTime = Time.time + duration });
        return effect.gameObject;
    }

    private Pool GetPool(GameObject prefab)
    {
        if (!pools.TryGetValue(prefab, out Pool p))
        {
            p = new Pool { Prefab = prefab };
            pools.Add(prefab, p);
            poolList.Add(p);
        }
        return p;
    }

    private PooledEffect Take(Pool p)
    {
        // Reuse a free copy (skipping any that were destroyed with a parent).
        while (p.Free.Count > 0)
        {
            PooledEffect free = p.Free.Pop();
            if (free != null) return free;
        }

        // At the cap: recycle the oldest active copy.
        if (p.Active.Count >= MaxActivePerPrefab)
        {
            while (p.Active.Count > 0)
            {
                PooledEffect oldest = p.Active[0].Effect;
                p.Active.RemoveAt(0);
                if (oldest != null)
                {
                    oldest.gameObject.SetActive(false);
                    return oldest;
                }
            }
        }

        return Create(p);
    }

    private PooledEffect Create(Pool p)
    {
        GameObject go = Instantiate(p.Prefab, transform, false);
        go.name = p.Prefab.name;
        go.SetActive(false);
        if (!go.TryGetComponent(out PooledEffect effect))
            effect = go.AddComponent<PooledEffect>();
        effect.Init();
        return effect;
    }

    private void Release(Pool p, PooledEffect effect)
    {
        if (effect == null) return; // destroyed together with its parent (e.g. a dropped weapon)
        effect.gameObject.SetActive(false);
        effect.transform.SetParent(transform, false);
        p.Free.Push(effect);
    }

    private void Update()
    {
        float now = Time.time;
        for (int i = 0; i < poolList.Count; i++)
        {
            Pool p = poolList[i];
            List<Active> active = p.Active;
            int write = 0;
            for (int read = 0; read < active.Count; read++)
            {
                Active a = active[read];
                if (a.Effect == null) continue;
                if (now >= a.ExpireTime) Release(p, a.Effect);
                else active[write++] = a;
            }
            if (write < active.Count) active.RemoveRange(write, active.Count - write);
        }
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnApplicationQuit()
    {
        quitting = true;
    }
}

/// <summary>Added automatically to pooled effects. Restarts their particle systems and trails on reuse.</summary>
[DisallowMultipleComponent]
public class PooledEffect : MonoBehaviour
{
    private ParticleSystem[] particles;
    private ParticleSystem[] rootParticles;
    private TrailRenderer[] trails;

    /// <summary>How long the effect plays on its own (longest particle system duration + lifetime).</summary>
    public float NaturalDuration { get; private set; } = 1f;

    internal void Init()
    {
        particles = GetComponentsInChildren<ParticleSystem>(true);
        trails = GetComponentsInChildren<TrailRenderer>(true);

        var roots = new List<ParticleSystem>();
        float longest = 0f;
        foreach (ParticleSystem ps in particles)
        {
            ParticleSystem.MainModule main = ps.main;
            // A pooled object must never destroy itself.
            if (main.stopAction == ParticleSystemStopAction.Destroy)
                main.stopAction = ParticleSystemStopAction.None;
            main.playOnAwake = false;

            if (!main.loop)
                longest = Mathf.Max(longest, main.duration + main.startDelay.constantMax + main.startLifetime.constantMax);

            // Only top-level systems need Play(true); it plays their sub-systems too.
            Transform parent = ps.transform.parent;
            if (parent == null || parent.GetComponentInParent<ParticleSystem>(true) == null || ps.transform == transform)
                roots.Add(ps);
        }
        rootParticles = roots.ToArray();
        NaturalDuration = longest > 0f ? longest : 1f;
    }

    internal void Restart()
    {
        for (int i = 0; i < trails.Length; i++)
            trails[i].Clear();
        for (int i = 0; i < rootParticles.Length; i++)
        {
            ParticleSystem ps = rootParticles[i];
            ps.Clear(true);
            ps.Play(true);
        }
    }
}
