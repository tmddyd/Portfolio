using System.Collections;
using TMPro;
using UnityEngine;

public enum ScorePopupType
{
    Kill,
    StageClear
}

public class ScoreGainPopupUI : MonoBehaviour
{
    [Header("Refs (Max 2 Lines)")]
    public TextMeshProUGUI line1Text;
    public TextMeshProUGUI line2Text;

    [Header("Colors")]
    public Color killColor = new Color(0.72f, 0.35f, 0.95f, 1f); // 보라
    public Color clearColor = new Color(1f, 0.85f, 0.2f, 1f);    // 노랑

    [Header("Timing")]
    [Tooltip("표시 유지 시간(초)")]
    public float holdSeconds = 1.0f;

    [Tooltip("페이드 아웃 시간(초)")]
    public float fadeSeconds = 0.45f;

    [Header("Fade Motion (NEW)")]
    [Tooltip("페이드 시작과 함께 위로 올라가는 거리(Y, anchoredPosition 기준)")]
    public float fadeMoveUpY = 18f;

    [Tooltip("페이드 이동 easing")]
    public AnimationCurve fadeMoveEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("페이드 alpha easing (0~1)")]
    public AnimationCurve fadeAlphaEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Promotion Motion")]
    [Tooltip("승격(2->1) 시 위로 이동하는 느낌(선택)")]
    public float promoteMoveY = 28f;

    [Tooltip("승격 이동 시간(초)")]
    public float promoteMoveSeconds = 0.12f;

    public AnimationCurve promoteEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // --------------------
    // runtime
    // --------------------
    private Coroutine line1Co;
    private Coroutine line2Co;

    private RectTransform line1Rt;
    private RectTransform line2Rt;

    private Vector2 line1BasePos;
    private Vector2 line2BasePos;

    private void Awake()
    {
        if (line1Text != null) line1Rt = line1Text.GetComponent<RectTransform>();
        if (line2Text != null) line2Rt = line2Text.GetComponent<RectTransform>();

        if (line1Rt != null) line1BasePos = line1Rt.anchoredPosition;
        if (line2Rt != null) line2BasePos = line2Rt.anchoredPosition;

        HideLine(line1Text);
        HideLine(line2Text);
    }

    public void Show(ScorePopupType type, int value)
    {
        if (line1Text == null || line2Text == null)
        {
            Debug.LogWarning("[ScoreGainPopupUI] line1Text/line2Text가 비어있습니다.");
            return;
        }

        bool has1 = IsVisible(line1Text);
        bool has2 = IsVisible(line2Text);

        if (!has1 && !has2)
        {
            SetLine(line1Text, type, value);
            RestartLineRoutine(ref line1Co, LineIndex.One);
            return;
        }

        if (has1 && !has2)
        {
            SetLine(line2Text, type, value);
            RestartLineRoutine(ref line2Co, LineIndex.Two);
            return;
        }

        // 2줄 꽉참: line1 즉시 제거 -> line2 승격 -> 새 항목 line2
        ForceClearLine(LineIndex.One);
        PromoteLine2ToLine1();
        SetLine(line2Text, type, value);
        RestartLineRoutine(ref line2Co, LineIndex.Two);
    }

    // --------------------
    private enum LineIndex { One, Two }

    private bool IsVisible(TextMeshProUGUI txt)
    {
        return txt != null && txt.gameObject.activeSelf && txt.color.a > 0.001f;
    }

    private void SetLine(TextMeshProUGUI txt, ScorePopupType type, int value)
    {
        txt.text = $"+{Mathf.Abs(value)}";
        txt.color = GetColor(type, 1f);
        txt.gameObject.SetActive(true);
    }

    private Color GetColor(ScorePopupType type, float alpha)
    {
        Color c = (type == ScorePopupType.Kill) ? killColor : clearColor;
        c.a = alpha;
        return c;
    }

    private void HideLine(TextMeshProUGUI txt)
    {
        if (txt == null) return;
        var c = txt.color;
        c.a = 0f;
        txt.color = c;
        txt.gameObject.SetActive(false);
    }

    private void RestartLineRoutine(ref Coroutine co, LineIndex idx)
    {
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(CoHoldThenFade(idx));
    }

    private void ForceClearLine(LineIndex idx)
    {
        if (idx == LineIndex.One)
        {
            if (line1Co != null) { StopCoroutine(line1Co); line1Co = null; }
            HideLine(line1Text);
            ResetPos(LineIndex.One);
        }
        else
        {
            if (line2Co != null) { StopCoroutine(line2Co); line2Co = null; }
            HideLine(line2Text);
            ResetPos(LineIndex.Two);
        }
    }

    private void ResetPos(LineIndex idx)
    {
        if (idx == LineIndex.One && line1Rt != null) line1Rt.anchoredPosition = line1BasePos;
        if (idx == LineIndex.Two && line2Rt != null) line2Rt.anchoredPosition = line2BasePos;
    }

    // --------------------
    // Promotion (2 -> 1)
    // --------------------
    private void PromoteLine2ToLine1()
    {
        if (!IsVisible(line2Text)) return;

        line1Text.text = line2Text.text;
        line1Text.color = line2Text.color;
        line1Text.gameObject.SetActive(true);

        if (line2Co != null) { StopCoroutine(line2Co); line2Co = null; }
        HideLine(line2Text);
        ResetPos(LineIndex.Two);

        // 승격된 line1도 다시 시간/페이드로 사라짐
        RestartLineRoutine(ref line1Co, LineIndex.One);

        // 승격 모션(선택)
        if (line1Rt != null)
        {
            line1Rt.anchoredPosition = line2BasePos;
            StartCoroutine(CoPromoteMove(line1Rt, from: line2BasePos, to: line1BasePos + new Vector2(0f, promoteMoveY)));
        }
    }

    private IEnumerator CoPromoteMove(RectTransform rt, Vector2 from, Vector2 to)
    {
        float dur = Mathf.Max(0.01f, promoteMoveSeconds);
        float t = 0f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / dur);
            float e = promoteEase.Evaluate(n);
            rt.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
            yield return null;
        }

        if (rt == line1Rt)
            rt.anchoredPosition = line1BasePos;
    }

    // --------------------
    // Lifetime: hold -> (fade + move up) -> hide
    // --------------------
    private IEnumerator CoHoldThenFade(LineIndex idx)
    {
        TextMeshProUGUI txt = (idx == LineIndex.One) ? line1Text : line2Text;
        RectTransform rt = (idx == LineIndex.One) ? line1Rt : line2Rt;

        Vector2 basePos = (idx == LineIndex.One) ? line1BasePos : line2BasePos;

        // hold
        float hold = Mathf.Max(0f, holdSeconds);
        if (hold > 0f) yield return new WaitForSeconds(hold);

        // fade + move up (NEW)
        float fade = Mathf.Max(0.01f, fadeSeconds);
        float t = 0f;

        Color baseC = txt.color;
        float startA = baseC.a;

        Vector2 fromPos = (rt != null) ? rt.anchoredPosition : basePos;
        Vector2 toPos = fromPos + new Vector2(0f, fadeMoveUpY);

        while (t < fade)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / fade);

            // alpha
            float ae = fadeAlphaEase.Evaluate(n);
            float a = Mathf.Lerp(startA, 0f, ae);

            Color c = baseC;
            c.a = a;
            txt.color = c;

            // move up
            if (rt != null)
            {
                float me = fadeMoveEase.Evaluate(n);
                rt.anchoredPosition = Vector2.LerpUnclamped(fromPos, toPos, me);
            }

            yield return null;
        }

        // hide & reset
        HideLine(txt);
        ResetPos(idx);

        // after cleanup:
        if (idx == LineIndex.One)
        {
            line1Co = null;

            // line1이 사라졌는데 line2가 남아 있으면 자동 승격
            if (IsVisible(line2Text))
            {
                PromoteLine2ToLine1();
            }
        }
        else
        {
            line2Co = null;
        }
    }
}
