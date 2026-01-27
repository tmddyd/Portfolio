using UnityEngine;

public class MonsterAttack : MonoBehaviour
{
    [Header("Refs")]
    public MonsterStat attackerStat;

    [Tooltip("공격 대상(플레이어). 비어있으면 targetTag로 자동 탐색")]
    public Transform target;

    [Tooltip("target이 비어있을 때 찾을 태그")]
    public string targetTag = "Player";

    [Tooltip("target 자동 재탐색 주기(초)")]
    public float refindInterval = 1.0f;

    [Header("Sheet -> Range/Interval")]
    public bool useSheetArAsRange = true;
    public float arToRangeScale = 1f;   // Ar * scale => meters
    public float minRange = 0.25f;
    public float maxRange = 50f;

    [Tooltip("As를 '공격 가능한 초(쿨타임)'로 해석합니다. 예: As=1이면 1초마다 1회")]
    public bool useSheetAsAsIntervalSeconds = true;

    public float defaultHitInterval = 1f; // 시트 로드 전/실패 시 기본값
    public float minHitInterval = 0.05f;

    [Header("Damage Formula Params")]
    public float attackCoef = 1f;
    public float defenseK = 100f;
    public float baseCritBonus = 0.5f;
    public float extraDamageMul = 1f;

    [Header("Distance Policy")]
    [Tooltip("Y축 무시(탑다운/뱀서류류 권장)")]
    public bool ignoreY = true;

    [Header("Debug")]
    public bool logApply = true;
    public bool logDamage = true;

    [Header("Gizmos")]
    public bool drawRangeGizmo = true;
    public bool drawOnlyWhenSelected = true;

    private IUnitStatProvider attackerProvider;

    private float cachedRange = 1f;
    private float cachedInterval = 1f;

    private float nextAttackTime = 0f;
    private float nextFindTime = 0f;

    // 캐싱(매번 GetComponent 하지 않도록)
    private PlayerHealth cachedPlayerHealth;
    private IUnitStatProvider cachedDefenderProvider;

    private void Reset()
    {
        attackerStat = GetComponentInParent<MonsterStat>();
    }

    private void Awake()
    {
        if (attackerStat == null)
            attackerStat = GetComponentInParent<MonsterStat>();

        attackerProvider = attackerStat as IUnitStatProvider;
        if (attackerProvider == null)
            Debug.LogError("[MonsterAttack] MonsterStat이 없거나 IUnitStatProvider를 구현하지 않았습니다.");

        // 시트 로드 후 1회 적용
        if (attackerStat != null)
            attackerStat.OnSheetLoaded += ApplyFromSheet;
    }

    private void OnDestroy()
    {
        if (attackerStat != null)
            attackerStat.OnSheetLoaded -= ApplyFromSheet;
    }

    private void Start()
    {
        ApplyFromSheet();
        FindTargetIfNeeded(force: true);
        nextAttackTime = Time.time; // 시작 즉시 1타 가능
    }

    private void Update()
    {
        FindTargetIfNeeded(force: false);
        if (target == null) return;

        // 사거리 내인지 체크
        if (!IsInRange(target.position)) return;

        // 공격 타이밍(As) 체크
        if (Time.time < nextAttackTime) return;

        // 대상 컴포넌트 캐싱
        if (!EnsureTargetComponents()) return;

        // 데미지 계산
        var res = DamageFormula.Compute(
            attacker: attackerProvider,
            defender: cachedDefenderProvider,
            attackCoef: attackCoef,
            defenseK: defenseK,
            baseCritBonus: baseCritBonus,
            extraDamageMul: extraDamageMul
        );

        int beforeHp = cachedPlayerHealth.Hp;
        int beforeMax = cachedPlayerHealth.MaxHp;

        // 적용
        cachedPlayerHealth.ApplyFinalDamage(res.damage);

        if (logDamage)
        {
            Debug.Log(
                $"[DMG][Send][Monster] '{name}' -> Player='{cachedPlayerHealth.name}' " +
                $"HP {beforeHp}/{beforeMax} -> {cachedPlayerHealth.Hp}/{cachedPlayerHealth.MaxHp} (-{res.damage}) " +
                $"crit={res.isCrit} raw={res.raw:F2} range={cachedRange:F2} interval={cachedInterval:F2}s"
            );
        }

        // 다음 공격 시간 갱신
        nextAttackTime = Time.time + cachedInterval;
    }

    private void ApplyFromSheet()
    {
        float range = Mathf.Clamp(1f * arToRangeScale, minRange, maxRange);
        float interval = Mathf.Max(minHitInterval, defaultHitInterval);

        if (attackerStat != null && attackerStat.IsSheetLoaded)
        {
            if (useSheetArAsRange)
                range = Mathf.Clamp(attackerStat.SheetAr * arToRangeScale, minRange, maxRange);

            if (useSheetAsAsIntervalSeconds)
                interval = Mathf.Max(minHitInterval, attackerStat.SheetAs);
            else
            {
                // (옵션) As를 "초당 공격횟수"로 해석하고 싶으면 여기 사용
                float rate = Mathf.Max(0.0001f, attackerStat.SheetAs);
                interval = Mathf.Clamp(1f / rate, minHitInterval, 10f);
            }
        }

        cachedRange = range;
        cachedInterval = interval;

        if (logApply)
        {
            Debug.Log($"[MonsterAttack][Apply] range(Ar)={cachedRange:F2}, interval(As)={cachedInterval:F2} " +
                      $"(SheetLoaded={attackerStat != null && attackerStat.IsSheetLoaded}, Ar={attackerStat?.SheetAr}, As={attackerStat?.SheetAs})");
        }
    }

    private void FindTargetIfNeeded(bool force)
    {
        if (!force && target != null) return;
        if (!force && Time.time < nextFindTime) return;

        nextFindTime = Time.time + Mathf.Max(0.1f, refindInterval);

        if (string.IsNullOrWhiteSpace(targetTag)) return;

        var go = GameObject.FindWithTag(targetTag);
        target = go != null ? go.transform : null;

        // 타겟이 바뀌면 캐시 초기화
        cachedPlayerHealth = null;
        cachedDefenderProvider = null;
    }

    private bool EnsureTargetComponents()
    {
        if (cachedPlayerHealth == null)
            cachedPlayerHealth = target.GetComponentInParent<PlayerHealth>();

        if (cachedPlayerHealth == null || cachedPlayerHealth.IsDead)
            return false;

        if (cachedDefenderProvider == null)
        {
            // PlayerCharacterStats가 IUnitStatProvider를 구현하고 있으므로 캐스팅 가능
            var pcs = target.GetComponentInParent<PlayerCharacterStats>();
            cachedDefenderProvider = pcs as IUnitStatProvider;
        }

        if (cachedDefenderProvider == null)
            return false;

        return true;
    }

    private bool IsInRange(Vector3 targetPos)
    {
        Vector3 a = transform.position;
        Vector3 b = targetPos;

        if (ignoreY)
        {
            a.y = 0f;
            b.y = 0f;
        }

        float r = Mathf.Max(0.01f, cachedRange);
        float rr = r * r;
        float d2 = (b - a).sqrMagnitude;
        return d2 <= rr;
    }

    // =========================
    // Gizmos: 빨간 원으로 사정거리 표시
    // =========================
    private void OnDrawGizmos()
    {
        if (!drawRangeGizmo) return;
        if (drawOnlyWhenSelected) return;
        DrawRangeGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawRangeGizmo) return;
        if (!drawOnlyWhenSelected) return;
        DrawRangeGizmo();
    }

    private void DrawRangeGizmo()
    {
        float r = cachedRange > 0f ? cachedRange : 1f;

        Vector3 center = transform.position;
        center.y += 0.05f;

        Gizmos.color = Color.red;
        DrawWireCircleXZ(center, r, 64);
    }

    private void DrawWireCircleXZ(Vector3 center, float radius, int segments)
    {
        if (segments < 8) segments = 8;

        float step = (Mathf.PI * 2f) / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float a = step * i;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
