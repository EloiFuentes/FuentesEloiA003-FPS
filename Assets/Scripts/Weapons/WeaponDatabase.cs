using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The list of every weapon in the game, looked up by id like a dictionary.
/// Create with Assets > Create > FPS > Weapon Database and drag your WeaponData assets in.
/// </summary>
[CreateAssetMenu(menuName = "FPS/Weapon Database", fileName = "WeaponDatabase", order = 2)]
public class WeaponDatabase : ScriptableObject
{
    [SerializeField] private List<WeaponData> weapons = new List<WeaponData>();

    private Dictionary<string, WeaponData> lookup;

    public IReadOnlyList<WeaponData> All => weapons;

    public bool TryGet(string id, out WeaponData data)
    {
        if (lookup == null) BuildLookup();
        if (string.IsNullOrEmpty(id)) { data = null; return false; }
        return lookup.TryGetValue(id, out data);
    }

    public WeaponData Get(string id)
    {
        if (TryGet(id, out WeaponData data)) return data;
        Debug.LogWarning($"[WeaponDatabase] No weapon with id '{id}' in '{name}'.", this);
        return null;
    }

    public bool Contains(string id) => TryGet(id, out _);

    private void BuildLookup()
    {
        lookup = new Dictionary<string, WeaponData>();
        foreach (WeaponData data in weapons)
        {
            if (data == null) continue;
            if (lookup.ContainsKey(data.id))
            {
                Debug.LogWarning($"[WeaponDatabase] Duplicate weapon id '{data.id}' ({data.name}). Ids must be unique.", this);
                continue;
            }
            lookup.Add(data.id, data);
        }
    }

    private void OnEnable() => lookup = null;
    private void OnValidate() => lookup = null;
}
