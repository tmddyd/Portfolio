using System;
using System.Collections.Generic;
using UnityEngine;

public class MonsterPrefab : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string mobId;     // string ID
        public GameObject prefab;
    }

    [Header("MobID(string) -> Prefab")]
    public List<Entry> entries = new List<Entry>();

    private Dictionary<string, GameObject> map =
        new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        BuildMap();
    }

    public void BuildMap()
    {
        map.Clear();

        foreach (var e in entries)
        {
            if (e == null || e.prefab == null) continue;
            if (string.IsNullOrWhiteSpace(e.mobId)) continue;

            map[e.mobId.Trim()] = e.prefab;
        }
    }

    public GameObject GetPrefab(string mobId)
    {
        if (string.IsNullOrWhiteSpace(mobId)) return null;
        map.TryGetValue(mobId.Trim(), out var prefab);
        return prefab;
    }
}
