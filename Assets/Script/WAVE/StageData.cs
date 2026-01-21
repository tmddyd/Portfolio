using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class StageData : MonoBehaviour
{
    [Header("Google Sheet")]
    public string spreadsheetId;
    public long gid = 0;

    [Header("Default (Auto Fill)")]
    [SerializeField]
    private string defaultSpreadsheetId =
        "1H9JCEjWLgUPNGSmyJUwBk_yhnQqCci2_nrlW-qxrh4o";
    [SerializeField] private long defaultGid = 0;

    [Header("Runtime Data")]
    public Dictionary<string, StageRow> byKey =
        new Dictionary<string, StageRow>(StringComparer.OrdinalIgnoreCase);

    public string CsvUrl =>
        $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={gid}";

    private void Reset()
    {
        ApplyDefaultsIfEmpty();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyDefaultsIfEmpty();
    }
#endif

    private void ApplyDefaultsIfEmpty()
    {
        if (string.IsNullOrWhiteSpace(spreadsheetId))
            spreadsheetId = defaultSpreadsheetId;

        if (gid == 0)
            gid = defaultGid;
    }

    public IEnumerator Load()
    {
        byKey.Clear();

        if (string.IsNullOrWhiteSpace(spreadsheetId))
        {
            Debug.LogError("[StageData] spreadsheetId가 비어있습니다.");
            yield break;
        }

        using (UnityWebRequest req = UnityWebRequest.Get(CsvUrl))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"[StageData] CSV 다운로드 실패: {req.error}\nURL: {CsvUrl}");
                yield break;
            }

            Parse(req.downloadHandler.text);
        }
    }

    private void Parse(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            Debug.LogError("[StageData] CSV 비어있음");
            return;
        }

        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count < 2)
        {
            Debug.LogError("[StageData] 헤더/데이터 부족");
            return;
        }

        var header = CsvUtil.ParseLine(lines[0]).Select(CsvUtil.NormalizeHeader).ToList();
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++) col[header[i]] = i;

        int Req(string name)
        {
            if (!col.TryGetValue(name, out var idx))
                throw new Exception($"[StageData] '{name}' 컬럼 없음. 헤더: {string.Join(", ", header)}");
            return idx;
        }

        int iStageId = Req("StageID");
        int iStep = Req("Step");
        int iWaveId = Req("WaveID");

        for (int r = 1; r < lines.Count; r++)
        {
            var f = CsvUtil.ParseLine(lines[r]);
            int need = Mathf.Max(iWaveId, Mathf.Max(iStageId, iStep));
            if (f.Count <= need) continue;

            string stageId = (f[iStageId] ?? "").Trim();
            int step = CsvUtil.ToInt(f[iStep], 0);
            string waveId = (f[iWaveId] ?? "").Trim();

            if (string.IsNullOrWhiteSpace(stageId) || step <= 0 || string.IsNullOrWhiteSpace(waveId))
                continue;

            var row = new StageRow
            {
                StageID = stageId,
                Step = step,
                WaveID = waveId
            };

            byKey[MakeKey(stageId, step)] = row;
        }

        Debug.Log($"[StageData] 로드 완료: {byKey.Count} rows");
    }

    public bool TryGetWaveId(string stageId, int step, out string waveId)
    {
        waveId = null;
        if (string.IsNullOrWhiteSpace(stageId) || step <= 0) return false;

        if (byKey.TryGetValue(MakeKey(stageId.Trim(), step), out var row))
        {
            waveId = row.WaveID;
            return !string.IsNullOrWhiteSpace(waveId);
        }
        return false;
    }

    private static string MakeKey(string stageId, int step) => $"{stageId}__{step}";
}

[Serializable]
public class StageRow
{
    public string StageID;
    public int Step;
    public string WaveID;
}
