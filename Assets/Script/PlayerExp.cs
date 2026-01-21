using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerExp : MonoBehaviour
{
    [Header("Level")]
    public int level = 1;
    public int currentExp = 0;
    public int needExpToNext = 10;

    [Header("Refs")]
    public LevelData levelData;

    [Header("UI (TMP) - Screenshot Style")]
    public Slider expSlider;
    public TMP_Text levelText;
    public TMP_Text expText;

    [Header("Text Format")]
    public string levelPrefix = "Level ";
    public bool showSpacesInExp = false;

    [Header("Debug")]
    public bool logExpGain = true;
    public bool logLevelUp = true;
    public bool logExpMultiplier = true;

    [Header("Apply Stats On LevelUp")]
    public PlayerCharacterStats characterStats;
    public bool applyStatsOnLevelUp = true;
    public bool refillHpOnLevelUp = true;
    public bool processPendingExpWhenDataLoaded = true;

    [Tooltip("ON이면 레벨업 while 루프 중에도(각 단계별) stats.ApplyForLevel을 호출해, OnLevelUp 이벤트 시점에 스탯이 이미 갱신된 상태를 보장합니다.\n" +
             "일반적으로는 OFF(기존 방식)로 둬도 무방합니다.")]
    public bool applyStatsInsideLevelUpLoop = false;

    [Header("Skill Select On LevelUp (Add)")]
    public PlayerSkillSystem skillSystem;
    public bool openSkillSelectOnLevelUp = true;

    [Header("ExpGain (Add)")]
    public bool applyExpGainMultiplier = true;

    public event Action<int, int, int> OnExpChanged;
    public event Action<int> OnLevelUp;

    private void Start()
    {
        level = Mathf.Max(1, level);
        currentExp = Mathf.Max(0, currentExp);

        if (characterStats == null)
            characterStats = GetComponent<PlayerCharacterStats>();

        if (skillSystem == null)
            skillSystem = GetComponent<PlayerSkillSystem>();

        if (levelData == null)
            levelData = FindObjectOfType<LevelData>();

        if (levelData != null && levelData.IsLoaded)
        {
            RefreshNeedExp();

            if (processPendingExpWhenDataLoaded)
                ProcessPendingExpWithLoadedData();

            RefreshUI();
        }
        else if (levelData != null)
        {
            levelData.OnLoaded += HandleLevelDataLoaded;
            RefreshUI();
        }
        else
        {
            Debug.LogWarning("[PlayerExp] LevelData를 찾지 못했습니다. (씬에 LevelData 오브젝트 배치 필요)");
            RefreshUI();
        }
    }

    private void OnDestroy()
    {
        if (levelData != null)
            levelData.OnLoaded -= HandleLevelDataLoaded;
    }

    private void HandleLevelDataLoaded()
    {
        if (levelData != null)
            levelData.OnLoaded -= HandleLevelDataLoaded;

        RefreshNeedExp();

        if (processPendingExpWhenDataLoaded)
            ProcessPendingExpWithLoadedData();

        RefreshUI();
    }

    private void RefreshNeedExp()
    {
        if (levelData == null) return;
        needExpToNext = Mathf.Max(1, levelData.GetNeedExpToNext(level));
    }

    /// <summary>
    /// 기본 호출: ExpGain 배율을 적용해서 경험치 추가
    /// </summary>
    public void AddExp(int amount)
    {
        AddExp(amount, applyMultiplier: true);
    }

    /// <summary>
    /// 필요하면 배율 적용을 끌 수 있는 오버로드
    /// - applyMultiplier=false 로 호출하면 ExpGain 미적용
    /// </summary>
    public void AddExp(int amount, bool applyMultiplier)
    {
        if (amount <= 0) return;

        int baseAmount = amount;

        // ExpGain 반영(선택사항)
        if (applyExpGainMultiplier && applyMultiplier && skillSystem != null)
        {
            float mul = Mathf.Max(0f, skillSystem.GetExpGainMultiplier());
            amount = Mathf.RoundToInt(amount * mul);

            if (logExpMultiplier)
                Debug.Log($"[PlayerExp] ExpGain mul={mul:F3} | base={baseAmount} -> applied={amount}");
        }

        if (logExpGain)
            Debug.Log($"[PlayerExp] EXP +{amount} (이전: Level {level} {currentExp}/{needExpToNext})");

        // 데이터 로드 전이면 누적만
        if (levelData == null || !levelData.IsLoaded)
        {
            currentExp += amount;
            currentExp = Mathf.Max(0, currentExp);
            FireChanged();
            return;
        }

        currentExp += amount;

        int levelUps = 0;

        while (currentExp >= needExpToNext)
        {
            currentExp -= needExpToNext;
            level = Mathf.Max(1, level + 1);
            levelUps++;

            RefreshNeedExp();

            // ✅ 옵션: 레벨업 단계별로 스탯을 먼저 갱신해서, OnLevelUp 이벤트 시점에 스탯이 이미 “해당 레벨 기준”이 되게 함
            if (applyStatsOnLevelUp && applyStatsInsideLevelUpLoop && characterStats != null)
                characterStats.ApplyForLevel(level, refillHp: false);

            OnLevelUp?.Invoke(level);

            if (logLevelUp)
                Debug.Log($"[PlayerExp] ★ Level Up -> Level {level} (남은EXP {currentExp}/{needExpToNext})");
        }

        // ✅ 기본 동작: 레벨업이 끝난 뒤 최종 레벨 기준으로 한 번만 base 갱신 + 버프 재계산
        if (applyStatsOnLevelUp && levelUps > 0)
        {
            if (characterStats != null)
                characterStats.ApplyForLevel(level, refillHpOnLevelUp);
            else
                Debug.LogWarning("[PlayerExp] PlayerCharacterStats가 없습니다.");
        }

        // ✅ 레벨업 횟수만큼 스킬 선택 UI 요청 (스탯 적용 이후가 안전)
        if (openSkillSelectOnLevelUp && levelUps > 0 && skillSystem != null)
            skillSystem.RequestSelections(levelUps);

        FireChanged();
    }

    private void ProcessPendingExpWithLoadedData()
    {
        if (levelData == null || !levelData.IsLoaded) return;

        RefreshNeedExp();

        int levelUps = 0;

        while (currentExp >= needExpToNext)
        {
            currentExp -= needExpToNext;
            level = Mathf.Max(1, level + 1);
            levelUps++;

            RefreshNeedExp();

            if (applyStatsOnLevelUp && applyStatsInsideLevelUpLoop && characterStats != null)
                characterStats.ApplyForLevel(level, refillHp: false);

            OnLevelUp?.Invoke(level);

            if (logLevelUp)
                Debug.Log($"[PlayerExp] ★ (Pending) Level Up -> Level {level} (남은EXP {currentExp}/{needExpToNext})");
        }

        if (applyStatsOnLevelUp && levelUps > 0)
        {
            if (characterStats != null)
                characterStats.ApplyForLevel(level, refillHpOnLevelUp);
        }

        if (openSkillSelectOnLevelUp && levelUps > 0 && skillSystem != null)
            skillSystem.RequestSelections(levelUps);

        FireChanged();
    }

    private void FireChanged()
    {
        OnExpChanged?.Invoke(level, currentExp, needExpToNext);
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (levelText != null)
            levelText.text = $"{levelPrefix}{level}";

        if (expText != null)
        {
            string sep = showSpacesInExp ? " / " : "/";
            expText.text = $"{currentExp}{sep}{needExpToNext}";
        }

        if (expSlider != null)
        {
            expSlider.minValue = 0f;
            expSlider.maxValue = needExpToNext;
            expSlider.value = Mathf.Clamp(currentExp, 0, needExpToNext);
        }
    }
}
