using System;
using UnityEngine;

public class MonsterStat : MonoBehaviour, IUnitStatProvider
{
    [Header("Base Stats (원본)")]
    public int baseHP = 100;
    public int baseAtk = 10;
    public int baseDef = 0;

    // ✅ 몬스터 기본 점수(데이터테이블 M_Score를 넣어둘 변수)
    public int baseScore = 10;

    [Header("Runtime Stats (배율 적용 후)")]
    public int maxHP;
    public int atk;
    public int def;
    public int hp;

    // ✅ (baseScore * ScoreMul) 최종 처치 점수
    public int killScore;

    [Header("Runtime Multipliers (Debug)")]
    public float hpMulApplied = 1f;
    public float atkMulApplied = 1f;
    public float defMulApplied = 1f;
    public float scoreMulApplied = 1f;

    public event Action<MonsterStat> OnDied;

    // ✅ HP 변경/피격 이벤트 (기존 유지)
    public event Action<int, int> OnHpChanged; // (current, max)
    public event Action OnDamaged;             // "피격했다" 신호 (UI 표시용)

    // =========================
    // ✅ Sheet Load Options
    // =========================
    [Header("Sheet (CharStatData)")]
    public CharStatDataLoader statLoader;
    public string monsterStatId = "MStat01";

    [Tooltip("시트(MStat01)의 MaxHp/Atk/Def를 baseHP/baseAtk/baseDef로 주입할지")]
    public bool loadBaseStatsFromSheet = true;

    [Tooltip("시트(MStat01)의 Crp/Crd를 치명 계산에 사용할지")]
    public bool loadCritFromSheet = true;

    [Header("Crit Convert (for DamageFormula)")]
    public float crpToChanceScale = 0.01f;
    public float crdToBonusScale = 0.01f;

    // ✅ (선택) 이동속도 시트 적용을 Monster.cs가 받기 쉬우라고 여기서는 값만 보관/노출
    // (Monster.cs에서 moveSpeed에 반영하도록)
    private CharStatDataRow sheetRow;
    private int sheetCrp = 0;
    private int sheetCrd = 0;

    // ✅ 외부에서 시트 로드 완료를 알 수 있게
    public bool IsSheetLoaded { get; private set; } = false;
    public event Action OnSheetLoaded;

    // ✅ 시트 값 외부 노출 (필요한 것만)
    public float SheetSpd => sheetRow != null ? sheetRow.Spd : 0f;
    public int SheetMaxHp => sheetRow != null ? sheetRow.MaxHp : 0;
    public int SheetAtk => sheetRow != null ? sheetRow.Atk : 0;
    public int SheetDef => sheetRow != null ? sheetRow.Def : 0;
    public int SheetCrd => sheetRow != null ? sheetRow.Crd : 0;
    public int SheetCrp => sheetRow != null ? sheetRow.Crp : 0;
    public int SheetAr => sheetRow != null ? sheetRow.Ar : 0;   // ※ Ar을 0.5처럼 쓰려면 CharStatDataRow/Loader에서 Ar을 float로 바꿔야 합니다.
    public float SheetAs => sheetRow != null ? sheetRow.As : 0f;

    // 배율이 먼저 적용될 수 있으므로 저장해뒀다가, 시트 로드 후 재적용
    private bool multipliersAppliedOnce = false;
    private float lastHpMul = 1f, lastAtkMul = 1f, lastDefMul = 1f, lastScoreMul = 1f;

    private void Start()
    {
        if (statLoader == null)
            statLoader = FindObjectOfType<CharStatDataLoader>();

        if (statLoader == null) return;

        if (statLoader.IsLoaded) LoadSheetNow();
        else statLoader.OnLoaded += LoadSheetNow;
    }

    private void OnDestroy()
    {
        if (statLoader != null)
            statLoader.OnLoaded -= LoadSheetNow;
    }

    private void LoadSheetNow()
    {
        if (statLoader == null) return;

        statLoader.OnLoaded -= LoadSheetNow;

        if (!statLoader.TryGet(monsterStatId, out var r))
            return;

        sheetRow = r;
        IsSheetLoaded = true;

        // ✅ (선택) base 스탯을 시트에서 주입
        if (loadBaseStatsFromSheet)
        {
            baseHP = r.MaxHp;
            baseAtk = r.Atk;
            baseDef = r.Def;
        }

        // ✅ (선택) 치명 수치(시트 기반)
        if (loadCritFromSheet)
        {
            sheetCrp = r.Crp;
            sheetCrd = r.Crd;
        }

        // ✅ 배율이 이미 적용된 상태였다면, 시트 base로 다시 재계산
        if (multipliersAppliedOnce)
        {
            ApplyWaveMultipliers(lastHpMul, lastAtkMul, lastDefMul, lastScoreMul);
        }

        // ✅ 외부 알림 (Monster.cs / MonsterAttack.cs 등이 여기서 반영 가능)
        OnSheetLoaded?.Invoke();
    }

    // ------------------------------------------------------------
    // 기존 시그니처 유지
    // ------------------------------------------------------------
    public void ApplyWaveMultipliers(float hpMul, float atkMul, float defMul)
    {
        ApplyWaveMultipliers(hpMul, atkMul, defMul, 1f);
    }

    public void ApplyWaveMultipliers(float hpMul, float atkMul, float defMul, float scoreMul)
    {
        // ✅ (추가) 마지막 배율 저장
        multipliersAppliedOnce = true;
        lastHpMul = hpMul;
        lastAtkMul = atkMul;
        lastDefMul = defMul;
        lastScoreMul = scoreMul;

        // ---- 이하 기존 로직 유지 ----
        hpMulApplied = hpMul;
        atkMulApplied = atkMul;
        defMulApplied = defMul;
        scoreMulApplied = scoreMul;

        maxHP = Mathf.Max(1, Mathf.RoundToInt(baseHP * hpMul));
        atk = Mathf.Max(0, Mathf.RoundToInt(baseAtk * atkMul));
        def = Mathf.Max(0, Mathf.RoundToInt(baseDef * defMul));
        hp = maxHP;

        killScore = Mathf.Max(0, Mathf.RoundToInt(baseScore * scoreMul));

        OnHpChanged?.Invoke(hp, maxHP);
    }

    public void TakeDamage(int dmg)
    {
        int final = Mathf.Max(1, dmg - def);

        if (hp <= 0) return;
        if (maxHP <= 0) maxHP = Mathf.Max(1, baseHP);

        hp -= final;

        OnDamaged?.Invoke();
        OnHpChanged?.Invoke(hp, maxHP);

        if (hp <= 0)
            Die();
    }

    // ✅ 공식으로 계산된 "최종 데미지"를 바로 넣는 함수 (추가)
    public void TakeFinalDamage(int finalDamage)
    {
        int d = Mathf.Max(1, finalDamage);

        if (hp <= 0) return;
        if (maxHP <= 0) maxHP = Mathf.Max(1, baseHP);

        hp -= d;

        OnDamaged?.Invoke();
        OnHpChanged?.Invoke(hp, maxHP);

        if (hp <= 0)
            Die();
    }

    public void Die()
    {
        OnDied?.Invoke(this);
        Destroy(gameObject);
    }

    // =========================
    // ✅ IUnitStatProvider 구현 (통합)
    // =========================
    int IUnitStatProvider.MaxHp => Mathf.Max(1, maxHP > 0 ? maxHP : baseHP);
    int IUnitStatProvider.Atk => Mathf.Max(0, atk);
    int IUnitStatProvider.Def => Mathf.Max(0, def);

    float IUnitStatProvider.CritChance01
    {
        get
        {
            if (!loadCritFromSheet) return 0f;
            return Mathf.Clamp01(sheetCrp * crpToChanceScale);
        }
    }

    float IUnitStatProvider.ExtraCritBonus
    {
        get
        {
            if (!loadCritFromSheet) return 0f;
            return Mathf.Max(0f, sheetCrd * crdToBonusScale);
        }
    }
}
