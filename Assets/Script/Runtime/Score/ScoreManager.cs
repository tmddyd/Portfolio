using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("Data")]
    public int score;

    [Header("UI")]
    public TextMeshProUGUI scoreText;
    public string format = "Score : {0}";

    [Header("Score Gain Popup (Optional)")]
    public ScoreGainPopupUI gainPopup; // ✅ 추가

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        RefreshUI();
    }

    // ✅ 기존 기능 유지 (연출 없음)
    public void AddScore(int value)
    {
        score += value;
        RefreshUI();
    }

    // ✅ 기존 기능 유지 (연출 없음)
    public void SetScore(int value)
    {
        score = value;
        RefreshUI();
    }

    // ----------------------------
    // ✅ 추가: "연출 포함" 점수 추가
    // ----------------------------
    public void AddKillScore(MonsterStat stat)
    {
        if (stat == null) return;
        AddKillScore(stat.killScore);
    }

    public void AddKillScore(int killScore)
    {
        score += killScore;
        RefreshUI();

        // ✅ 보라색 팝업
        if (gainPopup != null)
            gainPopup.Show(ScorePopupType.Kill, killScore);
    }

    // ✅ 스테이지 클리어(노란색)용
    public void AddStageClearScore(int clearScore)
    {
        score += clearScore;
        RefreshUI();

        if (gainPopup != null)
            gainPopup.Show(ScorePopupType.StageClear, clearScore);
    }

    void RefreshUI()
    {
        if (scoreText != null)
            scoreText.text = string.Format(format, score);
    }
}
