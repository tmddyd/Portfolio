using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class CharacterDataRow
{
    public string CharID;
    public string CharName;
    public string CharStatID;
    public string CharInfoID;
    public string LevelBracketID1;
    public string LevelBracketID2;
    public string LevelBracketID3;
    public string LevelBracketID4;
    public string LevelBracketID5;
    public string SkillID;
}

public class CharacterDataLoader : MonoBehaviour
{
    [Header("Google Sheet CSV")]
    public string spreadsheetId = "12ZlfqvkeKy3FOcxlVdm7ZZ2ixdSMzy4ketNu2C3SFtE";
    public int gid = 0;

    [Header("Runtime")]
    public bool autoLoadOnStart = true;

    public bool IsLoaded { get; private set; }

    public List<CharacterDataRow> Rows { get; private set; } = new List<CharacterDataRow>();
    private readonly Dictionary<string, CharacterDataRow> byId = new Dictionary<string, CharacterDataRow>();

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
                Debug.LogError($"[CharacterDataLoader] CSV 다운로드 실패: {req.error}\nURL={url}");
                yield break;
            }

            string csv = (req.downloadHandler.text ?? "").Trim('\uFEFF');
            var trimmed = csv.TrimStart();
            if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("[CharacterDataLoader] CSV가 아니라 HTML이 내려왔습니다. (시트 공유 권한 확인)");
                yield break;
            }

            ParseCsv(csv);
        }

        IsLoaded = true;
        OnLoaded?.Invoke();
        Debug.Log($"[CharacterDataLoader] 로드 완료. Rows={Rows.Count}");
    }

    private void ParseCsv(string csv)
    {
        var table = CsvUtil.Parse(csv);
        if (table.Count <= 1)
        {
            Debug.LogWarning("[CharacterDataLoader] CSV에 데이터가 없습니다.");
            return;
        }

        var headerMap = CsvUtil.BuildHeaderMap(table[0]);

        // 헤더 존재 여부 체크(정확히 일치해야 함)
        RequireHeader(headerMap, "CharID");
        RequireHeader(headerMap, "CharName");
        RequireHeader(headerMap, "CharStatID");
        RequireHeader(headerMap, "CharInfoID");
        RequireHeader(headerMap, "LevelBracketID1");
        RequireHeader(headerMap, "LevelBracketID2");
        RequireHeader(headerMap, "LevelBracketID3");
        RequireHeader(headerMap, "LevelBracketID4");
        RequireHeader(headerMap, "LevelBracketID5");
        RequireHeader(headerMap, "SkillID");

        for (int i = 1; i < table.Count; i++)
        {
            var r = table[i];
            if (r == null || r.Length == 0) continue;

            if (!CsvUtil.TryGetCell(r, headerMap, out string charId, "CharID")) continue;
            if (string.IsNullOrWhiteSpace(charId)) continue;

            var row = new CharacterDataRow();
            row.CharID = charId;

            CsvUtil.TryGetCell(r, headerMap, out row.CharName, "CharName");
            CsvUtil.TryGetCell(r, headerMap, out row.CharStatID, "CharStatID");
            CsvUtil.TryGetCell(r, headerMap, out row.CharInfoID, "CharInfoID");
            CsvUtil.TryGetCell(r, headerMap, out row.LevelBracketID1, "LevelBracketID1");
            CsvUtil.TryGetCell(r, headerMap, out row.LevelBracketID2, "LevelBracketID2");
            CsvUtil.TryGetCell(r, headerMap, out row.LevelBracketID3, "LevelBracketID3");
            CsvUtil.TryGetCell(r, headerMap, out row.LevelBracketID4, "LevelBracketID4");
            CsvUtil.TryGetCell(r, headerMap, out row.LevelBracketID5, "LevelBracketID5");
            CsvUtil.TryGetCell(r, headerMap, out row.SkillID, "SkillID");

            Rows.Add(row);
            byId[row.CharID] = row;
        }
    }

    private void RequireHeader(Dictionary<string, int> headerMap, string header)
    {
        var key = CsvUtil.NormalizeHeader(header);
        if (!headerMap.ContainsKey(key))
            Debug.LogError($"[CharacterDataLoader] 헤더 누락: {header} (시트 1행 확인)");
    }

    public bool TryGet(string charId, out CharacterDataRow row)
        => byId.TryGetValue(charId ?? "", out row);
}
