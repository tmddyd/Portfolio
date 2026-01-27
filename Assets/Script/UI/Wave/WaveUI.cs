using UnityEngine;
using TMPro;

public class WaveUI : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI waveText;        // 예: "WAVE 1"

    [Header("Format")]
    public string waveFormat = "WAVE {0}";

    public void SetWave(int waveId)
    {
        if (waveText != null)
            waveText.text = string.Format(waveFormat, waveId);

        Debug.Log($"[UI] Wave {waveId}");
    }

    // 클리어 텍스트 UI는 제거했지만, 외부에서 호출 중일 수 있으니 함수는 유지합니다.
    // 필요 없으면 이 함수 자체도 삭제해도 됩니다.
    public void ShowClear(int waveId)
    {
        Debug.Log($"[UI] Wave Clear {waveId}");
    }
}
