using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ✅ HUD가 읽을 수 있도록 인터페이스 구현
public class CharacterSkill : MonoBehaviour, ICooldownSkillSource
{
    [Header("Refs")]
    public PlayerCharacterStats attackerStats;
    public PlayerSkillSystem skillSystem;

    [Header("Link (Movement Lock)")]
    public PlayerMove playerMove; // PlayerMove에 추가한 SetMovementLocked 사용

    [Header("Enable/Runtime")]
    public bool isEnabled = false;
    public string skillId = "Skill014";
    public int level = 1;

    [Header("Cooldown")]
    public float cooldown = 10f;
    public bool useUnscaledTime = false;
    private float nextReadyTime = 0f;

    [Header("Target Acquire")]
    public float acquireRadius = 3f;
    public LayerMask monsterMask;
    public string monsterTag = "Monster";

    [Header("Damage")]
    [Tooltip("✅ 규칙: 0=1배, 100=2배, 106=2.06배 (배율 = 1 + atkPercent/100)")]
    public int atkPercent = 100;

    [Header("Critical (IlSum Only)")]
    public bool allowCritical = true;
    public float baseCritBonus = 0.5f;
    public bool rollCritOncePerCast = true;

    [Header("Dash")]
    public float maxDashDistance = 3f;
    public float dashDuration = 0.07f;

    [Tooltip("공격 애니메이션 출력 후 돌진까지 딜레이(초)")]
    public float preDashDelay = 1f;

    public bool keepY = true;

    [Header("Dash Policy")]
    public bool dashToMaxDistanceEvenIfTargetNear = true;
    public bool allowCastWithoutTargetDashForward = false;

    [Header("Hit Box (Rectangle Along Dash Path)")]
    public float boxWidth = 2.5f;
    public float boxHeight = 2.0f;

    [Header("Policy")]
    public bool requireTargetToCast = true;

    [Tooltip("벽/지형은 멈추되, 몬스터는 관통합니다.")]
    public bool stopAtObstacle = true;
    public LayerMask obstacleMask;

    [Header("Animation")]
    public Animator animator;

    [Tooltip("스킬 사용 가능(IsReady) 시 전용 로코모션(Idle/Walk)로 전환하는 Bool")]
    public string skillReadyBoolName = "IlSumReady";

    [Tooltip("캐스팅 중 상태 Bool(선택)")]
    public string castingBoolName = "IlSumCasting";

    [Tooltip("일섬 공격 Trigger")]
    public string attackTriggerName = "IlSumAttack";

    public bool faceTargetOnCast = true;

    // ✅ 팝업은 싱글톤(Instance)만 사용하도록 고정 => 인스펙터 필드 제거 가능
    [Header("Damage Popup")]
    public bool spawnDamagePopup = true;

    [Header("Debug")]
    public bool logCast = true;
    public bool drawGizmos = true;

    private bool isCasting = false;
    public bool IsCasting => isCasting;

    private float Now => useUnscaledTime ? Time.unscaledTime : Time.time;
    public bool IsReady => isEnabled && !isCasting && Now >= nextReadyTime;
    public float CooldownRemaining => Mathf.Max(0f, nextReadyTime - Now);

    // =========================
    // ✅ ICooldownSkillSource (HUD용)
    // =========================
    public string SkillId => skillId;
    public bool IsUnlocked => isEnabled;
    public bool IsExclusive => true;

    bool ICooldownSkillSource.IsReady => IsReady;
    public float CooldownDuration => Mathf.Max(0.01f, cooldown);
    float ICooldownSkillSource.CooldownRemaining => CooldownRemaining;

    private void Reset()
    {
        attackerStats = GetComponent<PlayerCharacterStats>();
        skillSystem = GetComponent<PlayerSkillSystem>();
        playerMove = GetComponent<PlayerMove>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Awake()
    {
        if (attackerStats == null) attackerStats = GetComponent<PlayerCharacterStats>();
        if (skillSystem == null) skillSystem = GetComponent<PlayerSkillSystem>();
        if (playerMove == null) playerMove = GetComponent<PlayerMove>();

        // 가능하면 PlayerMove animator를 사용(동일 애니메이터 보장)
        if (animator == null)
        {
            if (playerMove != null && playerMove.animator != null) animator = playerMove.animator;
            else animator = GetComponentInChildren<Animator>();
        }

        ApplyAnimBools();
    }

    private void OnDisable()
    {
        if (playerMove != null) playerMove.SetMovementLocked(false);
        isCasting = false;
        SetCastingAnim(false);
        SetReadyAnim(false);
    }

    private void Update()
    {
        ApplyAnimBools();

        if (!IsReady) return;
        if (attackerStats == null) return;

        Transform target = FindNearestTargetInRadius(acquireRadius);

        if (requireTargetToCast && target == null) return;
        if (target == null && !allowCastWithoutTargetDashForward) return;

        StartCoroutine(CoCast(target));
    }

    private void ApplyAnimBools()
    {
        if (isCasting)
        {
            SetReadyAnim(false);
            SetCastingAnim(true);
            return;
        }

        SetCastingAnim(false);
        SetReadyAnim(IsReady);
    }

    private void SetReadyAnim(bool ready)
    {
        if (animator == null) return;
        if (!string.IsNullOrWhiteSpace(skillReadyBoolName))
            animator.SetBool(skillReadyBoolName, ready);
    }

    private void SetCastingAnim(bool casting)
    {
        if (animator == null) return;
        if (!string.IsNullOrWhiteSpace(castingBoolName))
            animator.SetBool(castingBoolName, casting);
    }

    // =========================
    // External config
    // =========================
    public void EnableIlSum(PlayerCharacterStats stats, PlayerSkillSystem sys, int newLevel, int newAtkPercent, float newCooldown)
    {
        // ✅ null로 덮어쓰지 않도록 방어
        if (stats != null) attackerStats = stats;
        if (sys != null) skillSystem = sys;

        level = Mathf.Max(1, newLevel);
        atkPercent = Mathf.Max(0, newAtkPercent);     // 0=1배, 100=2배
        cooldown = Mathf.Max(0.01f, newCooldown);

        isEnabled = true;

        if (nextReadyTime < Now) nextReadyTime = Now;

        if (logCast)
            Debug.Log($"[CharacterSkill:IlSum] Enabled Lv{level} atk%(+)= {atkPercent}, cd={cooldown}, radius={acquireRadius}");

        ApplyAnimBools();
    }

    // =========================
    // Core
    // =========================
    private IEnumerator CoCast(Transform target)
    {
        isCasting = true;
        nextReadyTime = Now + cooldown;

        if (playerMove != null) playerMove.SetMovementLocked(true);
        ApplyAnimBools();

        if (animator != null && !string.IsNullOrWhiteSpace(attackTriggerName))
            animator.SetTrigger(attackTriggerName);

        Vector3 startPos = transform.position;

        // 대시 방향(타겟 있으면 타겟 방향)
        Vector3 dashDir = transform.forward;
        dashDir.y = 0f;
        if (dashDir.sqrMagnitude < 0.0001f) dashDir = Vector3.forward;

        if (target != null)
        {
            Vector3 to = target.position - startPos;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
                dashDir = to.normalized;
        }

        dashDir.Normalize();

        if (faceTargetOnCast && dashDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dashDir, Vector3.up);

        // 1) 딜레이
        if (preDashDelay > 0f)
        {
            if (useUnscaledTime) yield return new WaitForSecondsRealtime(preDashDelay);
            else yield return new WaitForSeconds(preDashDelay);
        }

        // 딜레이 후 방향 1회 갱신(타겟 이동 대응)
        startPos = transform.position;

        dashDir = transform.forward;
        dashDir.y = 0f;
        if (dashDir.sqrMagnitude < 0.0001f) dashDir = Vector3.forward;

        if (target != null)
        {
            Vector3 to = target.position - startPos;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
                dashDir = to.normalized;
        }

        dashDir.Normalize();

        if (faceTargetOnCast && dashDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dashDir, Vector3.up);

        // 2) 대시 거리
        float dashDist = maxDashDistance;

        if (!dashToMaxDistanceEvenIfTargetNear && target != null)
        {
            Vector3 to = target.position - startPos;
            to.y = 0f;
            dashDist = Mathf.Min(maxDashDistance, to.magnitude);
        }

        Vector3 desiredEnd = startPos + dashDir * dashDist;
        if (keepY) desiredEnd.y = startPos.y;

        // 3) 장애물(벽/지형)만 멈춤 - 몬스터는 관통
        Vector3 endPos = desiredEnd;

        if (stopAtObstacle && obstacleMask.value != 0)
        {
            Vector3 rayOrigin = startPos + Vector3.up * 0.5f;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, dashDir, dashDist, obstacleMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            bool found = false;

            foreach (var h in hits)
            {
                if (h.collider == null) continue;

                // 몬스터 태그는 관통(장애물 제외)
                if (!string.IsNullOrWhiteSpace(monsterTag) && h.collider.CompareTag(monsterTag))
                    continue;

                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    found = true;
                }
            }

            if (found)
            {
                float safe = Mathf.Max(0f, nearest - 0.05f);
                endPos = startPos + dashDir * safe;
                if (keepY) endPos.y = startPos.y;
            }
        }

        // 4) 돌진(Transform)
        float t = 0f;
        float dur = Mathf.Max(0.0001f, dashDuration);

        while (t < dur)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            t += dt;
            float a = Mathf.Clamp01(t / dur);

            Vector3 next = Vector3.Lerp(startPos, endPos, a);
            if (keepY) next.y = startPos.y;

            transform.position = next;
            yield return null;
        }

        // 5) 피해 계산
        int atkNow = attackerStats is IUnitStatProvider isp ? isp.Atk : attackerStats.atk;

        float mul = 1f + Mathf.Max(0f, atkPercent) / 100f; // ✅ 100=2배
        int baseDmg = Mathf.Max(1, Mathf.RoundToInt(atkNow * mul));

        bool isCrit = false;
        int finalDmg = baseDmg;

        if (allowCritical)
        {
            float critChance = 0f;
            float extraCrit = 0f;

            if (attackerStats is IUnitStatProvider isp2)
            {
                critChance = isp2.CritChance01;
                extraCrit = Mathf.Max(0f, isp2.ExtraCritBonus);
            }

            if (rollCritOncePerCast)
                isCrit = Random.value < critChance;

            if (isCrit)
            {
                float critMul = 1f + Mathf.Max(0f, baseCritBonus) + extraCrit;
                finalDmg = Mathf.Max(1, Mathf.RoundToInt(baseDmg * critMul));
            }
        }

        // 6) 경로 박스 판정
        GetBoxParams(startPos, endPos, dashDir, out Vector3 center, out Vector3 halfExtents, out Quaternion rot);

        int mask = (monsterMask.value != 0) ? monsterMask.value : Physics.AllLayers;
        Collider[] cols = Physics.OverlapBox(center, halfExtents, rot, mask);

        HashSet<int> attacked = new HashSet<int>();

        // ✅ 팝업은 싱글톤만 사용 (인스펙터 세팅 불필요)
        var spawner = DamagePopupSpawner.Instance;

        foreach (var c in cols)
        {
            if (c == null) continue;
            if (!string.IsNullOrWhiteSpace(monsterTag) && !c.CompareTag(monsterTag)) continue;

            Monster m = c.GetComponentInParent<Monster>();
            if (m == null) continue;

            int id = m.gameObject.GetInstanceID();
            if (!attacked.Add(id)) continue;

            MonsterStat ms = m.GetComponent<MonsterStat>();
            if (ms != null && ms.hp <= 0) continue;

            // ✅ 데미지 텍스트
            if (spawnDamagePopup && spawner != null)
                spawner.Spawn(m.transform, transform, finalDmg, isCrit, DamagePopupType.PlayerDeal);

            m.TakeFinalDamage(finalDmg);
        }

        if (logCast)
        {
            Debug.Log($"[CharacterSkill:IlSum] Cast! target='{(target != null ? target.name : "null")}', " +
                      $"hit={attacked.Count}, atkNow={atkNow}, atk%(+)= {atkPercent}, mul={mul:0.00}, baseDmg={baseDmg}, finalDmg={finalDmg}, crit={isCrit}, " +
                      $"preDelay={preDashDelay:0.00}s, dashDist={Vector3.Distance(startPos, endPos):0.00}, cd={cooldown:0.00}s");
        }

        if (playerMove != null) playerMove.SetMovementLocked(false);

        isCasting = false;
        ApplyAnimBools();
    }

    private Transform FindNearestTargetInRadius(float radius)
    {
        int mask = (monsterMask.value != 0) ? monsterMask.value : Physics.AllLayers;
        Collider[] hits = Physics.OverlapSphere(transform.position, radius, mask);

        Transform best = null;
        float bestDist = float.MaxValue;

        foreach (var c in hits)
        {
            if (c == null) continue;
            if (!string.IsNullOrWhiteSpace(monsterTag) && !c.CompareTag(monsterTag)) continue;

            var m = c.GetComponentInParent<Monster>();
            if (m == null) continue;

            var ms = m.GetComponent<MonsterStat>();
            if (ms != null && ms.hp <= 0) continue;

            float d = (m.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = m.transform;
            }
        }

        return best;
    }

    private void GetBoxParams(Vector3 startPos, Vector3 endPos, Vector3 dir, out Vector3 center, out Vector3 halfExtents, out Quaternion rot)
    {
        center = (startPos + endPos) * 0.5f;

        float len = Vector3.Distance(startPos, endPos);
        float halfLen = Mathf.Max(0.01f, len * 0.5f);

        halfExtents = new Vector3(
            Mathf.Max(0.01f, boxWidth * 0.5f),
            Mathf.Max(0.01f, boxHeight * 0.5f),
            halfLen
        );

        rot = Quaternion.LookRotation(dir, Vector3.up);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        if (!isEnabled) return;

        Gizmos.DrawWireSphere(transform.position, acquireRadius);
    }
}
