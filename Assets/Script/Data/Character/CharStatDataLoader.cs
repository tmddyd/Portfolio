using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class CharStatDataRow
{
    public string CharStatID;
    public int MaxHp;
    public int Atk;
    public int Def;
    public float Spd;
    public int Crd;
    public int Crp;
    public int Ar;
    public float As;
}

public class CharStatDataLoader : MonoBehaviour
{
    [Header("Google Sheet CSV")]
    public string spreadsheetId = "13jDqouXYkVG47F_TvktSfIO7rdn4DiWfotRxwm3hRi4";
    public int gid = 0;

    [Header("Runtime")]
    public bool autoLoadOnStart = true;

    public bool IsLoaded { get; private set; }

    public List<CharStatDataRow> Rows { get; private set; } = new List<CharStatDataRow>();
    private readonly Dictionary<string, CharStatDataRow> byId = new Dictionary<string, CharStatDataRow>();

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
                Debug.LogError($"[CharStatDataLoader] CSV 다운로드 실패: {req.error}\nURL={url}");
                yield break;
            }

            string csv = (req.downloadHandler.text ?? "").Trim('\uFEFF');
            var trimmed = csv.TrimStart();
            if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("[CharStatDataLoader] CSV가 아니라 HTML이 내려왔습니다. (시트 공유 권한 확인)");
                yield break;
            }

            ParseCsv(csv);
        }

        IsLoaded = true;
        OnLoaded?.Invoke();
        Debug.Log($"[CharStatDataLoader] 로드 완료. Rows={Rows.Count}");
    }

    private void ParseCsv(string csv)
    {
        var table = CsvUtil.Parse(csv);
        if (table.Count <= 1)
        {
            Debug.LogWarning("[CharStatDataLoader] CSV에 데이터가 없습니다.");
            return;
        }

        var headerMap = CsvUtil.BuildHeaderMap(table[0]);

        RequireHeader(headerMap, "CharStatID");
        RequireHeader(headerMap, "MaxHp");
        RequireHeader(headerMap, "Atk");
        RequireHeader(headerMap, "Def");
        RequireHeader(headerMap, "Spd");
        RequireHeader(headerMap, "Crd");
        RequireHeader(headerMap, "Crp");
        RequireHeader(headerMap, "Ar");
        RequireHeader(headerMap, "As");

        for (int i = 1; i < table.Count; i++)
        {
            var r = table[i];
            if (r == null || r.Length == 0) continue;

            if (!CsvUtil.TryGetCell(r, headerMap, out string id, "CharStatID")) continue;
            if (string.IsNullOrWhiteSpace(id)) continue;

            CsvUtil.TryGetCell(r, headerMap, out string sMaxHp, "MaxHp");
            CsvUtil.TryGetCell(r, headerMap, out string sAtk, "Atk");
            CsvUtil.TryGetCell(r, headerMap, out string sDef, "Def");
            CsvUtil.TryGetCell(r, headerMap, out string sSpd, "Spd");
            CsvUtil.TryGetCell(r, headerMap, out string sCrd, "Crd");
            CsvUtil.TryGetCell(r, headerMap, out string sCrp, "Crp");
            CsvUtil.TryGetCell(r, headerMap, out string sAr, "Ar");
            CsvUtil.TryGetCell(r, headerMap, out string sAs, "As");

            var row = new CharStatDataRow
            {
                CharStatID = id,
                MaxHp = CsvUtil.ToInt(sMaxHp, 0),
                Atk = CsvUtil.ToInt(sAtk, 0),
                Def = CsvUtil.ToInt(sDef, 0),
                Spd = CsvUtil.ToFloat(sSpd, 1f),
                Crd = CsvUtil.ToInt(sCrd, 0),
                Crp = CsvUtil.ToInt(sCrp, 0),
                Ar = CsvUtil.ToInt(sAr, 0),
                As = CsvUtil.ToFloat(sAs, 1f),
            };

            Rows.Add(row);
            byId[row.CharStatID] = row;
        }
    }

    private void RequireHeader(Dictionary<string, int> headerMap, string header)
    {
        var key = CsvUtil.NormalizeHeader(header);
        if (!headerMap.ContainsKey(key))
            Debug.LogError($"[CharStatDataLoader] 헤더 누락: {header} (시트 1행 확인)");
    }

    public bool TryGet(string statId, out CharStatDataRow row)
        => byId.TryGetValue(statId ?? "", out row);
}
