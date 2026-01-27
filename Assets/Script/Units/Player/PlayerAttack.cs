using System.Collections.Generic;
using UnityEngine;

public class AutoTargetArcAttack : MonoBehaviour
{
    [Header("공격 설정")]
    public float attackRadius = 3f;
    public float attackAngle = 90f;

    [Tooltip("Stat(As)을 못 읽을 때 사용할 기본 쿨타임(초)")]
    public float attackCooldown = 1f;

    [Header("Stat -> Range/Cooldown")]
    public bool useStatArAsRadius = true;
    public float arToRadiusScale = 1f;
    public float minRadius = 0.25f;
    public float maxRadius = 50f;

    public bool useStatAsAsAttacksPerSecond = true;
    public float minCooldown = 0.05f;

    [Header("Damage Formula Params")]
    public PlayerCharacterStats attackerStats;
    public float attackCoef = 1f;
    public float defenseK = 100f;
    public float baseCritBonus = 0.5f;
    public float extraDamageMul = 1f;

    [Header("Target Filter")]
    public string targetTag = "Monster";

    [Header("Debug (Damage Logs)")]
    public bool logDamageGive = true;
    public bool logDamageReceive = true;
    public bool logIncludeRaw = true;
    public bool logOnlyWhenHit = true;

    [Header("Debug (Attack Timing)")]
    public bool logApplyRangeCooldown = false;

    [Header("Crit Policy")]
    public bool rollCritOncePerAttack = true;

    [Header("Skill System (Optional)")]
    public PlayerSkillSystem skillSystem;

    [Header("Animation (Attack Combo)")]
    public Animator animator;
    public int animatorLayer = 0;
    public string attack1StateName = "Attack1";
    public string attack2StateName = "Attack2";
    public string attack3StateName = "Attack3";
    public float crossFadeTime = 0.05f;
    public float comboResetSeconds = 0f;
    public bool resetComboWhenNoHit = true;
    public bool loopAfterThird = true;

    [Header("Damage Popup (World Text)")]
    public bool spawnDamagePopup = true;
    public DamagePopupSpawner damagePopupSpawner;
    public Transform popupAttackerOverride;

    private float timer;
    private float cachedRadius;
    private float cachedCooldown;

    private int comboIndex = 0;
    private float lastHitAttackTime = -999f;

    private void Reset()
    {
        attackerStats = GetComponent<PlayerCharacterStats>();
        if (skillSystem == null) skillSystem = GetComponent<PlayerSkillSystem>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (damagePopupSpawner == null) damagePopupSpawner = DamagePopupSpawner.Instance;
    }

    private void Awake()
    {
        if (attackerStats == null)
            attackerStats = GetComponent<PlayerCharacterStats>();

        if (attackerStats == null)
            Debug.LogError("[AutoTargetArcAttack] PlayerCharacterStats가 필요합니다.");

        if (skillSystem == null)
            skillSystem = GetComponent<PlayerSkillSystem>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (damagePopupSpawner == null)
            damagePopupSpawner = DamagePopupSpawner.Instance;
    }

    private void OnEnable()
    {
        if (attackerStats == null) attackerStats = GetComponent<PlayerCharacterStats>();
        if (attackerStats != null) attackerStats.OnStatsChanged += RefreshFromStats;

        RefreshFromStats();
        ResetCombo();
    }

    private void OnDisable()
    {
        if (attackerStats != null) attackerStats.OnStatsChanged -= RefreshFromStats;
        ResetCombo();
    }

    private void Start()
    {
        RefreshFromStats();
    }

    public void ForceRefreshFromStats() => RefreshFromStats();

    private void RefreshFromStats()
    {
        float r = attackRadius;
        if (useStatArAsRadius && attackerStats != null && attackerStats.ar > 0)
            r = Mathf.Clamp(attackerStats.ar * arToRadiusScale, minRadius, maxRadius);

        cachedRadius = Mathf.Max(0.01f, r);
        attackRadius = cachedRadius;

        cachedCooldown = GetCooldownSeconds();

        if (logApplyRangeCooldown)
        {
            float asRate = (attackerStats != null) ? attackerStats.attackSpeed : 0f;
            Debug.Log($"[PlayerAttack][Apply] Ar={attackerStats?.ar} => radius={cachedRadius:F2} | As(rate)={asRate:F3} => cooldown={cachedCooldown:F3}s");
        }
    }

    private float GetCooldownSeconds()
    {
        float fallback = Mathf.Max(minCooldown, attackCooldown);

        if (!useStatAsAsAttacksPerSecond) return fallback;
        if (attackerStats == null) return fallback;

        float asRate = attackerStats.attackSpeed;
        if (asRate <= 0f) return fallback;

        float cd = 1f / Mathf.Max(0.0001f, asRate);
        return Mathf.Max(minCooldown, cd);
    }

    private void Update()
    {
        float cd = GetCooldownSeconds();

        timer += Time.deltaTime;
        if (timer >= cd)
        {
            timer = 0f;

            bool didHit = PerformArcAttack();
            if (didHit) PlayAttackComboAnimation();
            else if (resetComboWhenNoHit) ResetCombo();
        }
    }

    private bool PerformArcAttack()
    {
        if (attackerStats == null) return false;

        bool didApplyDamage = false;

        bool? forceCrit = null;
        if (rollCritOncePerAttack)
        {
            float critChance = ((IUnitStatProvider)attackerStats).CritChance01;
            bool rolledCrit = UnityEngine.Random.value < critChance;
            forceCrit = rolledCrit;
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, cachedRadius);
        HashSet<int> attacked = new HashSet<int>();

        foreach (Collider hit in hits)
        {
            if (!PassTargetFilter(hit)) continue;

            Monster m = hit.GetComponentInParent<Monster>();
            if (m == null) continue;

            int id = m.gameObject.GetInstanceID();
            if (attacked.Contains(id)) continue;
            attacked.Add(id);

            Vector3 dir = (m.transform.position - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) continue;

            float angle = Vector3.Angle(transform.forward, dir.normalized);
            if (angle > attackAngle * 0.5f)
            {
                if (!logOnlyWhenHit && logDamageGive)
                    Debug.Log($"[DMG][Give][Player->Monster] out of arc. Monster='{m.name}', angle={angle:F1}");
                continue;
            }

            MonsterStat ms = m.GetComponent<MonsterStat>();
            IUnitStatProvider defender = ms as IUnitStatProvider;

            int beforeHp = (ms != null) ? ms.hp : -1;
            int beforeMax = (ms != null) ? ms.maxHP : -1;

            var res = DamageFormula.Compute(
                attacker: attackerStats,
                defender: defender,
                attackCoef: attackCoef,
                defenseK: defenseK,
                baseCritBonus: baseCritBonus,
                extraDamageMul: extraDamageMul,
                forceCrit: forceCrit
            );

            // ✅ 몬스터 피격 팝업 = PlayerDeal 색상
            if (spawnDamagePopup)
            {
                var spawner = (damagePopupSpawner != null) ? damagePopupSpawner : DamagePopupSpawner.Instance;
                Transform attackerTr = (popupAttackerOverride != null) ? popupAttackerOverride : transform;

                spawner?.Spawn(m.transform, attackerTr, res.damage, res.isCrit, DamagePopupType.PlayerDeal);
            }

            m.TakeFinalDamage(res.damage);
            didApplyDamage = true;

            if (skillSystem != null)
                skillSystem.OnPlayerDealtDamage(m.transform, res.damage, PlayerSkillSystem.DamageSource.BasicAttack);

            if (logDamageGive)
            {
                int atk = ((IUnitStatProvider)attackerStats).Atk;
                int def = (defender != null) ? defender.Def : 0;
                string rawStr = logIncludeRaw ? $", raw={res.raw:F2}" : "";
                Debug.Log($"[DMG][Give][Player->Monster] Player='{gameObject.name}' -> Monster='{m.name}' | atk={atk}, def={def}, crit={res.isCrit} | dmg={res.damage}{rawStr}");
            }

            if (logDamageReceive && ms != null)
            {
                Debug.Log($"[DMG][Recv][Monster] Monster='{m.name}' HP {beforeHp}/{beforeMax} -> {ms.hp}/{ms.maxHP} (-{res.damage})");
            }
        }

        return didApplyDamage;
    }

    private void PlayAttackComboAnimation()
    {
        if (animator == null) return;

        float cd = Mathf.Max(0.01f, GetCooldownSeconds());
        float resetT = (comboResetSeconds > 0f) ? comboResetSeconds : (cd * 1.5f);

        if (Time.time - lastHitAttackTime > resetT)
            comboIndex = 0;

        string stateName =
            (comboIndex == 0) ? attack1StateName :
            (comboIndex == 1) ? attack2StateName :
                               attack3StateName;

        if (!string.IsNullOrEmpty(stateName))
            animator.CrossFadeInFixedTime(stateName, crossFadeTime, animatorLayer);

        lastHitAttackTime = Time.time;

        if (comboIndex < 2) comboIndex++;
        else comboIndex = loopAfterThird ? 0 : 2;
    }

    private void ResetCombo()
    {
        comboIndex = 0;
        lastHitAttackTime = -999f;
    }

    private bool PassTargetFilter(Collider other)
    {
        if (string.IsNullOrWhiteSpace(targetTag)) return true;
        return other != null && other.tag == targetTag;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? cachedRadius : attackRadius);

        Gizmos.color = Color.green;

        float r = Application.isPlaying ? cachedRadius : attackRadius;
        Vector3 forward = transform.forward * r;

        Quaternion leftRot = Quaternion.Euler(0, -attackAngle / 2f, 0);
        Quaternion rightRot = Quaternion.Euler(0, attackAngle / 2f, 0);

        Gizmos.DrawLine(transform.position, transform.position + leftRot * forward);
        Gizmos.DrawLine(transform.position, transform.position + rightRot * forward);

        int segments = 10;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float a = Mathf.Lerp(-attackAngle / 2f, attackAngle / 2f, t);

            Quaternion rot = Quaternion.Euler(0, a, 0);
            Vector3 d = rot * forward;
            Gizmos.DrawLine(transform.position, transform.position + d);
        }
    }
}
