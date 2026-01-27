using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class LevelBracketDataRow
{
    public string LevelBracketID;
    public int MinLv;
    public int MaxLv;

    public int HpGain;
    public int AtkGain;
    public int DefGain;
    public int CrdGain;
    public int CrpGain;
    public float AsGain;
}

public class LevelBracketDataLoader : MonoBehaviour
{
    [Header("Google Sheet CSV")]
    public string spreadsheetId = "1D8_5O2NrN1iGqdfAWTmBlbEvgVqg7t5qHGGTEbtfB_c";
    public int gid = 0;

    [Header("Runtime")]
    public bool autoLoadOnStart = true;

    public bool IsLoaded { get; private set; }

    public List<LevelBracketDataRow> Rows { get; private set; } = new List<LevelBracketDataRow>();
    private readonly Dictionary<string, LevelBracketDataRow> byId = new Dictionary<string, LevelBracketDataRow>();

    public event Action OnLoaded;

    private void Start()
    {
        if (autoLoadOnStart)
            StartCoroutine(Load());
    }

    public IEnumerator Load()
    {
        IsLoaded = false;
        Rows.Clear();
        byId.Clear();

        string url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={gid}";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = 20;
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"[LevelBracketDataLoader] CSV 다운로드 실패: {req.error}\nURL={url}");
                yield break;
            }

            string csv = (req.downloadHandler.text ?? "").Trim('\uFEFF');
            var trimmed = csv.TrimStart();
            if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("[LevelBracketDataLoader] CSV가 아니라 HTML이 내려왔습니다. (시트 공유 권한 확인)");
                yield break;
            }

            ParseCsv(csv);
        }

        IsLoaded = true;
        OnLoaded?.Invoke();
        Debug.Log($"[LevelBracketDataLoader] 로드 완료. Rows={Rows.Count}");
    }

    private void ParseCsv(string csv)
    {
        var table = CsvUtil.Parse(csv);
        if (table.Count <= 1)
        {
            Debug.LogWarning("[LevelBracketDataLoader] CSV에 데이터가 없습니다.");
            return;
        }

        var headerMap = CsvUtil.BuildHeaderMap(table[0]);

        RequireHeader(headerMap, "LevelBracketID");
        RequireHeader(headerMap, "MinLv");
        RequireHeader(headerMap, "MaxLv");
        RequireHeader(headerMap, "HpGain");
        RequireHeader(headerMap, "AtkGain");
        RequireHeader(headerMap, "DefGain");
        RequireHeader(headerMap, "CrdGain");
        RequireHeader(headerMap, "CrpGain");
        RequireHeader(headerMap, "AsGain");

        for (int i = 1; i < table.Count; i++)
        {
            var r = table[i];
            if (r == null || r.Length == 0) continue;

            if (!CsvUtil.TryGetCell(r, headerMap, out string id, "LevelBracketID")) continue;
            if (string.IsNullOrWhiteSpace(id)) continue;

            CsvUtil.TryGetCell(r, headerMap, out string sMin, "MinLv");
            CsvUtil.TryGetCell(r, headerMap, out string sMax, "MaxLv");
            CsvUtil.TryGetCell(r, headerMap, out string sHp, "HpGain");
            CsvUtil.TryGetCell(r, headerMap, out string sAtk, "AtkGain");
            CsvUtil.TryGetCell(r, headerMap, out string sDef, "DefGain");
            CsvUtil.TryGetCell(r, headerMap, out string sCrd, "CrdGain");
            CsvUtil.TryGetCell(r, headerMap, out string sCrp, "CrpGain");
            CsvUtil.TryGetCell(r, headerMap, out string sAs, "AsGain");

            var row = new LevelBracketDataRow
            {
                LevelBracketID = id,
                MinLv = CsvUtil.ToInt(sMin, 1),
                MaxLv = CsvUtil.ToInt(sMax, 1),
                HpGain = CsvUtil.ToInt(sHp, 0),
                AtkGain = CsvUtil.ToInt(sAtk, 0),
                DefGain = CsvUtil.ToInt(sDef, 0),
                CrdGain = CsvUtil.ToInt(sCrd, 0),
                CrpGain = CsvUtil.ToInt(sCrp, 0),
                AsGain = CsvUtil.ToFloat(sAs, 0f),
            };

            Rows.Add(row);
            byId[row.LevelBracketID] = row;
        }
    }

    private void RequireHeader(Dictionary<string, int> headerMap, string header)
    {
        var key = CsvUtil.NormalizeHeader(header);
        if (!headerMap.ContainsKey(key))
            Debug.LogError($"[LevelBracketDataLoader] 헤더 누락: {header} (시트 1행 확인)");
    }

    public bool TryGet(string bracketId, out LevelBracketDataRow row)
        => byId.TryGetValue(bracketId ?? "", out row);
}
