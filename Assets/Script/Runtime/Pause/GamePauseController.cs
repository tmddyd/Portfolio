using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GamePauseController : MonoBehaviour
{
    [Header("Buttons (HUD)")]
    public Button pauseButton;

    [Header("Pause UI")]
    public GameObject pausePanel;
    public Button closeButtonTopRight;      // X
    public Button closeButtonBottomRight;   // Continue(우하단 X와 동일 기능)
    public Button restartButton;            // Restart
    public Button giveUpButton;             // Give Up -> Result

    [Header("Resume Countdown UI")]
    public TMP_Text resumeCountdownText;
    public int resumeCountdownSeconds = 3;

    [Header("Acquired Skill Grid (Prefab Spawn)")]
    [Tooltip("아이콘들이 생성될 부모(권장: ScrollView/Viewport/Content)")]
    public Transform acquiredSkillGridRoot;

    [Tooltip("스킬 아이콘 프리팹(여기에 pauseleveltext가 붙어있어야 함)")]
    public GameObject skillIconPrefab;

    [Tooltip("생성된 아이콘 오브젝트 자체가 클릭을 가로채지 않게 CanvasGroup으로 막을지 여부(선택)")]
    public bool addCanvasGroupBlockRaycastOff = false;

    [Header("Skill Source")]
    [Tooltip("ISkillInventorySource 구현 컴포넌트(보통 PlayerSkillSystem)")]
    public MonoBehaviour skillInventorySource;

    [Header("Disable While Paused (Optional)")]
    public Behaviour[] disableBehavioursWhilePaused;

    [Header("Result UI")]
    public GameResultUI resultUI;

    [Header("Result Meta (for Localization)")]
    [Tooltip("현재 스테이지 ID(예: Story_SG001). 인스펙터에서 직접 넣거나, WaveManager에서 연동해도 됩니다.")]
    public string stageId = "Story_SG001";

    [Tooltip("레벨 표기(임시). 실제 레벨 시스템 붙이면 교체하세요.")]
    public int levelFallback = 1;

    public static bool IsPaused { get; private set; }

    private ISkillInventorySource _skillSource;
    private PlayerSkillSystem _pss; // 이벤트 구독용
    private Coroutine _resumeRoutine;

    private void Awake()
    {
        if (pauseButton) pauseButton.onClick.AddListener(OpenPause);

        if (closeButtonTopRight) closeButtonTopRight.onClick.AddListener(ResumeWithCountdown);
        if (closeButtonBottomRight) closeButtonBottomRight.onClick.AddListener(ResumeWithCountdown);

        if (restartButton) restartButton.onClick.AddListener(RestartStage);
        if (giveUpButton) giveUpButton.onClick.AddListener(GiveUpToResult);

        if (pausePanel) pausePanel.SetActive(false);
        if (resumeCountdownText) resumeCountdownText.gameObject.SetActive(false);

        _skillSource = skillInventorySource as ISkillInventorySource;
        if (_skillSource == null && skillInventorySource != null)
            Debug.LogWarning("[GamePauseController] skillInventorySource는 ISkillInventorySource를 구현해야 합니다.");

        // (선택) 스킬이 바뀌면 Pause 열려있을 때 즉시 갱신
        _pss = skillInventorySource as PlayerSkillSystem;
        if (_pss != null) _pss.OnOwnedSkillsChanged += HandleOwnedSkillsChanged;

        // ResultUI 자동 탐색(권장: 인스펙터로 직접 연결)
        if (resultUI == null)
            resultUI = FindObjectOfType<GameResultUI>();
    }

    private void OnDestroy()
    {
        if (_pss != null) _pss.OnOwnedSkillsChanged -= HandleOwnedSkillsChanged;

        if (IsPaused)
        {
            Time.timeScale = 1f;
            IsPaused = false;
        }
    }

    private void HandleOwnedSkillsChanged()
    {
        if (pausePanel != null && pausePanel.activeSelf)
            RefreshAcquiredSkillGrid();
    }

    public void OpenPause()
    {
        CancelResumeCountdown();

        SetPaused(true);
        if (pausePanel) pausePanel.SetActive(true);

        RefreshAcquiredSkillGrid();
    }

    public void ResumeWithCountdown()
    {
        if (_resumeRoutine != null) return;
        _resumeRoutine = StartCoroutine(CoResumeCountdown());
    }

    private IEnumerator CoResumeCountdown()
    {
        if (pausePanel) pausePanel.SetActive(false);

        // 카운트다운 동안도 정지 유지
        SetPaused(true);

        if (resumeCountdownText) resumeCountdownText.gameObject.SetActive(true);

        int remain = Mathf.Max(1, resumeCountdownSeconds);
        while (remain > 0)
        {
            if (resumeCountdownText) resumeCountdownText.text = remain.ToString();
            yield return new WaitForSecondsRealtime(1f);
            remain--;
        }

        if (resumeCountdownText) resumeCountdownText.gameObject.SetActive(false);

        SetPaused(false);
        _resumeRoutine = null;
    }

    public void RestartStage()
    {
        CancelResumeCountdown();
        SetPaused(false);

        var scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex);
    }

    /// <summary>
    /// Give Up = 패배 결과로 처리
    /// </summary>
    public void GiveUpToResult()
    {
        CancelResumeCountdown();

        SetPaused(true);
        if (pausePanel) pausePanel.SetActive(false);
        if (resumeCountdownText) resumeCountdownText.gameObject.SetActive(false);

        if (resultUI == null)
            resultUI = FindObjectOfType<GameResultUI>();

        if (resultUI != null)
        {
            // 포기 = 패배
            resultUI.ShowFinalResult(
                isVictory: false,
                stageId: stageId,
                level: levelFallback,
                exp: 0,
                expMax: 0,
                expGain: 0,
                rewards: null
            );
        }
        else
        {
            Debug.LogWarning("[GamePauseController] resultUI가 비어있습니다. GameResultUI를 씬에 두고 참조를 연결하세요.");
        }
    }

    private void CancelResumeCountdown()
    {
        if (_resumeRoutine != null)
        {
            StopCoroutine(_resumeRoutine);
            _resumeRoutine = null;
        }
        if (resumeCountdownText) resumeCountdownText.gameObject.SetActive(false);
    }

    private void SetPaused(bool paused)
    {
        IsPaused = paused;
        Time.timeScale = paused ? 0f : 1f;

        if (disableBehavioursWhilePaused != null)
        {
            for (int i = 0; i < disableBehavioursWhilePaused.Length; i++)
            {
                var b = disableBehavioursWhilePaused[i];
                if (b) b.enabled = !paused;
            }
        }
    }

    // =========================
    // Prefab Spawn 방식
    // =========================
    private void RefreshAcquiredSkillGrid()
    {
        if (acquiredSkillGridRoot == null) return;

        ClearChildren(acquiredSkillGridRoot);

        if (skillIconPrefab == null)
        {
            Debug.LogWarning("[GamePauseController] skillIconPrefab이 비어있습니다.");
            return;
        }

        if (_skillSource == null)
            return;

        List<OwnedSkillIconInfo> owned = _skillSource.GetOwnedSkillIcons() ?? new List<OwnedSkillIconInfo>();
        if (owned.Count == 0) return;

        var unique = new Dictionary<string, OwnedSkillIconInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in owned)
        {
            if (string.IsNullOrWhiteSpace(s.skillId)) continue;
            if (!unique.ContainsKey(s.skillId)) unique.Add(s.skillId, s);
        }

        var ordered = unique.Values
            .OrderByDescending(x => x.isExclusive)
            .ThenBy(x => x.acquiredIndex)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            var data = ordered[i];
            if (data.icon == null) continue;

            var go = Instantiate(skillIconPrefab, acquiredSkillGridRoot);
            go.name = $"{skillIconPrefab.name}_Spawned_{data.skillId}";

            if (addCanvasGroupBlockRaycastOff)
            {
                var cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }

            var itemUI = go.GetComponent<pauseleveltext>();
            if (itemUI == null)
            {
                Debug.LogWarning("[GamePauseController] skillIconPrefab에 pauseleveltext가 없습니다. 프리팹에 스크립트를 붙여주세요.");
                continue;
            }

            itemUI.Set(data.icon, data.level);
        }
    }

    private void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Destroy(root.GetChild(i).gameObject);
        }
    }
}

// Pause UI에 “획득 스킬 목록” 제공
public interface ISkillInventorySource
{
    List<OwnedSkillIconInfo> GetOwnedSkillIcons();
}

[Serializable]
public struct OwnedSkillIconInfo
{
    public string skillId;
    public Sprite icon;
    public bool isExclusive;
    public int acquiredIndex;
    public int level;

    public OwnedSkillIconInfo(string skillId, Sprite icon, bool isExclusive, int acquiredIndex, int level)
    {
        this.skillId = skillId;
        this.icon = icon;
        this.isExclusive = isExclusive;
        this.acquiredIndex = acquiredIndex;
        this.level = level;
    }
}
