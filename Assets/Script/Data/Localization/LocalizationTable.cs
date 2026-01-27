using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LocalizationTable : MonoBehaviour
{
    [Header("Google Sheet")]
    public string spreadsheetId = "1uJ6PYDg8Obp-BNRm-w7IyVARKk0YpHDPOdTy5WRvuSU";
    public long gid = 0;

    [Header("Load")]
    public bool loadOnStart = true;
    public bool debugLog = false;

    private Dictionary<string, string> _koByKey = new Dictionary<string, string>();
    public bool IsReady { get; private set; } = false;

    private void Start()
    {
        if (loadOnStart)
            StartCoroutine(Load());
    }

    public System.Collections.IEnumerator Load()
    {
        IsReady = false;
        _koByKey.Clear();

        string url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={gid}";
        if (debugLog) Debug.Log($"[LocalizationTable] Download: {url}");

        using (var req = UnityWebRequest.Get(url))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"[LocalizationTable] Load failed: {req.error}");
                yield break;
            }

            string csv = req.downloadHandler.text;
            BuildFromCsv(csv);
        }

        IsReady = true;
        Debug.Log($"[LocalizationTable] Ready. keys={_koByKey.Count}");
    }

    public string GetKO(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        if (_koByKey.TryGetValue(key, out var v) && v != null) return v;
        return key; // 없으면 키 그대로(디버깅에 유리)
    }

    private void BuildFromCsv(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count <= 1) return;

        var header = rows[0];

        int keyCol = FindColumn(header, "Key");
        int koCol = FindColumn(header, "Korean(Ko)", "Korean (Ko)", "Korean");

        if (keyCol < 0)
        {
            Debug.LogError("[LocalizationTable] 'Key' column not found.");
            return;
        }
        if (koCol < 0)
        {
            Debug.LogError("[LocalizationTable] 'Korean(Ko)' column not found.");
            return;
        }

        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count <= keyCol) continue;

            string k = SafeCell(r, keyCol).Trim();
            if (string.IsNullOrWhiteSpace(k)) continue;

            string ko = SafeCell(r, koCol);
            _koByKey[k] = ko ?? "";
        }
    }

    private int FindColumn(List<string> header, params string[] names)
    {
        for (int i = 0; i < header.Count; i++)
        {
            string h = NormalizeHeader(header[i]);
            foreach (var n in names)
            {
                if (h == NormalizeHeader(n)) return i;
            }
        }
        return -1;
    }

    private string NormalizeHeader(string s)
    {
        if (s == null) return "";
        // 공백 제거 + 따옴표 제거 + 대소문자 무시
        return s.Replace("\"", "").Replace(" ", "").Trim().ToLowerInvariant();
    }

    private string SafeCell(List<string> row, int col)
    {
        if (row == null || col < 0 || col >= row.Count) return "";
        return row[col] ?? "";
    }

    // 간단하지만 쿼트/콤마 처리 되는 CSV 파서
    private List<List<string>> ParseCsv(string csv)
    {
        var result = new List<List<string>>();
        if (string.IsNullOrEmpty(csv)) return result;

        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];

            if (inQuotes)
            {
                if (c == '\"')
                {
                    // "" => "
                    if (i + 1 < csv.Length && csv[i + 1] == '\"')
                    {
                        cell.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }
            }
            else
            {
                if (c == '\"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                }
                else if (c == '\r')
                {
                    // ignore
                }
                else if (c == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                    result.Add(row);
                    row = new List<string>();
                }
                else
                {
                    cell.Append(c);
                }
            }
        }

        // last cell
        row.Add(cell.ToString());
        result.Add(row);

        return result;
    }
}
