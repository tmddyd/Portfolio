using UnityEngine;

public class Monster : MonoBehaviour
{
    [Header("Move")]
    public float moveSpeed = 2f;

    [Header("Stat -> MoveSpeed")]
    public bool applySpeedFromStat = true;
    public float spdToMoveSpeedScale = 1f;
    public float minMoveSpeed = 0.05f;
    public bool logApplySpeed = true;

    [Header("Debug")]
    public bool logPlayerFindResult = true;
    public bool warnIfNoPlayer = true;
    public float retryFindInterval = 1.0f;

    [Header("EXP Drop")]
    public ExpOrb expOrbPrefab;
    public int totalExpOnDeath = 1;
    public int orbCount = 1;
    public float scatterRadius = 0.6f;
    public float scatterUp = 0.2f;

    [Header("Score (Optional)")]
    public bool addScoreOnDeath = true;
    public bool logScoreGain = true;

    private Transform player;
    private MonsterStat stat;

    private bool dropped;
    private bool scored;

    void Awake()
    {
        stat = GetComponent<MonsterStat>();

        if (stat != null)
        {
            stat.OnDied += HandleDied;
            stat.OnSheetLoaded += ApplyMoveSpeedFromStat;
        }
    }

    private void OnDestroy()
    {
        if (stat != null)
        {
            stat.OnDied -= HandleDied;
            stat.OnSheetLoaded -= ApplyMoveSpeedFromStat;
        }
    }

    void Start()
    {
        FindPlayerOnce();

        if (player == null && retryFindInterval > 0f)
            InvokeRepeating(nameof(FindPlayerOnce), retryFindInterval, retryFindInterval);

        ApplyMoveSpeedFromStat();
    }

    void Update()
    {
        MoveToPlayer();
    }

    private void ApplyMoveSpeedFromStat()
    {
        if (!applySpeedFromStat) return;
        if (stat == null) return;
        if (!stat.IsSheetLoaded) return;

        float s = stat.SheetSpd;
        if (s <= 0f) return;

        float before = moveSpeed;
        moveSpeed = Mathf.Max(minMoveSpeed, s * spdToMoveSpeedScale);

        if (logApplySpeed)
            Debug.Log($"[Monster][Apply] '{name}' SheetSpd={s} => moveSpeed {before:F2} -> {moveSpeed:F2}");
    }

    void FindPlayerOnce()
    {
        var p = GameObject.FindWithTag("Player");
        player = (p != null) ? p.transform : null;

        if (!logPlayerFindResult) return;

        if (player == null)
        {
            if (warnIfNoPlayer)
            {
                // Debug.LogWarning(...)
            }
        }
        else
        {
            CancelInvoke(nameof(FindPlayerOnce));
        }
    }

    void MoveToPlayer()
    {
        if (player == null) return;

        Vector3 dir = (player.position - transform.position).normalized;
        dir.y = 0f;

        transform.position += dir * moveSpeed * Time.deltaTime;

        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    public void TakeDamage(int damage)
    {
        if (stat != null)
        {
            stat.TakeDamage(damage);
            return;
        }

        Debug.LogError($"[Monster] MonsterStat이 없어 파괴됩니다. Monster='{name}'");
        Destroy(gameObject);
    }

    public void TakeFinalDamage(int finalDamage)
    {
        if (stat != null)
        {
            stat.TakeFinalDamage(finalDamage);
            return;
        }

        Debug.LogError($"[Monster] MonsterStat이 없어 파괴됩니다. Monster='{name}'");
        Destroy(gameObject);
    }

    private void HandleDied(MonsterStat s)
    {
        DropExp();
        AddScoreOnDeath(s);
    }

    private void AddScoreOnDeath(MonsterStat s)
    {
        if (!addScoreOnDeath) return;
        if (scored) return;
        scored = true;

        if (s == null) return;

        int add = Mathf.Max(0, s.killScore);

        ScoreManager sm = null;
        if (ScoreManager.Instance != null)
            sm = ScoreManager.Instance;
        else
            sm = FindObjectOfType<ScoreManager>();

        if (sm == null)
        {
            Debug.LogWarning("[Monster] ScoreManager를 씬에서 찾지 못했습니다. 점수가 반영되지 않습니다.");
            return;
        }

        // ✅ 핵심 변경: AddScore -> AddKillScore (보라색 2줄 팝업 포함)
        sm.AddKillScore(add);

        if (logScoreGain)
            Debug.Log($"[Monster] Died => +{add} score (killScore). Monster='{name}'");
    }

    private void DropExp()
    {
        if (dropped) return;
        dropped = true;

        if (expOrbPrefab == null) return;
        if (totalExpOnDeath <= 0) return;

        int count = Mathf.Max(1, orbCount);
        int remain = totalExpOnDeath;

        for (int i = 0; i < count; i++)
        {
            int give = (i == count - 1) ? remain : Mathf.Max(1, totalExpOnDeath / count);
            remain -= give;

            Vector2 r = Random.insideUnitCircle * scatterRadius;
            Vector3 pos = transform.position + new Vector3(r.x, scatterUp, r.y);

            ExpOrb orb = Instantiate(expOrbPrefab, pos, Quaternion.identity);
            orb.expAmount = give;
        }
    }
}
