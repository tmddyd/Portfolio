using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class SkillSheetsLoader : MonoBehaviour
{
    [Header("Target DB")]
    public SkillDatabase db;

    [Header("Auto")]
    public bool autoLoadOnStart = true;

    [Header("Google Sheet IDs (default = user provided)")]
    public string skillSheetId = "1s2pxkTeWVoATbuxTCNnggZ8Vn-UMA6SeWyV3YxhY_cY";
    public int skillGid = 0;

    public string effectSheetId = "11njC10biCsaNtgZhaHZCnPIvaRcDQYR9wkI2UeWOWaE";
    public int effectGid = 0;

    public string effectValueSheetId = "1QfdYb_zBViblfKzkRi2-ZzRGQLVwPhzjalCX65kRDPo";
    public int effectValueGid = 0;

    [Header("Debug")]
    public bool logUrls = true;
    public bool logCounts = true;
    public bool dumpFirstRows = false;

    public bool IsLoaded { get; private set; } = false;
    public event Action OnLoaded;

    private void Reset()
    {
        db = FindObjectOfType<SkillDatabase>();
    }

    private IEnumerator Start()
    {
        if (!autoLoadOnStart) yield break;
        yield return LoadAll();
    }

    public IEnumerator LoadAll()
    {
        IsLoaded = false;

        if (db == null)
        {
            Debug.LogError("[SkillSheetsLoader] SkillDatabase reference missing.");
            yield break;
        }

        // clear
        db.skills.Clear();
        db.effects.Clear();
        db.effectValues.Clear();

        // 1) SkillData
        string skillTsv = null;
        yield return DownloadTsv(skillSheetId, skillGid, text => skillTsv = text);
        if (string.IsNullOrEmpty(skillTsv)) yield break;
        var skills = ParseSkillRows(skillTsv);
        db.skills.AddRange(skills);

        // 2) EffectData
        string effectTsv = null;
        yield return DownloadTsv(effectSheetId, effectGid, text => effectTsv = text);
        if (string.IsNullOrEmpty(effectTsv)) yield break;
        var effects = ParseEffectRows(effectTsv);
        db.effects.AddRange(effects);

        // 3) EffectValueData
        string evTsv = null;
        yield return DownloadTsv(effectValueSheetId, effectValueGid, text => evTsv = text);
        if (string.IsNullOrEmpty(evTsv)) yield break;
        var evs = ParseEffectValueRows(evTsv);
        db.effectValues.AddRange(evs);

        // build dicts
        db.Build();
        IsLoaded = true;

        if (logCounts)
        {
            Debug.Log($"[SkillSheetsLoader] Loaded OK. skills={db.skills.Count}, effects={db.effects.Count}, effectValues={db.effectValues.Count}");
        }

        if (dumpFirstRows)
        {
            if (db.skills.Count > 0)
                Debug.Log($"[SkillSheetsLoader] Skill sample: {db.skills[0].SkillID}, {db.skills[0].SkillName}, {db.skills[0].Effect}, {db.skills[0].CoolTime}, {db.skills[0].SkillExplain}");
            if (db.effects.Count > 0)
                Debug.Log($"[SkillSheetsLoader] Effect sample: {db.effects[0].EffectID}, {db.effects[0].TargetType}, {db.effects[0].EffectType}, {db.effects[0].ReferenseStat}");
            if (db.effectValues.Count > 0)
                Debug.Log($"[SkillSheetsLoader] EV sample: {db.effectValues[0].EffectValueID}, {db.effectValues[0].EffectID}, L{db.effectValues[0].Level}, {db.effectValues[0].ValueMin}~{db.effectValues[0].ValueMax}");
        }

        OnLoaded?.Invoke();
    }

    // =========================
    // Download
    // =========================
    private IEnumerator DownloadTsv(string sheetId, int gid, Action<string> onDone)
    {
        string url = $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=tsv&gid={gid}";
        if (logUrls) Debug.Log($"[SkillSheetsLoader] GET {url}");

        using var req = UnityWebRequest.Get(url);
        req.downloadHandler = new DownloadHandlerBuffer();
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[SkillSheetsLoader] Download failed: {req.error}\nURL={url}\n" +
                           $"(Check sharing permission: Anyone with the link / or publish to web)");
            onDone?.Invoke(null);
            yield break;
        }

        // UTF-8 강제 디코딩(한글 안정)
        var bytes = req.downloadHandler.data;
        string text = Encoding.UTF8.GetString(bytes);

        // 구글이 간혹 HTML 에러 페이지를 줄 때 방지
        if (text.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"[SkillSheetsLoader] Received HTML instead of TSV.\nURL={url}\n" +
                           $"Likely permission issue. Set sheet to public or publish it.");
            onDone?.Invoke(null);
            yield break;
        }

        onDone?.Invoke(text);
    }

    // =========================
    // TSV parsing helpers
    // =========================
    private static List<string[]> ParseTsv(string tsv)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(tsv)) return rows;

        var lines = tsv.Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 기본 TSV split (구글 export는 일반적으로 quote 없음)
            var cols = line.Split('\t');
            rows.Add(cols);
        }
        return rows;
    }

    private static Dictionary<string, int> BuildHeaderMap(string[] header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Length; i++)
        {
            var key = (header[i] ?? "").Trim();
            if (string.IsNullOrEmpty(key)) continue;
            if (!map.ContainsKey(key)) map.Add(key, i);
        }
        return map;
    }

    private static string Get(Dictionary<string, int> map, string[] row, string key, string def = "")
    {
        if (!map.TryGetValue(key, out int idx)) return def;
        if (idx < 0 || idx >= row.Length) return def;
        return (row[idx] ?? "").Trim();
    }

    private static int GetInt(Dictionary<string, int> map, string[] row, string key, int def = 0)
    {
        var s = Get(map, row, key, "");
        if (string.IsNullOrWhiteSpace(s) || s == "-") return def;
        if (int.TryParse(s, out var v)) return v;
        return def;
    }

    // =========================
    // Row converters
    // =========================
    private static List<SkillRow> ParseSkillRows(string tsv)
    {
        var raw = ParseTsv(tsv);
        var list = new List<SkillRow>();
        if (raw.Count == 0) return list;

        var header = raw[0];
        var map = BuildHeaderMap(header);

        foreach (var row in raw.Skip(1))
        {
            // 헤더가 2번 들어있는 케이스 방지
            // (사용자 예시: 첫 줄과 둘째 줄이 동일 헤더)
            string id = Get(map, row, "SkillID", "");
            if (string.IsNullOrEmpty(id)) continue;
            if (id.Equals("SkillID", StringComparison.OrdinalIgnoreCase)) continue;

            list.Add(new SkillRow
            {
                SkillID = id,
                SkillName = Get(map, row, "SkillName", ""),
                Effect = Get(map, row, "Effect", ""),          // 사용자 시트 컬럼명 Effect
                CoolTime = Get(map, row, "CoolTime", "-"),
                SkillExplain = Get(map, row, "SkillExplain", "")
            });
        }
        return list;
    }

    private static List<EffectRow> ParseEffectRows(string tsv)
    {
        var raw = ParseTsv(tsv);
        var list = new List<EffectRow>();
        if (raw.Count == 0) return list;

        var header = raw[0];
        var map = BuildHeaderMap(header);

        foreach (var row in raw.Skip(1))
        {
            string id = Get(map, row, "EffectID", "");
            if (string.IsNullOrEmpty(id)) continue;
            if (id.Equals("EffectID", StringComparison.OrdinalIgnoreCase)) continue;

            list.Add(new EffectRow
            {
                EffectID = id,
                EffectValue = Get(map, row, "EffectValue", ""),
                TargetType = Get(map, row, "TargetType", ""),
                EffectType = GetInt(map, row, "EffectType", 0),
                ReferenseStat = GetInt(map, row, "ReferenseStat", 0),
                DurationType = GetInt(map, row, "DurationType", 0),
                DurationTime = Get(map, row, "DurationTime", "-"),
            });
        }
        return list;
    }

    private static List<EffectValueRow> ParseEffectValueRows(string tsv)
    {
        var raw = ParseTsv(tsv);
        var list = new List<EffectValueRow>();
        if (raw.Count == 0) return list;

        var header = raw[0];
        var map = BuildHeaderMap(header);

        foreach (var row in raw.Skip(1))
        {
            string id = Get(map, row, "EffectValueID", "");
            if (string.IsNullOrEmpty(id)) continue;
            if (id.Equals("EffectValueID", StringComparison.OrdinalIgnoreCase)) continue;

            list.Add(new EffectValueRow
            {
                EffectValueID = id,
                EffectID = Get(map, row, "EffectID", ""),
                Level = GetInt(map, row, "Level", 1),
                ValueMax = GetInt(map, row, "ValueMax", 0),
                ValueMin = GetInt(map, row, "ValueMin", 0),
            });
        }
        return list;
    }
}
