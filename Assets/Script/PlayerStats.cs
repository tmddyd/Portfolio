using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerCharacterStats : MonoBehaviour, IUnitStatProvider
{
    [Header("Identity")]
    public string charId = "Char01";

    [Header("Service Ref")]
    public CharacterStatService statService;

    [Header("Options")]
    public bool applyOnStart = true;
    public bool refillHpOnApply = true;

    [Header("Percent Buff Stacking")]
    [Tooltip("true: 곱연산(1.1 * 1.2...), false: 합연산(1 + (10+20)/100).")]
    public bool useMultiplicativePercentBuffs = true;

    [Header("Crit Convert (for DamageFormula)")]
    public float crpToChanceScale = 0.01f;
    public float crdToBonusScale = 0.01f;

    [Header("Level (read)")]
    [SerializeField] private int level = 1;
    public int Level => level;

    [Header("Runtime Stats (Read Only in Play)")]
    public int maxHp;
    public int atk;
    public int def;
    public float spd;
    public int crd;
    public int crp;
    public int ar;
    public float attackSpeed;

    [Header("Runtime HP")]
    public int hp;

    public bool IsApplied { get; private set; }
    public event Action OnStatsChanged;

    // ✅ HP 처리 통합(플레이어 피격은 PlayerHealth가 담당)
    [Header("HP Authority")]
    public bool forwardDamageToPlayerHealth = true;
    public PlayerHealth playerHealth;

    private int baseMaxHp;
    private int baseAtk;
    private int baseDef;
    private float baseSpd;
    private int baseCrd;
    private int baseCrp;
    private int baseAr;
    private float baseAs;

    // =========================
    // ✅ Percent Buff Key (case-insensitive source)
    // =========================
    private readonly struct StatSourceKey : IEquatable<StatSourceKey>
    {
        public readonly ReferenceStat stat;
        public readonly string source;

        public StatSourceKey(ReferenceStat stat, string source)
        {
            this.stat = stat;
            this.source = source ?? string.Empty;
        }

        public bool Equals(StatSourceKey other)
            => stat == other.stat &&
               string.Equals(source, other.source, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj)
            => obj is StatSourceKey other && Equals(other);

        public override int GetHashCode()
        {
            int h1 = (int)stat;
            int h2 = StringComparer.OrdinalIgnoreCase.GetHashCode(source ?? string.Empty);
            return HashCode.Combine(h1, h2);
        }
    }

    // (stat, sourceSkillId) -> percent
    private readonly Dictionary<StatSourceKey, float> _buffPercents = new();

    [Header("Debug")]
    public bool logRebuild = false;

    private void Awake()
    {
        level = Mathf.Max(1, level);

        if (playerHealth == null)
            playerHealth = GetComponent<PlayerHealth>();
    }

    private IEnumerator Start()
    {
        if (!applyOnStart) yield break;

        if (statService == null)
        {
            Debug.LogError("[PlayerCharacterStats] statService가 비어있습니다. Inspector에서 할당하세요.");
            yield break;
        }

        while (!statService.IsReady)
            yield return null;

        ApplyForLevel(level, refillHpOnApply);
    }

    /// <summary>
    /// ✅ 레벨업 시 “base 스탯”을 갱신하고,
    /// 현재 보유 중인 스킬 % 버프를 base에 재적용하여 최종 스탯을 다시 만든다.
    /// </summary>
    public void ApplyForLevel(int newLevel, bool refillHp)
    {
        if (statService == null)
        {
            Debug.LogError("[PlayerCharacterStats] statService가 null입니다.");
            return;
        }

        if (!statService.IsReady)
        {
            Debug.LogWarning("[PlayerCharacterStats] statService가 아직 Ready가 아닙니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(charId))
        {
            Debug.LogError("[PlayerCharacterStats] charId가 비어있습니다.");
            return;
        }

        newLevel = Mathf.Max(1, newLevel);
        var final = statService.GetFinalStat(charId, newLevel);

        // ✅ 여기 값이 “레벨별 기본 스탯(base)”이 됩니다.
        baseMaxHp = final.MaxHp;
        baseAtk = final.Atk;
        baseDef = final.Def;
        baseSpd = final.Spd;
        baseCrd = final.Crd;
        baseCrp = final.Crp;
        baseAr = final.Ar;
        baseAs = final.As;

        level = newLevel;

        RebuildFinalStats(refillHp);

        IsApplied = true;
        Debug.Log($"[PlayerCharacterStats] Applied Base {charId} Lv{level} => {final}");
    }

    /// <summary>
    /// ✅ (stat, sourceSkillId) 단위로 %버프를 “설정(교체)”한다.
    /// 예) Skill_ATK_UP: 10% -> 20% 업그레이드면 10을 지우고 20으로 덮어쓴다.
    /// percent=0이면 제거.
    /// </summary>
    public void SetPercentBuff(ReferenceStat stat, string sourceSkillId, float percent)
    {
        if (string.IsNullOrWhiteSpace(sourceSkillId)) sourceSkillId = "UnknownSkill";

        var key = new StatSourceKey(stat, sourceSkillId);

        if (Mathf.Approximately(percent, 0f))
            _buffPercents.Remove(key);
        else
            _buffPercents[key] = percent;

        RebuildFinalStats(refillHp: false);
    }

    private float GetPercentFactor(ReferenceStat stat)
    {
        if (useMultiplicativePercentBuffs)
        {
            // 10% & 20% => 1.1 * 1.2 = 1.32
            float factor = 1f;
            foreach (var kv in _buffPercents)
            {
                if (kv.Key.stat != stat) continue;
                factor *= (1f + kv.Value / 100f);
            }
            return factor;
        }
        else
        {
            // 10% & 20% => 1 + (10+20)/100 = 1.3
            float sum = 0f;
            foreach (var kv in _buffPercents)
            {
                if (kv.Key.stat != stat) continue;
                sum += kv.Value;
            }
            return 1f + (sum / 100f);
        }
    }

    private float GetAdditivePercent(ReferenceStat stat)
    {
        float sum = 0f;
        foreach (var kv in _buffPercents)
        {
            if (kv.Key.stat != stat) continue;
            sum += kv.Value;
        }
        return sum;
    }

    private void RebuildFinalStats(bool refillHp)
    {
        // ✅ 최종 스탯은 항상 “현재 레벨의 base”에 “현재 보유중인 버프”를 적용해서 만든다.
        atk = Mathf.RoundToInt(baseAtk * GetPercentFactor(ReferenceStat.Atk));
        def = Mathf.RoundToInt(baseDef * GetPercentFactor(ReferenceStat.Def));
        spd = baseSpd * GetPercentFactor(ReferenceStat.Spd);
        crd = Mathf.RoundToInt(baseCrd * GetPercentFactor(ReferenceStat.Crd));
        ar = Mathf.RoundToInt(baseAr * GetPercentFactor(ReferenceStat.Ar));
        attackSpeed = baseAs * GetPercentFactor(ReferenceStat.As);
        maxHp = Mathf.Max(1, Mathf.RoundToInt(baseMaxHp * GetPercentFactor(ReferenceStat.MaxHp)));

        // ✅ 크리확률은 기존대로 “가산” 유지(현재 설계 유지)
        crp = Mathf.RoundToInt(baseCrp + GetAdditivePercent(ReferenceStat.Crp));

        // hp는 PlayerHealth가 권위라서 여기서는 최소한만 클램프
        if (!IsApplied) hp = maxHp;
        else if (refillHp) hp = maxHp;
        else hp = Mathf.Clamp(hp, 0, maxHp);

        OnStatsChanged?.Invoke();

        if (logRebuild)
        {
            Debug.Log($"[PlayerCharacterStats][Rebuild] Lv{level} => atk={atk}, def={def}, spd={spd:F2}, " +
                      $"crp={crp}, crd={crd}, ar={ar}, as={attackSpeed:F2}, maxHp={maxHp}");
        }
    }

    // =========================
    // ✅ 플레이어 피격 API
    // - 가능하면 PlayerHealth로 포워딩해서 i-frame/팝업/HP관리 통일
    // =========================
    public void TakeDamage(int rawDamage)
    {
        TakeDamage(rawDamage, attacker: null, isCrit: false);
    }

    public void TakeDamage(int rawDamage, Transform attacker)
    {
        TakeDamage(rawDamage, attacker, isCrit: false);
    }

    public void TakeDamage(int rawDamage, Transform attacker, bool isCrit)
    {
        if (forwardDamageToPlayerHealth && playerHealth != null)
        {
            // rawDamage 기준으로 방어력 적용까지 포함해 처리
            playerHealth.ApplyRawDamage(rawDamage, attacker, isCrit);
            return;
        }

        // (예외) PlayerHealth가 없는 경우만 직접 처리(팝업은 여기서 안 스폰함)
        int final = Mathf.Max(1, rawDamage - def);
        hp -= final;
        if (hp < 0) hp = 0;
        OnStatsChanged?.Invoke();
    }

    public void Heal(int amount)
    {
        hp = Mathf.Clamp(hp + Mathf.Max(0, amount), 0, maxHp);
        OnStatsChanged?.Invoke();
    }

    int IUnitStatProvider.MaxHp => Mathf.Max(1, maxHp);
    int IUnitStatProvider.Atk => Mathf.Max(0, atk);
    int IUnitStatProvider.Def => Mathf.Max(0, def);
    float IUnitStatProvider.CritChance01 => Mathf.Clamp01(crp * crpToChanceScale);
    float IUnitStatProvider.ExtraCritBonus => Mathf.Max(0f, crd * crdToBonusScale);
}
