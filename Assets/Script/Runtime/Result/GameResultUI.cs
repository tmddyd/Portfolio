using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 결과 패널 표시 전용.
/// 승/패 판정은 외부(WaveManager 등)에서 결정해서 전달.
/// </summary>
public class GameResultUI : MonoBehaviour
{
    [Header("Root")]
    public GameObject root; // ResultPanel 루트
    public bool hideOnAwake = true;

    [Header("Title (Victory/Defeat)")]
    public TMP_Text resultTitleText;
    public string victoryTitle = "Battle Complete";
    public string defeatTitle = "DEFEAT";

    [Header("Texts")]
    public TMP_Text missionText;
    public TMP_Text levelText;
    public TMP_Text expText;
    public TMP_Text expGainText;

    [Header("EXP UI")]
    public Slider expSlider;

    [Header("Rewards (Optional)")]
    public Transform rewardGridRoot;
    public List<Image> rewardSlots = new List<Image>();
    public Sprite emptyRewardSprite;

    [Header("Tap To Continue (Optional)")]
    public Button tapButton;

    [Tooltip("비어있지 않으면 탭 시 해당 씬으로 이동. 비어있으면 결과 패널을 닫습니다.")]
    public string goToSceneOnTap = "";

    [Header("Localization (Stage)")]
    [Tooltip("씬에 있는 LocalizationTable을 연결하세요. 비워도 자동 탐색합니다.")]
    public LocalizationTable loc;

    [Tooltip("일부 프로젝트에서 Stage_ 접두사 키를 쓰는 경우를 위해 보조로 지원합니다. 예: Stage_Story_SG001")]
    public string stageKeyPrefix = "Stage_";

    [Tooltip("로컬라이즈가 아직 준비되지 않았을 때, 최대 몇 초까지 대기 후 갱신할지")]
    public float waitLocalizationMaxSeconds = 5f;

    private Coroutine _waitLocCoroutine;

    private void Awake()
    {
        if (root == null)
        {
            root = gameObject;
            Debug.LogWarning("[GameResultUI] root가 비어있어서 현재 GameObject로 자동 지정했습니다. ResultPanel 루트를 root에 연결하는 것을 권장합니다.");
        }

        if (hideOnAwake && root) root.SetActive(false);

        if (rewardSlots.Count == 0 && rewardGridRoot != null)
            rewardSlots = new List<Image>(rewardGridRoot.GetComponentsInChildren<Image>(true));

        if (tapButton) tapButton.onClick.AddListener(OnTapContinue);

        if (loc == null) loc = FindObjectOfType<LocalizationTable>();
    }

    /// <summary>
    /// 외부에서 호출하는 최종 결과 표시 API
    /// </summary>
    public void ShowFinalResult(bool isVictory, string stageId, int level, int exp, int expMax, int expGain, List<Sprite> rewards)
    {
        if (root) root.SetActive(true);

        ApplyTitle(isVictory);
        ApplyStageMission(stageId);

        if (levelText) levelText.text = $"Lv.{level}";
        if (expText) expText.text = $"{exp}/{expMax}";
        if (expGainText) expGainText.text = expGain > 0 ? $"+{expGain}" : "";

        if (expSlider)
        {
            expSlider.minValue = 0f;
            expSlider.maxValue = 1f;
            expSlider.value = (expMax > 0) ? (float)exp / expMax : 0f;
        }

        RefreshRewards(rewards);
    }

    public void Hide()
    {
        if (root) root.SetActive(false);
    }

    private void ApplyTitle(bool isVictory)
    {
        if (!resultTitleText) return;
        resultTitleText.text = isVictory ? victoryTitle : defeatTitle;
    }

    private void ApplyStageMission(string stageId)
    {
        if (!missionText) return;
        if (string.IsNullOrEmpty(stageId))
        {
            missionText.text = "";
            return;
        }

        if (loc == null) loc = FindObjectOfType<LocalizationTable>();

        // loc가 아직 준비 안 됐으면, stageId 먼저 보여주고 준비되면 갱신
        if (loc == null || !loc.IsReady)
        {
            missionText.text = stageId;

            if (_waitLocCoroutine != null) StopCoroutine(_waitLocCoroutine);

            if (loc != null)
                _waitLocCoroutine = StartCoroutine(WaitAndApplyStageName(stageId));

            return;
        }

        // ✅ 1순위: 시트에 실제로 넣은 키 = "Story_SG001"
        string v = loc.GetKO(stageId);
        if (!string.IsNullOrEmpty(v) && v != stageId)
        {
            missionText.text = v;
            return;
        }

        // ✅ 2순위: 혹시 Stage_Story_SG001 형태로 넣었을 수도 있으니 보조 지원
        string prefixedKey = stageKeyPrefix + stageId;
        string v2 = loc.GetKO(prefixedKey);
        if (!string.IsNullOrEmpty(v2) && v2 != prefixedKey)
        {
            missionText.text = v2;
            return;
        }

        // 못 찾으면 stageId 그대로
        missionText.text = stageId;
    }

    private IEnumerator WaitAndApplyStageName(string stageIdToApply)
    {
        float t = 0f;
        while (loc != null && !loc.IsReady && t < waitLocalizationMaxSeconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (missionText == null) yield break;
        if (loc == null || !loc.IsReady)
        {
            missionText.text = stageIdToApply;
            yield break;
        }

        // 1순위: stageId 그대로
        string v = loc.GetKO(stageIdToApply);
        if (!string.IsNullOrEmpty(v) && v != stageIdToApply)
        {
            missionText.text = v;
            yield break;
        }

        // 2순위: Stage_ 접두사
        string prefixedKey = stageKeyPrefix + stageIdToApply;
        string v2 = loc.GetKO(prefixedKey);
        if (!string.IsNullOrEmpty(v2) && v2 != prefixedKey)
        {
            missionText.text = v2;
            yield break;
        }

        missionText.text = stageIdToApply;
    }

    private void RefreshRewards(List<Sprite> rewards)
    {
        if (rewardSlots == null || rewardSlots.Count == 0) return;

        for (int i = 0; i < rewardSlots.Count; i++)
        {
            var img = rewardSlots[i];
            if (!img) continue;

            if (rewards != null && i < rewards.Count && rewards[i] != null)
            {
                img.enabled = true;
                img.sprite = rewards[i];
                img.color = Color.white;
            }
            else
            {
                if (emptyRewardSprite != null)
                {
                    img.enabled = true;
                    img.sprite = emptyRewardSprite;
                    img.color = Color.white;
                }
                else
                {
                    img.enabled = false;
                }
            }
        }
    }

    private void OnTapContinue()
    {
        if (!string.IsNullOrWhiteSpace(goToSceneOnTap))
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(goToSceneOnTap);
        }
        else
        {
            Hide();
        }
    }
}
