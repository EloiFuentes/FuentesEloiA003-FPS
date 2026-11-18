using UnityEngine;

/// <summary>Raycast helpers for hitscan weapons.</summary>
public static class Hitscan
{
    private static readonly RaycastHit[] Buffer = new RaycastHit[32];

    /// <summary>Closest hit that doesn't belong to the player.</summary>
    public static bool Cast(Vector3 origin, Vector3 direction, float range, LayerMask mask,
                            PlayerController ignore, out RaycastHit closest)
    {
        closest = default;
        int count = Physics.RaycastNonAlloc(origin, direction, Buffer, range, mask, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = Buffer[i];
            if (ignore != null && ignore.OwnsCollider(hit.collider)) continue;
            if (hit.distance < best)
            {
                best = hit.distance;
                closest = hit;
                found = true;
            }
        }
        return found;
    }

    /// <summary>Random direction inside a cone of <paramref name="angleDegrees"/> around the view.</summary>
    public static Vector3 SpreadDirection(Transform view, float angleDegrees)
    {
        if (angleDegrees <= 0f) return view.forward;
        Vector2 r = Random.insideUnitCircle * Mathf.Tan(angleDegrees * Mathf.Deg2Rad);
        return (view.forward + view.right * r.x + view.up * r.y).normalized;
    }
}
