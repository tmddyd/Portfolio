using System.Collections.Generic;
using UnityEngine;

public struct CharacterRuntimeStat
{
    public int MaxHp;
    public int Atk;
    public int Def;
    public float Spd;
    public int Crd;
    public int Crp;
    public int Ar;
    public float As;

    public override string ToString()
        => $"HP:{MaxHp}, ATK:{Atk}, DEF:{Def}, SPD:{Spd}, CRD:{Crd}, CRP:{Crp}, AR:{Ar}, AS:{As}";
}

public class CharacterStatService : MonoBehaviour
{
    public CharacterDataLoader characterLoader;
    public CharStatDataLoader statLoader;
    public LevelBracketDataLoader bracketLoader;

    [Header("Rules")]
    [Tooltip("최대 레벨 상한. (테스트/안전용) 0이면 제한 없음")]
    public int maxSupportedLevel = 50;

    [Tooltip("브라켓 누락/범위불일치 등을 경고 로그로 출력")]
    public bool logWarnings = true;

    public bool IsReady =>
        characterLoader != null && statLoader != null && bracketLoader != null &&
        characterLoader.IsLoaded && statLoader.IsLoaded && bracketLoader.IsLoaded;

    public CharacterRuntimeStat GetFinalStat(string charId, int level)
    {
        if (!IsReady)
        {
            Debug.LogError("[CharacterStatService] 로더가 아직 준비되지 않았습니다.");
            return default;
        }

        if (level < 1) level = 1;
        if (maxSupportedLevel > 0 && level > maxSupportedLevel)
        {
            if (logWarnings)
                Debug.LogWarning($"[CharacterStatService] level({level})이 maxSupportedLevel({maxSupportedLevel}) 초과. {maxSupportedLevel}로 클램프.");
            level = maxSupportedLevel;
        }

        if (!characterLoader.TryGet(charId, out var c))
        {
            Debug.LogError($"[CharacterStatService] CharID 없음: {charId}");
            return default;
        }

        if (!statLoader.TryGet(c.CharStatID, out var baseStat))
        {
            Debug.LogError($"[CharacterStatService] CharStatID 없음: {c.CharStatID} (CharID:{charId})");
            return default;
        }

        // 1) 기본 스탯
        var result = new CharacterRuntimeStat
        {
            MaxHp = baseStat.MaxHp,
            Atk = baseStat.Atk,
            Def = baseStat.Def,
            Spd = baseStat.Spd,
            Crd = baseStat.Crd,
            Crp = baseStat.Crp,
            Ar = baseStat.Ar,
            As = baseStat.As
        };

        if (level == 1) return result;

        // 2) 캐릭터가 가진 브라켓(ID1~ID5)을 실제 브라켓 Row로 캐싱
        var brackets = BuildBracketListForCharacter(c);

        if (brackets.Count == 0)
        {
            Debug.LogError($"[CharacterStatService] CharID:{charId} 의 브라켓이 비어있거나 전부 로드 실패.");
            return result; // 기본만 반환
        }

        // (선택) 범위 겹침/누락 검증 로그
        if (logWarnings)
            ValidateBracketRanges(charId, brackets);

        // 3) Lv2..LvN까지 레벨업 횟수만큼 누적
        for (int lv = 2; lv <= level; lv++)
        {
            var b = FindBracketByLevel(brackets, lv);
            if (b == null)
            {
                if (logWarnings)
                    Debug.LogWarning($"[CharacterStatService] CharID:{charId} 레벨 {lv}에 해당하는 브라켓이 없습니다. (MinLv/MaxLv 확인)");
                continue;
            }

            result.MaxHp += b.HpGain;
            result.Atk += b.AtkGain;
            result.Def += b.DefGain;
            result.Crd += b.CrdGain;
            result.Crp += b.CrpGain;
            result.As += b.AsGain;
        }

        return result;
    }

    /// <summary>
    /// CharacterDataRow의 LevelBracketID1~5를 읽어서, 실제 LevelBracketDataRow 리스트를 구성
    /// </summary>
    private List<LevelBracketDataRow> BuildBracketListForCharacter(CharacterDataRow c)
    {
        var list = new List<LevelBracketDataRow>(5);

        TryAddBracket(c.LevelBracketID1, list);
        TryAddBracket(c.LevelBracketID2, list);
        TryAddBracket(c.LevelBracketID3, list);
        TryAddBracket(c.LevelBracketID4, list);
        TryAddBracket(c.LevelBracketID5, list);

        // MinLv 기준으로 정렬(Find가 안정적으로 동작하도록)
        list.Sort((a, b) => a.MinLv.CompareTo(b.MinLv));
        return list;
    }

    private void TryAddBracket(string bracketId, List<LevelBracketDataRow> list)
    {
        if (string.IsNullOrWhiteSpace(bracketId)) return;

        if (!bracketLoader.TryGet(bracketId, out var row))
        {
            if (logWarnings)
                Debug.LogWarning($"[CharacterStatService] LevelBracketID '{bracketId}'를 찾을 수 없습니다. (LevelBracketData 시트 확인)");
            return;
        }

        list.Add(row);
    }

    /// <summary>
    /// 레벨에 해당하는 브라켓을 MinLv/MaxLv로 찾기
    /// </summary>
    private LevelBracketDataRow FindBracketByLevel(List<LevelBracketDataRow> brackets, int level)
    {
        // 브라켓 수가 적으니 선형 탐색으로 충분
        for (int i = 0; i < brackets.Count; i++)
        {
            var b = brackets[i];
            if (level >= b.MinLv && level <= b.MaxLv)
                return b;
        }
        return null;
    }

    /// <summary>
    /// (선택) 브라켓 구간이 겹치거나 끊기는 경우 경고
    /// </summary>
    private void ValidateBracketRanges(string charId, List<LevelBracketDataRow> brackets)
    {
        for (int i = 0; i < brackets.Count; i++)
        {
            var b = brackets[i];

            if (b.MinLv > b.MaxLv)
            {
                Debug.LogWarning($"[CharacterStatService] CharID:{charId} 브라켓 '{b.LevelBracketID}' 범위 이상: {b.MinLv}~{b.MaxLv}");
            }

            if (i > 0)
            {
                var prev = brackets[i - 1];

                // 겹침
                if (b.MinLv <= prev.MaxLv)
                {
                    Debug.LogWarning(
                        $"[CharacterStatService] CharID:{charId} 브라켓 범위 겹침 가능: " +
                        $"'{prev.LevelBracketID}'({prev.MinLv}~{prev.MaxLv}) and '{b.LevelBracketID}'({b.MinLv}~{b.MaxLv})"
                    );
                }

                // 끊김(연속이 아닐 때)
                if (b.MinLv > prev.MaxLv + 1)
                {
                    Debug.LogWarning(
                        $"[CharacterStatService] CharID:{charId} 브라켓 범위 끊김 가능: " +
                        $"'{prev.LevelBracketID}'({prev.MinLv}~{prev.MaxLv}) -> '{b.LevelBracketID}'({b.MinLv}~{b.MaxLv})"
                    );
                }
            }
        }
    }
}
