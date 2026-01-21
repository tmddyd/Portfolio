using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHealth : MonoBehaviour
{
    [Header("Refs")]
    public PlayerCharacterStats stats;

    [Header("Policy")]
    public bool keepHpRatioOnMaxHpChange = true;
    public bool fullHealOnFirstSync = true;

    [Header("Runtime")]
    [SerializeField] private int maxHp = 1;
    [SerializeField] private int hp = 1;

    public int MaxHp => maxHp;
    public int Hp => hp;
    public bool IsDead => hp <= 0;

    public event Action<int, int> OnHpChanged; // (hp, maxHp)
    public event Action OnDied;

    // ✅ PlayerHpChipBarUI / PlayerLowHpUI가 구독하는 이벤트 (beforeHp, afterHp, maxHp, damage)
    public event Action<int, int, int, int> OnDamagedFinal;

    private bool syncedOnce = false;

    // =========================
    // Damage Popup (Player Damaged)
    // =========================
    [Header("Damage Popup (Player Damaged)")]
    public bool spawnDamagePopupOnPlayerDamaged = true;
    public DamagePopupSpawner damagePopupSpawner;
    public Transform fallbackAttackerForPopup;
    public bool defaultIncomingCrit = false;

    [Tooltip("무적(i-frame)이어도 '0 데미지' 팝업을 보여줄지")]
    public bool showPopupEvenWhenInvincible = false;

    [Tooltip("stats.hp에도 현재 hp를 동기화할지(이중 관리 최소한의 안전장치)")]
    public bool syncHpToStats = true;

    // =========================
    // Invincibility (i-frames) + Blink
    // =========================
    [Header("Invincibility (i-frames)")]
    public bool enableInvincibleAfterHit = true;
    public float invincibleDuration = 0.5f;
    public bool useUnscaledTimeForInvincible = true;

    [Header("Blink (During Invincible)")]
    public bool enableBlink = true;
    public float blinkInterval = 0.1f;
    public Transform blinkRoot;
    public Renderer[] blinkRenderers;

    [Header("Blink Debug")]
    public bool logBlinkTargets = false;

    private float invincibleUntil = -1f;
    private Coroutine invCo;

    private float NowTime => useUnscaledTimeForInvincible ? Time.unscaledTime : Time.time;
    public bool IsInvincible => enableInvincibleAfterHit && NowTime < invincibleUntil;

    private void Reset()
    {
        stats = GetComponent<PlayerCharacterStats>();
    }

    private void Awake()
    {
        CacheUIRefs();

        if (damagePopupSpawner == null)
            damagePopupSpawner = DamagePopupSpawner.Instance;
    }

    private void OnEnable()
    {
        if (stats == null) stats = GetComponent<PlayerCharacterStats>();
        if (stats != null) stats.OnStatsChanged += SyncMaxHp;

        CacheUIRefs();

        SyncMaxHp();
        UpdateUI();
        UpdateUIFollow();

        SetVisible(true);
    }

    private void OnDisable()
    {
        if (stats != null) stats.OnStatsChanged -= SyncMaxHp;

        if (invCo != null)
        {
            StopCoroutine(invCo);
            invCo = null;
        }

        SetVisible(true);
    }

    private void LateUpdate()
    {
        UpdateUIFollow();
    }

    // =========================
    // Blink internals
    // =========================
    private void InitBlinkTargetsIfNeeded()
    {
        if (blinkRenderers != null && blinkRenderers.Length > 0) return;

        Transform root = blinkRoot != null ? blinkRoot : transform;
        var all = root.GetComponentsInChildren<Renderer>(true);

        var list = new System.Collections.Generic.List<Renderer>(all.Length);

        foreach (var r in all)
        {
            if (r == null) continue;

            if (r is ParticleSystemRenderer) continue;
            if (r is TrailRenderer) continue;
            if (r is LineRenderer) continue;

            if (r.GetComponent("VisualEffect") != null) continue;
            if (r.GetComponent<ParticleSystem>() != null) continue;

            list.Add(r);
        }

        blinkRenderers = list.ToArray();

        if (logBlinkTargets)
            Debug.Log($"[PlayerHealth][Blink] targets={blinkRenderers.Length} (root={(blinkRoot != null ? blinkRoot.name : name)})");
    }

    private void SetVisible(bool visible)
    {
        if (blinkRenderers == null) return;
        for (int i = 0; i < blinkRenderers.Length; i++)
        {
            var r = blinkRenderers[i];
            if (r == null) continue;
            r.enabled = visible;
        }
    }

    private void StartInvincible()
    {
        if (!enableInvincibleAfterHit) return;

        invincibleUntil = NowTime + Mathf.Max(0f, invincibleDuration);

        if (!enableBlink || blinkInterval <= 0f)
            return;

        if (invCo != null) StopCoroutine(invCo);
        invCo = StartCoroutine(CoBlinkUntil(invincibleUntil));
    }

    private IEnumerator CoBlinkUntil(float endTime)
    {
        InitBlinkTargetsIfNeeded();

        bool visible = true;
        float next = NowTime + Mathf.Max(0.01f, blinkInterval);
        SetVisible(true);

        while (NowTime < endTime)
        {
            if (NowTime >= next)
            {
                visible = !visible;
                SetVisible(visible);
                next = NowTime + Mathf.Max(0.01f, blinkInterval);
            }
            yield return null;
        }

        SetVisible(true);
        invCo = null;
    }

    // =========================
    // UI (optional)
    // =========================
    [Header("UI (Optional)")]
    public TMP_Text hpText;

    [Header("UI Debug")]
    public bool logUiSync = false;

    [Header("HP Bar Follow (GameView)")]
    public bool followSliderToPlayer = true;
    public Vector2 screenOffset = new Vector2(0f, -60f);

    [Tooltip("HP바 전체 루트 오브젝트(그린+칩 슬라이더를 포함한 부모)를 넣으세요.")]
    public RectTransform hpUiRoot;

    [Tooltip("hpUiRoot가 속한 Canvas. 비어있으면 자동 탐색")]
    public Canvas sliderCanvas;

    [Tooltip("Canvas가 ScreenSpace-Camera일 때 사용할 카메라(Overlay면 비워둠)")]
    public Camera uiCamera;

    private RectTransform canvasRect;

    private void CacheUIRefs()
    {
        if (hpUiRoot != null)
        {
            if (sliderCanvas == null)
                sliderCanvas = hpUiRoot.GetComponentInParent<Canvas>();

            if (sliderCanvas != null)
                canvasRect = sliderCanvas.GetComponent<RectTransform>();
        }

        if (uiCamera == null)
            uiCamera = Camera.main;
    }

    public void SyncMaxHp()
    {
        int newMax = 1;
        if (stats != null) newMax = Mathf.Max(1, stats.maxHp);

        if (!syncedOnce)
        {
            maxHp = newMax;
            hp = fullHealOnFirstSync ? maxHp : Mathf.Clamp(hp, 0, maxHp);
            syncedOnce = true;
            FireChanged();
            SyncHpToStatsIfNeeded();
            return;
        }

        if (newMax == maxHp) return;

        if (keepHpRatioOnMaxHpChange)
        {
            float ratio = (maxHp > 0) ? (hp / (float)maxHp) : 1f;
            maxHp = newMax;
            hp = Mathf.Clamp(Mathf.RoundToInt(maxHp * ratio), 0, maxHp);
        }
        else
        {
            maxHp = newMax;
            hp = Mathf.Clamp(hp, 0, maxHp);
        }

        FireChanged();
        SyncHpToStatsIfNeeded();
    }

    // =========================
    // Damage entry points
    // =========================
    public void ApplyRawDamage(int rawDamage, Transform attacker = null, bool isCrit = false)
    {
        int final = Mathf.Max(1, rawDamage);

        if (stats != null)
            final = Mathf.Max(1, rawDamage - stats.def);

        ApplyFinalDamage(final, attacker, isCrit);
    }

    public void ApplyFinalDamage(int dmg)
    {
        ApplyFinalDamage(dmg, attacker: null, isCrit: defaultIncomingCrit);
    }

    public void ApplyFinalDamage(int dmg, Transform attacker, bool isCrit)
    {
        if (IsDead) return;

        // i-frame 처리
        if (IsInvincible)
        {
            if (showPopupEvenWhenInvincible && spawnDamagePopupOnPlayerDamaged)
                SpawnDamagePopup(attacker, 0, isCrit);

            return;
        }

        int before = hp;

        int d = Mathf.Max(1, dmg);
        hp -= d;
        if (hp < 0) hp = 0;

        Debug.Log($"[DMG][Recv][Player] '{name}' HP {before}/{maxHp} -> {hp}/{maxHp} (-{d})");

        // ✅ 칩 바 / LowHpUI 등이 사용하는 이벤트 (핵심)
        OnDamagedFinal?.Invoke(before, hp, maxHp, d);

        // ✅ 플레이어 피격 팝업
        if (spawnDamagePopupOnPlayerDamaged)
            SpawnDamagePopup(attacker, d, isCrit);

        if (hp > 0)
            StartInvincible();

        FireChanged();
        SyncHpToStatsIfNeeded();

        if (hp <= 0)
        {
            SetVisible(true);
            OnDied?.Invoke();
        }
    }

    private void SpawnDamagePopup(Transform attacker, int damageValue, bool isCrit)
    {
        var spawner = (damagePopupSpawner != null) ? damagePopupSpawner : DamagePopupSpawner.Instance;
        if (spawner == null) return;

        Transform attackerTr = attacker != null ? attacker : fallbackAttackerForPopup;
        spawner.Spawn(transform, attackerTr, damageValue, isCrit, DamagePopupType.PlayerTaken);
    }

    public void Heal(int amount)
    {
        if (IsDead) return;

        hp = Mathf.Clamp(hp + Mathf.Max(0, amount), 0, maxHp);

        // 회복은 칩바가 snapChipOnHeal로 즉시 따라오게 처리하므로
        // OnDamagedFinal은 호출하지 않습니다.
        FireChanged();
        SyncHpToStatsIfNeeded();
    }

    private void SyncHpToStatsIfNeeded()
    {
        if (!syncHpToStats) return;
        if (stats == null) return;

        stats.hp = Mathf.Clamp(hp, 0, Mathf.Max(1, stats.maxHp));
    }

    private void FireChanged()
    {
        OnHpChanged?.Invoke(hp, maxHp);
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (hpText != null)
            hpText.text = $"{hp}/{maxHp}";

        if (logUiSync)
            Debug.Log($"[PlayerHealth][UI] hp={hp}, maxHp={maxHp}");
    }

    private void UpdateUIFollow()
    {
        if (!followSliderToPlayer) return;
        if (hpUiRoot == null) return;
        if (sliderCanvas == null || canvasRect == null) return;

        Camera camForWorldToScreen = Camera.main;
        if (camForWorldToScreen == null) return;

        Vector3 sp = camForWorldToScreen.WorldToScreenPoint(transform.position);

        if (sp.z < 0f)
        {
            if (hpUiRoot.gameObject.activeSelf) hpUiRoot.gameObject.SetActive(false);
            return;
        }
        else
        {
            if (!hpUiRoot.gameObject.activeSelf) hpUiRoot.gameObject.SetActive(true);
        }

        Vector2 screenPos = (Vector2)sp + screenOffset;

        // Screen Space - Overlay면 camForCanvas는 null
        Camera camForCanvas = null;
        if (sliderCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            camForCanvas = uiCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPos, camForCanvas, out Vector2 localPoint))
        {
            hpUiRoot.anchoredPosition = localPoint;
        }
    }

    // 필요하면 코드로 바인딩도 가능
    public void BindUIRoot(RectTransform uiRoot, TMP_Text text = null)
    {
        hpUiRoot = uiRoot;
        hpText = text;
        CacheUIRefs();
        UpdateUI();
        UpdateUIFollow();
    }
}
