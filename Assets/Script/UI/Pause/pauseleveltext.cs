using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class pauseleveltext : MonoBehaviour
{
    [Header("Bind In Inspector")]
    public Image iconImage;
    public TMP_Text levelText;

    [Header("Raycast Policy")]
    public bool disableRaycastTargets = true;

    public void Set(Sprite icon, int level)
    {
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = (icon != null);

            if (disableRaycastTargets)
                iconImage.raycastTarget = false;
        }

        if (levelText != null)
        {
            levelText.text = level.ToString(); // ✅ 숫자만
            levelText.gameObject.SetActive(true);

            if (disableRaycastTargets)
                levelText.raycastTarget = false;
        }
    }
}
