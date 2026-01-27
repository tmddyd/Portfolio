using System.Collections;
using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    [Header("이동 설정")]
    public float moveSpeed = 5f;
    public FloatingJoystick joystick;
    public Transform model;
    public Animator animator;

    [Header("몬스터 탐지 설정")]
    public float detectRadius = 3f;
    public LayerMask monsterLayer;

    [Header("회전 속도")]
    public float rotateSpeed = 10f;

    [Header("Stat -> MoveSpeed")]
    public bool applySpeedFromStats = true;
    public PlayerCharacterStats stats;
    public float spdToMoveSpeedScale = 1f;
    public float minMoveSpeed = 0.1f;

    [Header("Stat -> DetectRadius (Ar)")]
    public bool applyDetectRadiusFromAr = true;
    public float arToDetectRadiusScale = 1f;
    public float minDetectRadius = 0.25f;
    public float maxDetectRadius = 50f;

    [Header("Debug")]
    public bool logApplySpeed = true;
    public bool logApplyDetectRadius = true;

    [Header("Runtime Lock")]
    [SerializeField] private bool movementLocked = false;
    public bool MovementLocked => movementLocked;

    // =========================
    // Death (No Trigger/Bool)
    // =========================
    [Header("Death")]
    [Tooltip("비어있으면 GetComponent<PlayerHealth>()")]
    public PlayerHealth health;

    [Tooltip("Animator Controller의 '죽음 애니 State 이름' (정확히 일치해야 함)")]
    public string deathStateName = "Death";

    [Tooltip("죽음 애니가 있는 레이어 인덱스(보통 0)")]
    public int deathLayer = 0;

    [Tooltip("부드럽게 전환할 크로스페이드 시간(0이면 즉시 Play)")]
    public float deathCrossFade = 0.05f;

    [Tooltip("사망 시 조이스틱 오브젝트를 비활성화할지")]
    public bool disableJoystickOnDeath = true;

    private bool deathHandled = false;

    private Monster currentTarget;
    private Vector3 moveDir;

    private void Reset()
    {
        stats = GetComponent<PlayerCharacterStats>();
        health = GetComponent<PlayerHealth>();
    }

    private void OnEnable()
    {
        if (stats == null) stats = GetComponent<PlayerCharacterStats>();
        if (stats != null) stats.OnStatsChanged += ApplyFromStats;

        if (health == null) health = GetComponent<PlayerHealth>();
        if (health != null)
        {
            health.OnDied += HandleDeath;

            if (health.IsDead)
                HandleDeath();
        }

        ApplyFromStats();
    }

    private void OnDisable()
    {
        if (stats != null) stats.OnStatsChanged -= ApplyFromStats;
        if (health != null) health.OnDied -= HandleDeath;
    }

    // 외부(스킬)에서 호출
    public void SetMovementLocked(bool locked)
    {
        if (deathHandled) return;

        movementLocked = locked;

        if (locked)
        {
            moveDir = Vector3.zero;
            currentTarget = null;
            if (animator != null) animator.SetFloat("MoveSpeed", 0f);
        }
    }

    void Update()
    {
        if (movementLocked)
        {
            if (animator != null) animator.SetFloat("MoveSpeed", 0f);
            return;
        }

        Move();
        FindTarget();
        RotatePlayer();
    }

    private void ApplyFromStats()
    {
        if (stats == null) return;

        if (applySpeedFromStats)
        {
            float s = stats.spd;
            if (s > 0f)
            {
                float before = moveSpeed;
                moveSpeed = Mathf.Max(minMoveSpeed, s * spdToMoveSpeedScale);

                if (logApplySpeed)
                    Debug.Log($"[PlayerMove][Apply] Spd={s} => moveSpeed {before:F2} -> {moveSpeed:F2}");
            }
        }

        if (applyDetectRadiusFromAr)
        {
            int ar = stats.ar;
            if (ar > 0)
            {
                float before = detectRadius;
                float r = ar * arToDetectRadiusScale;
                detectRadius = Mathf.Clamp(r, minDetectRadius, maxDetectRadius);

                if (logApplyDetectRadius)
                    Debug.Log($"[PlayerMove][Apply] Ar={ar} => detectRadius {before:F2} -> {detectRadius:F2}");
            }
        }
    }

    void Move()
    {
        float h = joystick != null ? joystick.Horizontal : 0f;
        float v = joystick != null ? joystick.Vertical : 0f;

        moveDir = new Vector3(h, 0, v);

        if (moveDir.magnitude > 0.1f)
        {
            transform.position += moveDir.normalized * moveSpeed * Time.deltaTime;
            if (animator != null) animator.SetFloat("MoveSpeed", 1f);
        }
        else
        {
            if (animator != null) animator.SetFloat("MoveSpeed", 0f);
        }
    }

    void FindTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, detectRadius, monsterLayer);

        Monster closest = null;
        float minDist = Mathf.Infinity;

        foreach (var hit in hits)
        {
            Monster m = hit.GetComponent<Monster>();
            if (m == null) continue;

            float dist = Vector3.Distance(transform.position, m.transform.position);

            if (dist < minDist)
            {
                minDist = dist;
                closest = m;
            }
        }

        currentTarget = closest;
    }

    void RotatePlayer()
    {
        if (model == null) return;

        if (currentTarget != null)
        {
            Vector3 dir = (currentTarget.transform.position - transform.position);
            dir.y = 0;

            if (dir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                model.rotation = Quaternion.Slerp(model.rotation, targetRot, Time.deltaTime * rotateSpeed);
            }
            return;
        }

        if (moveDir.magnitude > 0.1f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir.normalized);
            model.rotation = Quaternion.Slerp(model.rotation, targetRot, Time.deltaTime * rotateSpeed);
        }
    }

    private void HandleDeath()
    {
        if (deathHandled) return;
        deathHandled = true;

        // 이동/탐지/회전 즉시 중단
        movementLocked = true;
        moveDir = Vector3.zero;
        currentTarget = null;

        if (disableJoystickOnDeath && joystick != null)
            joystick.gameObject.SetActive(false);

        // 애니메이션: 파라미터 없이 "State 강제 재생"
        if (animator != null && !string.IsNullOrEmpty(deathStateName))
        {
            animator.SetFloat("MoveSpeed", 0f);

            if (deathCrossFade > 0f)
                animator.CrossFadeInFixedTime(deathStateName, deathCrossFade, deathLayer, 0f);
            else
                animator.Play(deathStateName, deathLayer, 0f);

            animator.Update(0f);
        }

        // 결과 UI 표시는 WaveManager가 담당 (defeatResultDelay 포함)
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}
