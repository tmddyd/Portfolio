using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class WaveData : MonoBehaviour
{
    [Header("Google Sheet")]
    public string spreadsheetId;
    public long gid = 0;

    [Header("Runtime Data")]
    public Dictionary<string, WaveDataRow> byWaveId =
        new Dictionary<string, WaveDataRow>(StringComparer.OrdinalIgnoreCase);

    public string CsvUrl =>
        $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={gid}";

    public IEnumerator Load()
    {
        byWaveId.Clear();

        using (UnityWebRequest req = UnityWebRequest.Get(CsvUrl))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"[WaveData] CSV 다운로드 실패: {req.error}\nURL: {CsvUrl}");
                yield break;
            }

            Parse(req.downloadHandler.text);
        }
    }

    void Parse(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            Debug.LogError("[WaveData] CSV 비어있음");
            return;
        }

        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count < 2)
        {
            Debug.LogError("[WaveData] 헤더/데이터 부족");
            return;
        }

        var header = CsvUtil.ParseLine(lines[0]).Select(CsvUtil.NormalizeHeader).ToList();
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++) col[header[i]] = i;

        int Req(string name)
        {
            if (!col.TryGetValue(name, out var idx))
                throw new Exception($"[WaveData] '{name}' 컬럼 없음. 헤더: {string.Join(", ", header)}");
            return idx;
        }
        int Opt(string name) => col.TryGetValue(name, out var idx) ? idx : -1;

        int iWaveID = Req("WaveID");

        // WaveMobID (단일 컬럼, 예: "WM01,WM02,WM03") 지원
        int iWaveMobID = Opt("WaveMobID");

        // WaveMobID1~5 지원
        int[] iWaveMobN = new int[5];
        for (int k = 0; k < 5; k++)
            iWaveMobN[k] = Opt($"WaveMobID{k + 1}");

        int iDuration = Opt("Duration");
        int iClearScore = Opt("ClearScore");
        int iHpMul = Opt("HpMul");
        int iAtkMul = Opt("AtkMul");
        int iDefMul = Opt("DefMul");

        // ✅ 추가
        int iScoreMul = Opt("ScoreMul");

        for (int r = 1; r < lines.Count; r++)
        {
            var f = CsvUtil.ParseLine(lines[r]);
            if (f.Count <= iWaveID) continue;

            string waveId = f[iWaveID]?.Trim();
            if (string.IsNullOrWhiteSpace(waveId)) continue;

            var row = new WaveDataRow();
            row.WaveID = waveId;

            // 1) WaveMobID1~5가 있으면 그걸 우선 사용
            bool usedN = false;
            for (int k = 0; k < 5; k++)
            {
                if (iWaveMobN[k] >= 0 && iWaveMobN[k] < f.Count)
                {
                    row.WaveMobIDs[k] = (f[iWaveMobN[k]] ?? "").Trim();
                    usedN = true;
                }
                else
                {
                    row.WaveMobIDs[k] = "";
                }
            }

            // 2) 없으면 WaveMobID 단일 컬럼에서 콤마 split
            if (!usedN && iWaveMobID >= 0 && iWaveMobID < f.Count)
            {
                var raw = (f[iWaveMobID] ?? "").Trim();
                var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(x => x.Trim())
                               .Where(x => !string.IsNullOrWhiteSpace(x))
                               .Take(5)
                               .ToList();

                for (int k = 0; k < 5; k++)
                    row.WaveMobIDs[k] = (k < parts.Count) ? parts[k] : "";
            }

            row.Duration = (iDuration >= 0 && iDuration < f.Count) ? CsvUtil.ToFloat(f[iDuration], 10f) : 10f;
            row.ClearScore = (iClearScore >= 0 && iClearScore < f.Count) ? CsvUtil.ToInt(f[iClearScore], 0) : 0;

            row.HpMul = (iHpMul >= 0 && iHpMul < f.Count) ? CsvUtil.ToFloat(f[iHpMul], 1f) : 1f;
            row.AtkMul = (iAtkMul >= 0 && iAtkMul < f.Count) ? CsvUtil.ToFloat(f[iAtkMul], 1f) : 1f;
            row.DefMul = (iDefMul >= 0 && iDefMul < f.Count) ? CsvUtil.ToFloat(f[iDefMul], 1f) : 1f;

            // ✅ ScoreMul 파싱
            row.ScoreMul = (iScoreMul >= 0 && iScoreMul < f.Count) ? CsvUtil.ToFloat(f[iScoreMul], 1f) : 1f;

            byWaveId[waveId] = row;
        }

        Debug.Log($"[WaveData] 로드 완료: {byWaveId.Count} waves");
    }

    public bool TryGet(string waveId, out WaveDataRow row)
    {
        if (string.IsNullOrWhiteSpace(waveId))
        {
            row = null;
            return false;
        }
        return byWaveId.TryGetValue(waveId.Trim(), out row);
    }
}
