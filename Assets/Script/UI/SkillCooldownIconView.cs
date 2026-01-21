using UnityEngine;
using UnityEngine.UI;

public class SkillCooldownIconView : MonoBehaviour
{
    [Header("UI Refs")]
    public Image iconImage;              // 뒤(BG) 아이콘
    public Image cooldownFillImage;      // 앞(Fill) 아이콘 (Filled)

    [Header("Fill Auto Setup")]
    public bool autoConfigureFill = true;
    public Image.Origin360 fillOrigin = Image.Origin360.Top;
    public bool fillClockwise = true;

    private void Awake()
    {
        // ✅ Fill 이미지 세팅만 자동으로 (색/알파는 건드리지 않음)
        if (autoConfigureFill && cooldownFillImage != null)
        {
            cooldownFillImage.type = Image.Type.Filled;
            cooldownFillImage.fillMethod = Image.FillMethod.Radial360;
            cooldownFillImage.fillOrigin = (int)fillOrigin;
            cooldownFillImage.fillClockwise = fillClockwise;
        }
    }

    /// <summary>
    /// BG/Fill에 스프라이트만 넣는다. (색/알파는 인스펙터/PNG에서 관리)
    /// </summary>
    public void SetIcon(Sprite icon, bool hideWhenMissing)
    {
        if (icon == null)
        {
            if (hideWhenMissing) gameObject.SetActive(false);
            else
            {
                gameObject.SetActive(true);
                if (iconImage != null) iconImage.sprite = null;
                if (cooldownFillImage != null) cooldownFillImage.sprite = null;
            }
            return;
        }

        gameObject.SetActive(true);

        // ⚠️ 여기서는 "동일 스프라이트"를 양쪽에 넣습니다.
        // BG를 검정 버전으로 쓰고 싶으면, BG Image 쪽에 인스펙터로 다른 스프라이트를 넣거나
        // HUD에서 BG/Fill을 각각 다르게 세팅하는 방식으로 확장하면 됩니다.
        if (iconImage != null) iconImage.sprite = icon;
        if (cooldownFillImage != null) cooldownFillImage.sprite = icon;
    }

    /// <summary>
    /// remaining01: 1=막 사용(쿨타임 가득), 0=쿨타임 완료
    /// "차오르는" 연출이므로 progress = 1 - remaining01
    /// </summary>
    public void SetCooldownRemaining01(float remaining01)
    {
        if (cooldownFillImage == null) return;

        remaining01 = Mathf.Clamp01(remaining01);
        float progress01 = 1f - remaining01;
        cooldownFillImage.fillAmount = progress01;
    }

    /// <summary>
    /// 준비 완료면 꽉 채움(색/알파 조작 없음)
    /// </summary>
    public void SetReady(bool ready)
    {
        if (cooldownFillImage == null) return;

        if (ready)
            cooldownFillImage.fillAmount = 1f;
    }
}
