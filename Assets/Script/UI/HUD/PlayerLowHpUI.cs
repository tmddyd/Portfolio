using System.Collections;
using UnityEngine;

public class PlayerLowHpUI : MonoBehaviour
{
    [Header("Refs")]
    public PlayerHealth playerHealth;
    public CanvasGroup vignetteGroup;

    [Header("Policy")]
    [Range(0f, 1f)]
    public float lowHpThreshold = 0.30f;

    [Header("Flash")]
    [Range(0f, 1f)]
    public float maxAlpha = 0.65f;

    public float fadeIn = 0.06f;
    public float hold = 0.10f;
    public float fadeOut = 0.35f;

    private Coroutine co;

    private void Reset()
    {
        if (playerHealth == null) playerHealth = FindObjectOfType<PlayerHealth>();
        if (vignetteGroup == null) vignetteGroup = GetComponent<CanvasGroup>();
    }

    private void Awake()
    {
        if (vignetteGroup != null)
            vignetteGroup.alpha = 0f;
    }

    private void OnEnable()
    {
        if (playerHealth == null) return;
        playerHealth.OnDamagedFinal += OnDamaged;
    }

    private void OnDisable()
    {
        if (playerHealth == null) return;
        playerHealth.OnDamagedFinal -= OnDamaged;

        if (co != null) { StopCoroutine(co); co = null; }
        if (vignetteGroup != null) vignetteGroup.alpha = 0f;
    }

    private void OnDamaged(int beforeHp, int afterHp, int maxHp, int damage)
    {
        if (vignetteGroup == null) return;
        if (maxHp <= 0) return;

        float ratioAfter = afterHp / (float)maxHp;

        // ✅ “체력 30% 이하일 때 피격을 받으면” 발동
        if (ratioAfter > lowHpThreshold) return;

        if (co != null) StopCoroutine(co);
        co = StartCoroutine(CoFlash());
    }

    private IEnumerator CoFlash()
    {
        float inT = Mathf.Max(0.01f, fadeIn);
        float outT = Mathf.Max(0.01f, fadeOut);

        // fade in
        float t = 0f;
        while (t < inT)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / inT);
            vignetteGroup.alpha = Mathf.Lerp(0f, maxAlpha, n);
            yield return null;
        }
        vignetteGroup.alpha = maxAlpha;

        // hold
        float h = Mathf.Max(0f, hold);
        if (h > 0f) yield return new WaitForSeconds(h);

        // fade out
        t = 0f;
        while (t < outT)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / outT);
            vignetteGroup.alpha = Mathf.Lerp(maxAlpha, 0f, n);
            yield return null;
        }

        vignetteGroup.alpha = 0f;
        co = null;
    }
}
