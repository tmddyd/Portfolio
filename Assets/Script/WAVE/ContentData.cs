using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class ContentData : MonoBehaviour
{
    [Header("Google Sheet")]
    public string spreadsheetId;
    public long gid = 0;

    [Header("Default (Auto Fill)")]
    [SerializeField]
    private string defaultSpreadsheetId =
        "19_xixJQJPhu3inZd0wBQrSJcTnJRSHDhJwbEt3lGVvM";
    [SerializeField] private long defaultGid = 0;

    [Header("Runtime Data")]
    public Dictionary<string, ContentRow> byKey =
        new Dictionary<string, ContentRow>(StringComparer.OrdinalIgnoreCase);

    public string CsvUrl =>
        $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={gid}";

    // 컴포넌트 추가 시 1회 자동 입력
    private void Reset()
    {
        ApplyDefaultsIfEmpty();
    }

#if UNITY_EDITOR
    // 인스펙터에서 값이 비었을 때 자동 복구 (원치 않으면 삭제 가능)
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
            Debug.LogError("[ContentData] spreadsheetId가 비어있습니다.");
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
                Debug.LogError($"[ContentData] CSV 다운로드 실패: {req.error}\nURL: {CsvUrl}");
                yield break;
            }

            Parse(req.downloadHandler.text);
        }
    }

    private void Parse(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            Debug.LogError("[ContentData] CSV 비어있음");
            return;
        }

        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count < 2)
        {
            Debug.LogError("[ContentData] 헤더/데이터 부족");
            return;
        }

        var header = CsvUtil.ParseLine(lines[0]).Select(CsvUtil.NormalizeHeader).ToList();
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++) col[header[i]] = i;

        int Req(string name)
        {
            if (!col.TryGetValue(name, out var idx))
                throw new Exception($"[ContentData] '{name}' 컬럼 없음. 헤더: {string.Join(", ", header)}");
            return idx;
        }

        int iContentId = Req("ContentID");
        int iStep = Req("Step");
        int iStageId = Req("StageID");

        for (int r = 1; r < lines.Count; r++)
        {
            var f = CsvUtil.ParseLine(lines[r]);
            int need = Mathf.Max(iStageId, Mathf.Max(iContentId, iStep));
            if (f.Count <= need) continue;

            string contentId = (f[iContentId] ?? "").Trim();
            int step = CsvUtil.ToInt(f[iStep], 0);
            string stageId = (f[iStageId] ?? "").Trim();

            if (string.IsNullOrWhiteSpace(contentId) || step <= 0 || string.IsNullOrWhiteSpace(stageId))
                continue;

            var row = new ContentRow
            {
                ContentID = contentId,
                Step = step,
                StageID = stageId
            };

            byKey[MakeKey(contentId, step)] = row;
        }

        Debug.Log($"[ContentData] 로드 완료: {byKey.Count} rows");
    }

    public bool TryGetStageId(string contentId, int step, out string stageId)
    {
        stageId = null;
        if (string.IsNullOrWhiteSpace(contentId) || step <= 0) return false;

        if (byKey.TryGetValue(MakeKey(contentId.Trim(), step), out var row))
        {
            stageId = row.StageID;
            return !string.IsNullOrWhiteSpace(stageId);
        }
        return false;
    }

    private static string MakeKey(string contentId, int step) => $"{contentId}__{step}";
}

[Serializable]
public class ContentRow
{
    public string ContentID;
    public int Step;
    public string StageID;
}
