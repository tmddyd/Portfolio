using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHpChipBarUI : MonoBehaviour
{
    [Header("Refs")]
    public PlayerHealth playerHealth;

    [Tooltip("즉시 줄어드는 녹색 바")]
    public Slider greenSlider;

    [Tooltip("딜레이로 따라 내려오는 흰색(칩) 바")]
    public Slider chipSlider;

    [Header("Tuning")]
    [Tooltip("피격 후 칩 바가 줄기 시작하기까지 대기(초)")]
    public float chipDelay = 0.15f;

    [Tooltip("칩 바가 목표값까지 내려오는 속도 (정규화 값/초). 예: 0.8이면 1초에 80% 내려감")]
    public float chipDownSpeed = 0.9f;

    [Tooltip("회복(HP 증가) 시 칩 바도 즉시 따라올지")]
    public bool snapChipOnHeal = true;

    private Coroutine chipCo;
    private float targetNorm = 1f;

    private void Reset()
    {
        if (playerHealth == null) playerHealth = FindObjectOfType<PlayerHealth>();
    }

    private void OnEnable()
    {
        if (playerHealth == null) return;

        playerHealth.OnHpChanged += HandleHpChanged;
        playerHealth.OnDamagedFinal += HandleDamaged;

        // 초기 동기화
        SyncImmediate(playerHealth.Hp, playerHealth.MaxHp);
    }

    private void OnDisable()
    {
        if (playerHealth == null) return;

        playerHealth.OnHpChanged -= HandleHpChanged;
        playerHealth.OnDamagedFinal -= HandleDamaged;

        if (chipCo != null) { StopCoroutine(chipCo); chipCo = null; }
    }

    private void SetupSliders()
    {
        if (greenSlider != null)
        {
            greenSlider.minValue = 0f;
            greenSlider.maxValue = 1f;
        }

        if (chipSlider != null)
        {
            chipSlider.minValue = 0f;
            chipSlider.maxValue = 1f;
        }
    }

    private void SyncImmediate(int hp, int maxHp)
    {
        SetupSliders();
        float n = (maxHp > 0) ? (hp / (float)maxHp) : 0f;
        n = Mathf.Clamp01(n);

        if (greenSlider != null) greenSlider.value = n;
        if (chipSlider != null) chipSlider.value = n;

        targetNorm = n;
    }

    private void HandleHpChanged(int hp, int maxHp)
    {
        SetupSliders();

        float n = (maxHp > 0) ? (hp / (float)maxHp) : 0f;
        n = Mathf.Clamp01(n);

        // 녹색은 항상 즉시 반영
        if (greenSlider != null) greenSlider.value = n;

        // 회복(증가)일 때는 칩도 즉시 따라오게(권장)
        if (snapChipOnHeal && chipSlider != null && chipSlider.value < n)
        {
            if (chipCo != null) { StopCoroutine(chipCo); chipCo = null; }
            chipSlider.value = n;
        }

        targetNorm = n;
    }

    // beforeHp, afterHp, maxHp, damage
    private void HandleDamaged(int beforeHp, int afterHp, int maxHp, int damage)
    {
        if (chipSlider == null || greenSlider == null) return;

        float beforeN = (maxHp > 0) ? (beforeHp / (float)maxHp) : 0f;
        float afterN  = (maxHp > 0) ? (afterHp  / (float)maxHp) : 0f;
        beforeN = Mathf.Clamp01(beforeN);
        afterN  = Mathf.Clamp01(afterN);

        // 칩 바는 "피격 직전 값"으로 남겨두기
        chipSlider.value = Mathf.Max(chipSlider.value, beforeN);
        targetNorm = afterN;

        if (chipCo != null) StopCoroutine(chipCo);
        chipCo = StartCoroutine(CoChipDown());
    }

    private IEnumerator CoChipDown()
    {
        float delay = Mathf.Max(0f, chipDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        float speed = Mathf.Max(0.01f, chipDownSpeed);

        while (chipSlider != null && chipSlider.value > targetNorm + 0.0001f)
        {
            chipSlider.value = Mathf.MoveTowards(chipSlider.value, targetNorm, speed * Time.deltaTime);
            yield return null;
        }

        if (chipSlider != null)
            chipSlider.value = targetNorm;

        chipCo = null;
    }
}
