using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class WaveMobData : MonoBehaviour
{
    [Header("Google Sheet")]
    public string spreadsheetId;
    public long gid = 0;

    [Header("Runtime Data")]
    public Dictionary<string, List<WaveMobRow>> byWaveMobId =
        new Dictionary<string, List<WaveMobRow>>(StringComparer.OrdinalIgnoreCase);

    public string CsvUrl =>
        $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={gid}";

    public IEnumerator Load()
    {
        byWaveMobId.Clear();

        using (UnityWebRequest req = UnityWebRequest.Get(CsvUrl))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"[WaveMobData] CSV 다운로드 실패: {req.error}\nURL: {CsvUrl}");
                yield break;
            }

            Parse(req.downloadHandler.text);
        }
    }

    private void Parse(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            Debug.LogError("[WaveMobData] CSV 비어있음");
            return;
        }

        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count < 2)
        {
            Debug.LogError("[WaveMobData] 헤더/데이터 부족");
            return;
        }

        var header = CsvUtil.ParseLine(lines[0]).Select(CsvUtil.NormalizeHeader).ToList();
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++) col[header[i]] = i;

        int GetIndex(string name)
        {
            if (!col.TryGetValue(name, out int idx))
                throw new Exception($"[WaveMobData] 컬럼 '{name}' 없음. 헤더: {string.Join(", ", header)}");
            return idx;
        }

        int iWaveMobID = GetIndex("WaveMobID");
        int iMobID = GetIndex("MobID");
        int iMobCount = GetIndex("MobCount");
        int iSpawnRate = GetIndex("SpawnRate");

        for (int r = 1; r < lines.Count; r++)
        {
            var f = CsvUtil.ParseLine(lines[r]);
            if (f.Count <= iWaveMobID) continue;

            string waveMobId = f[iWaveMobID]?.Trim();
            if (string.IsNullOrWhiteSpace(waveMobId)) continue;

            string mobId = (f.Count > iMobID) ? f[iMobID]?.Trim() : "";
            int mobCount = (f.Count > iMobCount) ? CsvUtil.ToInt(f[iMobCount], 0) : 0;
            float spawnRate = (f.Count > iSpawnRate) ? CsvUtil.ToFloat(f[iSpawnRate], 0f) : 0f;

            if (string.IsNullOrWhiteSpace(mobId) || mobCount <= 0 || spawnRate <= 0f)
                continue;

            var row = new WaveMobRow
            {
                WaveMobID = waveMobId,
                MobID = mobId,
                MobCount = mobCount,
                SpawnRate = spawnRate,
            };

            if (!byWaveMobId.TryGetValue(waveMobId, out var list))
            {
                list = new List<WaveMobRow>();
                byWaveMobId[waveMobId] = list;
            }
            list.Add(row);
        }

        Debug.Log($"[WaveMobData] 로드 완료: {byWaveMobId.Count} groups");
    }

    public bool TryGet(string waveMobId, out List<WaveMobRow> rows)
    {
        if (string.IsNullOrWhiteSpace(waveMobId))
        {
            rows = null;
            return false;
        }
        return byWaveMobId.TryGetValue(waveMobId.Trim(), out rows);
    }
}
