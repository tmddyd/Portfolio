using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class LevelData : MonoBehaviour
{
    [Header("Google Sheet CSV")]
    public string spreadsheetId = "1pGQ44GfW6AgTMdXRRg43geyMa2_fXwQuL390ono__qg";
    public int gid = 0;

    [Header("Runtime")]
    public bool autoLoadOnStart = true;

    public bool IsLoaded { get; private set; }
    public int MaxLevel { get; private set; } = 1;

    // level -> needExpToNext
    private readonly Dictionary<int, int> needExpByLevel = new Dictionary<int, int>();

    public event Action OnLoaded;

    private void Start()
    {
        if (autoLoadOnStart)
            StartCoroutine(Load());
    }

    public IEnumerator Load()
    {
        IsLoaded = false;
        needExpByLevel.Clear();
        MaxLevel = 1;

        string url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={gid}";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"[LevelData] CSV 다운로드 실패: {req.error}\nURL={url}");
                yield break;
            }

            ParseCsv(req.downloadHandler.text);
        }

        IsLoaded = true;
        OnLoaded?.Invoke();
        Debug.Log($"[LevelData] 로드 완료. MaxLevel={MaxLevel}, Rows={needExpByLevel.Count}");
    }

    private void ParseCsv(string csv)
    {
        // 기대 헤더: ChoiceLevel, ChoiceExp
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1)
        {
            Debug.LogWarning("[LevelData] CSV에 데이터가 없습니다.");
            return;
        }

        string[] header = lines[0].Split(',');
        int idxLevel = Array.IndexOf(header, "ChoiceLevel");
        int idxExp = Array.IndexOf(header, "ChoiceExp");

        if (idxLevel < 0 || idxExp < 0)
        {
            Debug.LogError("[LevelData] 헤더가 일치하지 않습니다. (ChoiceLevel, ChoiceExp 확인)");
            return;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] cols = line.Split(',');
            if (cols.Length <= Mathf.Max(idxLevel, idxExp)) continue;

            if (!int.TryParse(cols[idxLevel].Trim(), out int level)) continue;
            if (!int.TryParse(cols[idxExp].Trim(), out int needExp)) continue;

            needExp = Mathf.Max(0, needExp);
            needExpByLevel[level] = needExp;

            if (level > MaxLevel) MaxLevel = level;
        }
    }

    public int GetNeedExpToNext(int level)
    {
        if (needExpByLevel.TryGetValue(level, out int need))
            return need;

        // 데이터가 없으면 레벨업 방지용으로 큰 값 반환
        return int.MaxValue / 4;
    }
}
