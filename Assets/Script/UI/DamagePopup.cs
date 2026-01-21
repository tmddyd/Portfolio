using System.Collections;
using TMPro;
using UnityEngine;

public enum DamagePopupType
{
    PlayerDeal,   // 플레이어가 준 피해(몬스터 위)
    PlayerTaken   // 플레이어가 받은 피해(플레이어 위)
}

public class DamagePopup : MonoBehaviour
{
    [Header("Refs")]
    public TMP_Text tmpText;
    public CanvasGroup canvasGroup;

    [Header("Fonts")]
    public TMP_FontAsset normalFont;
    public TMP_FontAsset criticalFont;

    [Header("Font Size")]
    public float normalSize = 36f;
    public float criticalSize = 44f;

    [Header("Colors - Player Deal (Monster Hit)")]
    public Color dealNormalColor = Color.white;
    public Color dealCriticalColor = new Color(1f, 0.85f, 0.2f, 1f);

    [Header("Colors - Player Taken (Player Hit)")]
    public Color takenNormalColor = new Color(1f, 0.25f, 0.25f, 1f);
    public Color takenCriticalColor = new Color(1f, 0.05f, 0.05f, 1f);

    [Header("Billboard")]
    public bool billboardToCamera = true;

    [Tooltip("숫자가 거꾸로 보이면 켜세요(대부분 World Space UI는 true가 정답).")]
    public bool flipFacing = true;

    [Header("Time")]
    public bool useUnscaledTime = false;

    [Tooltip("스폰 직후 '팝' 스케일 연출 시간")]
    public float popDuration = 0.10f;

    [Tooltip("팝 이후 '가만히' 멈춰있는 시간 (스폰 즉시 팍 튀는 느낌 제거 핵심)")]
    public float holdDuration = 0.06f;

    [Tooltip("상승/페이드/축소가 진행되는 시간")]
    public float floatDuration = 0.75f;

    [Header("Scale (Prefab scale is preserved)")]
    public float popFrom = 0.90f;
    public float popTo = 1.15f;
    public float endScale = 0.70f;

    [Header("Movement (Stella-like)")]
    [Tooltip("위로 떠오르는 거리(월드 단위)")]
    public float riseDistance = 0.65f;

    [Tooltip("좌/우 이동(드리프트) 거리. '왼쪽으로 이동 없애기'면 0으로 두세요.")]
    public float driftDistance = 0.00f; // ✅ 기본 0 = 좌우 이동 완전 제거

    [Tooltip("드리프트 방향 랜덤 각도(0이면 방향 고정 없이 0)")]
    public float driftRandomAngle = 180f;

    [Header("Eases")]
    public AnimationCurve popEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public AnimationCurve moveEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public AnimationCurve shrinkEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Fade (slow start)")]
    [Tooltip("floatDuration 중 몇 초 후부터 페이드를 시작할지")]
    public float fadeDelay = 0.30f;

    [Tooltip("페이드 아웃에 걸리는 시간")]
    public float fadeOutDuration = 0.35f;

    public AnimationCurve fadeEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Camera cam;
    private Vector3 baseScale = Vector3.one;
    private Vector3 driftDir = Vector3.zero;

    public void Init(int damage, bool isCritical, DamagePopupType type, Transform attacker, Transform target, Camera camera)
    {
        cam = (camera != null) ? camera : Camera.main;

        if (tmpText == null) tmpText = GetComponentInChildren<TMP_Text>(true);
        if (canvasGroup == null) canvasGroup = GetComponentInChildren<CanvasGroup>(true);

        // ✅ 프리팹 스케일 보존 (0.01 유지)
        baseScale = transform.localScale;
        if (baseScale == Vector3.zero) baseScale = Vector3.one;

        // 텍스트/폰트/사이즈/색
        if (tmpText != null)
        {
            tmpText.text = damage.ToString();

            if (isCritical)
            {
                if (criticalFont != null) tmpText.font = criticalFont;
                tmpText.fontSize = criticalSize;
                tmpText.color = (type == DamagePopupType.PlayerTaken) ? takenCriticalColor : dealCriticalColor;
            }
            else
            {
                if (normalFont != null) tmpText.font = normalFont;
                tmpText.fontSize = normalSize;
                tmpText.color = (type == DamagePopupType.PlayerTaken) ? takenNormalColor : dealNormalColor;
            }
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        // ✅ 좌/우 이동 방향(원하면 랜덤) — driftDistance=0이면 실제 이동 없음
        driftDir = Vector3.zero;
        if (driftDistance > 0f)
        {
            // 기본은 랜덤, 필요하면 0으로 두고 고정 방향으로도 가능
            Vector2 r = Random.insideUnitCircle;
            if (r.sqrMagnitude < 0.0001f) r = Vector2.right;
            r.Normalize();

            Vector3 d = new Vector3(r.x, 0f, r.y);

            if (driftRandomAngle < 180f)
            {
                float ang = Random.Range(-driftRandomAngle, driftRandomAngle);
                d = Quaternion.AngleAxis(ang, Vector3.up) * Vector3.forward;
            }

            driftDir = d.normalized;
        }

        // ✅ 시작 스케일(프리팹 0.01 * popFrom)
        transform.localScale = baseScale * popFrom;

        StopAllCoroutines();
        StartCoroutine(CoRun());
    }

    private IEnumerator CoRun()
    {
        Vector3 startPos = transform.position;

        // -----------------
        // Phase 1) Pop
        // -----------------
        float t = 0f;
        while (t < popDuration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            t += dt;

            float n = (popDuration <= 0f) ? 1f : Mathf.Clamp01(t / popDuration);
            float e = popEase.Evaluate(n);

            float s = Mathf.Lerp(popFrom, popTo, e);
            transform.localScale = baseScale * s;

            Billboard();
            yield return null;
        }

        // -----------------
        // Phase 1.5) Hold (핵심: 스폰 직후 '팍 튐' 제거)
        // -----------------
        t = 0f;
        while (t < holdDuration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            t += dt;

            // 위치는 고정, 스케일도 popTo 고정
            transform.position = startPos;
            transform.localScale = baseScale * popTo;

            Billboard();
            yield return null;
        }

        // -----------------
        // Phase 2) Float Up + (optional drift) + Fade + Shrink
        // -----------------
        t = 0f;
        while (t < floatDuration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            t += dt;

            float n = (floatDuration <= 0f) ? 1f : Mathf.Clamp01(t / floatDuration);
            float me = moveEase.Evaluate(n);
            float se = shrinkEase.Evaluate(n);

            // ✅ 위로만 상승(기본) + 좌우 드리프트(옵션)
            Vector3 pos = startPos
                          + Vector3.up * (riseDistance * me)
                          + driftDir * (driftDistance * me);

            transform.position = pos;

            // ✅ 스케일: popTo -> endScale
            float scaleMul = Mathf.Lerp(popTo, endScale, se);
            transform.localScale = baseScale * scaleMul;

            // ✅ 페이드: fadeDelay 이후부터만 사라지게
            if (canvasGroup != null)
            {
                float fadeT = 0f;
                if (fadeOutDuration <= 0f)
                {
                    fadeT = (t >= fadeDelay) ? 1f : 0f;
                }
                else
                {
                    fadeT = Mathf.Clamp01((t - fadeDelay) / fadeOutDuration);
                }

                float fe = fadeEase.Evaluate(fadeT);
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, fe);
            }

            Billboard();
            yield return null;
        }

        Destroy(gameObject);
    }

    private void Billboard()
    {
        if (!billboardToCamera) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 dir = cam.transform.position - transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(dir);

        if (flipFacing)
            transform.Rotate(0f, 180f, 0f);
    }
}
