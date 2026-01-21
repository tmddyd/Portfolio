using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class WaveManager : MonoBehaviour
{
    [Header("Data")]
    public WaveData waveData;
    public WaveMobData waveMobData;

    [Header("Content Routing (NEW)")]
    public ContentData contentData;
    public StageData stageData;

    [Tooltip("스토리 모드면 Story_CT")]
    public string contentId = "Story_CT";
    public int contentStep = 1;
    public int stageStep = 1;
    public bool useContentRouting = true;

    [Header("Runtime Stage (for Result UI)")]
    public string currentStageId = "Story_SG001";

    [Header("Spawn")]
    public MonsterPrefab MonsterPrefab;

    [Tooltip("비어있으면 Player 태그로 자동 탐색")]
    public Transform player;

    public float spawnRadius = 20f;
    public float yOffset = 0.5f;

    [Header("Progress")]
    public string startWaveId = "Wave001";
    public bool autoStart = true;

    [Header("Auto Advance")]
    public bool autoAdvanceNextWave = true;
    public float nextWaveDelay = 1f;

    [Header("Optional")]
    public ScoreManager scoreManager;
    public WaveUI waveUI;

    [Header("Result UI")]
    [Tooltip("결과 UI(컴포넌트). 결과 패널이 꺼져 있어도 참조만 있으면 호출 가능")]
    public GameResultUI resultUI;

    [Tooltip("플레이어 체력(사망 감지)")]
    public PlayerHealth playerHealth;

    [Tooltip("패배 시 결과 UI 표시까지 딜레이(사망 애니 시간) - unscaled")]
    public float defeatResultDelay = 2f;

    [Tooltip("승리 시 결과 UI 표시까지 딜레이 - unscaled")]
    public float victoryResultDelay = 0f;

    [Tooltip("결과창 표시 후 timeScale=0으로 멈출지(기본 false 권장: 사망 애니가 멈출 수 있음)")]
    public bool pauseTimeOnResult = false;

    [Tooltip("현재는 EXP/보상 무시라고 해서 기본값")]
    public int resultLevelFallback = 1;

    [Header("Debug")]
    public bool debugLogs = true;

    private WaveDataRow currentWave;
    private string currentWaveId;

    private bool spawningEnded = false;
    private int aliveCount = 0;
    private int runningGroupCoroutines = 0;

    private bool isAdvancing = false;
    private bool isRunEnded = false;
    private Coroutine _endRoutine;

    void Start()
    {
        if (autoStart)
            StartCoroutine(InitAndStart());
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
            playerHealth.OnDied -= HandlePlayerDied;
    }

    IEnumerator InitAndStart()
    {
        if (player == null)
        {
            var p = GameObject.FindWithTag("Player");
            player = (p != null) ? p.transform : null;
        }

        if (playerHealth == null)
        {
            if (player != null) playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth == null) playerHealth = FindObjectOfType<PlayerHealth>();
        }

        if (playerHealth != null)
        {
            playerHealth.OnDied -= HandlePlayerDied;
            playerHealth.OnDied += HandlePlayerDied;
        }
        else
        {
            Debug.LogWarning("[WaveManager] PlayerHealth를 찾지 못했습니다. 패배(사망) 처리가 동작하지 않을 수 있습니다.");
        }

        if (useContentRouting)
        {
            if (contentData != null) yield return StartCoroutine(contentData.Load());
            if (stageData != null) yield return StartCoroutine(stageData.Load());
        }

        yield return StartCoroutine(waveData.Load());
        yield return StartCoroutine(waveMobData.Load());

        string firstWaveId = startWaveId;

        if (useContentRouting)
        {
            if (!TryResolveWaveIdAndStageId(contentId, contentStep, stageStep, out var stageId, out firstWaveId))
            {
                Debug.LogError($"[WaveManager] 시작 웨이브 라우팅 실패. contentId={contentId}, contentStep={contentStep}, stageStep={stageStep}");
                yield break;
            }
            currentStageId = stageId;
        }

        StartWave(firstWaveId);
    }

    bool TryResolveWaveIdAndStageId(string cId, int cStep, int sStep, out string stageId, out string waveId)
    {
        stageId = null;
        waveId = null;

        if (contentData == null || stageData == null) return false;

        if (!contentData.TryGetStageId(cId, cStep, out stageId))
        {
            if (debugLogs) Debug.LogError($"[WaveManager] ContentData 매핑 실패: ({cId}, {cStep})");
            return false;
        }

        if (!stageData.TryGetWaveId(stageId, sStep, out waveId))
        {
            if (debugLogs) Debug.LogError($"[WaveManager] StageData 매핑 실패: ({stageId}, {sStep})");
            return false;
        }

        return true;
    }

    public void StartWave(string waveId)
    {
        if (isRunEnded) return;

        if (!waveData.TryGet(waveId, out currentWave))
        {
            Debug.LogError($"[WaveManager] WaveID='{waveId}' 를 찾지 못했습니다.");
            return;
        }

        if (player == null)
        {
            Debug.LogError("[WaveManager] player가 없습니다. Player 태그/인스펙터 확인 필요.");
            return;
        }

        currentWaveId = waveId;
        spawningEnded = false;
        aliveCount = 0;
        runningGroupCoroutines = 0;

        waveUI?.SetWave(ExtractWaveNumber(currentWaveId));

        if (debugLogs)
            Debug.Log($"[WaveManager] Wave Start: {waveId} (Duration={currentWave.Duration}, ClearScore={currentWave.ClearScore})");

        var waveMobIds = currentWave.WaveMobIDs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct()
            .ToList();

        if (waveMobIds.Count == 0)
        {
            ClearCurrentWave();
            return;
        }

        foreach (var waveMobId in waveMobIds)
        {
            if (isRunEnded) return;

            if (!waveMobData.TryGet(waveMobId, out var rules) || rules == null || rules.Count == 0)
                continue;

            runningGroupCoroutines++;
            StartCoroutine(SpawnGroupCoroutine(rules, currentWave.Duration));
        }

        if (runningGroupCoroutines == 0)
            ClearCurrentWave();
    }

    IEnumerator SpawnGroupCoroutine(List<WaveMobRow> rules, float duration)
    {
        if (isRunEnded) yield break;

        foreach (var rule in rules)
        {
            if (isRunEnded) yield break;
            if (string.IsNullOrWhiteSpace(rule.MobID) || rule.MobCount <= 0 || rule.SpawnRate <= 0f) continue;
            StartCoroutine(SpawnRuleCoroutine(rule, duration));
        }

        yield return new WaitForSeconds(duration);

        if (isRunEnded) yield break;

        runningGroupCoroutines--;
        if (runningGroupCoroutines <= 0)
        {
            spawningEnded = true;
            if (debugLogs) Debug.Log($"[WaveManager] Spawning Ended: Wave {currentWaveId}");
            TryClearIfReady();
        }
    }

    IEnumerator SpawnRuleCoroutine(WaveMobRow rule, float duration)
    {
        if (isRunEnded) yield break;

        float t = 0f;
        while (t < duration)
        {
            if (isRunEnded) yield break;

            Spawn(rule.MobID, rule.MobCount);
            yield return new WaitForSeconds(rule.SpawnRate);
            t += rule.SpawnRate;
        }
    }

    void Spawn(string mobId, int count)
    {
        if (isRunEnded) return;

        var prefab = MonsterPrefab.GetPrefab(mobId);
        if (prefab == null)
        {
            Debug.LogError($"[WaveManager] MobID='{mobId}' prefab이 MonsterPrefab에 없습니다.");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (isRunEnded) return;

            Vector3 pos = GetRandomSpawnPosAroundPlayer();
            var go = Instantiate(prefab, pos, Quaternion.identity);

            var stat = go.GetComponent<MonsterStat>();
            if (stat == null)
            {
                Destroy(go);
                continue;
            }

            stat.ApplyWaveMultipliers(currentWave.HpMul, currentWave.AtkMul, currentWave.DefMul, currentWave.ScoreMul);
            stat.OnDied += HandleMonsterDied;

            aliveCount++;
        }
    }

    Vector3 GetRandomSpawnPosAroundPlayer()
    {
        float angle = Random.Range(0f, 360f);
        float rad = angle * Mathf.Deg2Rad;

        Vector3 p = player.position;

        Vector3 spawnPos = new Vector3(
            p.x + Mathf.Cos(rad) * spawnRadius,
            p.y,
            p.z + Mathf.Sin(rad) * spawnRadius
        );

        spawnPos.y += yOffset;
        return spawnPos;
    }

    void HandleMonsterDied(MonsterStat m)
    {
        if (isRunEnded) return;

        m.OnDied -= HandleMonsterDied;

        aliveCount = Mathf.Max(0, aliveCount - 1);
        TryClearIfReady();
    }

    void TryClearIfReady()
    {
        if (isRunEnded) return;

        if (spawningEnded && aliveCount == 0)
            ClearCurrentWave();
    }

    void ClearCurrentWave()
    {
        if (isRunEnded) return;

        if (debugLogs)
            Debug.Log($"[WaveManager] Wave Clear: {currentWaveId} (+{currentWave.ClearScore} score)");

        if (scoreManager != null)
            scoreManager.AddStageClearScore(currentWave.ClearScore);

        waveUI?.ShowClear(ExtractWaveNumber(currentWaveId));

        if (autoAdvanceNextWave && !isAdvancing)
        {
            isAdvancing = true;
            StartCoroutine(StartNextWaveAfterDelay());
        }
    }

    IEnumerator StartNextWaveAfterDelay()
    {
        yield return new WaitForSeconds(nextWaveDelay);

        if (isRunEnded)
        {
            isAdvancing = false;
            yield break;
        }

        string nextWaveId = null;

        if (useContentRouting)
        {
            if (!TryResolveNextWaveByRouting(out nextWaveId))
            {
                // ✅ 다음 웨이브 없음 = 승리
                isAdvancing = false;
                EndRun(victory: true);
                yield break;
            }
        }
        else
        {
            nextWaveId = GetNextWaveId(currentWaveId);
        }

        if (!waveData.TryGet(nextWaveId, out _))
        {
            // ✅ 다음 웨이브 데이터 없음 = 승리로 처리
            isAdvancing = false;
            EndRun(victory: true);
            yield break;
        }

        isAdvancing = false;
        StartWave(nextWaveId);
    }

    bool TryResolveNextWaveByRouting(out string nextWaveId)
    {
        nextWaveId = null;

        // 1) 같은 Stage에서 stageStep+1
        int nextStageStep = stageStep + 1;
        if (TryResolveWaveIdAndStageId(contentId, contentStep, nextStageStep, out var stageIdSame, out var waveInSameStage))
        {
            stageStep = nextStageStep;
            currentStageId = stageIdSame;
            nextWaveId = waveInSameStage;
            return true;
        }

        // 2) Stage 끝이면 contentStep+1, stageStep=1
        int nextContentStep = contentStep + 1;
        if (TryResolveWaveIdAndStageId(contentId, nextContentStep, 1, out var stageIdNext, out var firstWaveOfNextStage))
        {
            contentStep = nextContentStep;
            stageStep = 1;
            currentStageId = stageIdNext;
            nextWaveId = firstWaveOfNextStage;
            return true;
        }

        return false;
    }

    // ✅ 사망 = 패배
    private void HandlePlayerDied()
    {
        if (isRunEnded) return;
        EndRun(victory: false);
    }

    private void EndRun(bool victory)
    {
        if (isRunEnded) return;
        isRunEnded = true;

        if (_endRoutine != null) StopCoroutine(_endRoutine);

        float delay = victory ? victoryResultDelay : defeatResultDelay;
        _endRoutine = StartCoroutine(CoShowResultAfterDelay(victory, delay));
    }

    private IEnumerator CoShowResultAfterDelay(bool victory, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        var ui = GetResultUIIncludingInactive();
        if (ui != null)
        {
            ui.ShowFinalResult(
                isVictory: victory,
                stageId: currentStageId,
                level: resultLevelFallback,
                exp: 0,
                expMax: 0,
                expGain: 0,
                rewards: null
            );
        }
        else
        {
            Debug.LogWarning("[WaveManager] Result UI를 찾지 못했습니다. (resultUI를 인스펙터에 직접 연결 권장)");
        }

        if (pauseTimeOnResult)
            Time.timeScale = 0f;

        _endRoutine = null;
    }

    private GameResultUI GetResultUIIncludingInactive()
    {
        if (resultUI != null) return resultUI;

        // 활성 오브젝트에서 먼저 탐색
        resultUI = FindObjectOfType<GameResultUI>();
        if (resultUI != null) return resultUI;

        // 비활성 포함 탐색 (씬 오브젝트만)
        var all = Resources.FindObjectsOfTypeAll<GameResultUI>();
        for (int i = 0; i < all.Length; i++)
        {
            var ui = all[i];
            if (ui == null) continue;
            if (!ui.gameObject.scene.IsValid()) continue;
            resultUI = ui;
            return resultUI;
        }

        return null;
    }

    int ExtractWaveNumber(string waveId)
    {
        if (string.IsNullOrWhiteSpace(waveId)) return 0;

        string digits = new string(waveId.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits)) return 0;

        return int.TryParse(digits, out int n) ? n : 0;
    }

    // useContentRouting=false일 때만 사용
    string GetNextWaveId(string waveId)
    {
        if (string.IsNullOrWhiteSpace(waveId)) return "Wave001";

        int firstDigit = -1;
        for (int i = 0; i < waveId.Length; i++)
        {
            if (char.IsDigit(waveId[i]))
            {
                firstDigit = i;
                break;
            }
        }

        if (firstDigit < 0)
            return waveId + "001";

        string prefix = waveId.Substring(0, firstDigit);
        string digits = waveId.Substring(firstDigit);

        digits = new string(digits.Where(char.IsDigit).ToArray());
        int width = digits.Length;

        if (!int.TryParse(digits, out int n)) n = 0;
        n += 1;

        return prefix + n.ToString().PadLeft(width, '0');
    }
}
