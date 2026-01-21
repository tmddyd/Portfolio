using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SkillSelectUI : MonoBehaviour
{
    [Serializable]
    public class OptionSlot
    {
        public Button button;
        public TMP_Text titleText;
        public TMP_Text descText;
        public TMP_Text levelText;

        [Header("Skill Icon (기존)")]
        public Image iconImage;

        [Header("Character Icon (전용 스킬일 때만)")]
        public Image characterIconImage;   // ✅ 여기로 charIcon 오브젝트의 Image를 연결
    }

    public GameObject root;
    public OptionSlot[] slots = new OptionSlot[3];

    [Header("Character Icon Sprite")]
    public Sprite rezeCharacterIcon;      // ✅ 여기로 REZE 아이콘 Sprite 할당

    [Header("Icon Load (Resources)")]
    public string iconResourcesFolder = "SkillIcons";
    public Sprite defaultIcon;
    public bool hideIconWhenMissing = true;

    private readonly Dictionary<string, Sprite> _iconCache = new();

    public bool IsOpen => (root != null ? root.activeSelf : gameObject.activeSelf);

    private Action<PlayerSkillSystem.OfferVM> _onPick;
    private List<PlayerSkillSystem.OfferVM> _offers;

    private void Awake()
    {
        if (root == null) root = gameObject;
        Close();
    }

    public void Open(List<PlayerSkillSystem.OfferVM> offers, Action<PlayerSkillSystem.OfferVM> onPick)
    {
        _offers = offers;
        _onPick = onPick;

        if (root != null) root.SetActive(true);

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.button == null) continue;

            if (offers != null && i < offers.Count)
            {
                var vm = offers[i];

                if (slot.titleText != null) slot.titleText.text = vm.title;
                if (slot.descText != null) slot.descText.text = vm.desc;
                if (slot.levelText != null) slot.levelText.text = $"Lv {vm.nextLevel}";

                // (기존) 스킬 아이콘
                ApplyIcon(slot, vm.skillId);

                // ✅ (추가) 전용 스킬이면 charIcon에 REZE 표시, 아니면 비활성화
                ApplyCharacterIcon(slot, vm.isExclusive);

                slot.button.gameObject.SetActive(true);
                slot.button.onClick.RemoveAllListeners();
                slot.button.onClick.AddListener(() => _onPick?.Invoke(vm));
            }
            else
            {
                slot.button.onClick.RemoveAllListeners();
                slot.button.gameObject.SetActive(false);

                if (slot.iconImage != null)
                {
                    slot.iconImage.sprite = null;
                    if (hideIconWhenMissing) slot.iconImage.gameObject.SetActive(false);
                }

                if (slot.characterIconImage != null)
                {
                    slot.characterIconImage.sprite = null;
                    slot.characterIconImage.gameObject.SetActive(false);
                }
            }
        }
    }

    public void Close()
    {
        if (root != null) root.SetActive(false);
    }

    private void ApplyCharacterIcon(OptionSlot slot, bool isExclusive)
    {
        if (slot == null || slot.characterIconImage == null) return;

        if (isExclusive && rezeCharacterIcon != null)
        {
            slot.characterIconImage.sprite = rezeCharacterIcon;
            slot.characterIconImage.gameObject.SetActive(true);
        }
        else
        {
            slot.characterIconImage.sprite = null;
            slot.characterIconImage.gameObject.SetActive(false);
        }
    }

    // =========================
    // Skill Icon (기존)
    // =========================
    private void ApplyIcon(OptionSlot slot, string skillId)
    {
        if (slot == null || slot.iconImage == null) return;

        Sprite icon = LoadIconBySkillId(skillId);

        if (icon != null)
        {
            slot.iconImage.sprite = icon;
            slot.iconImage.gameObject.SetActive(true);
        }
        else if (defaultIcon != null)
        {
            slot.iconImage.sprite = defaultIcon;
            slot.iconImage.gameObject.SetActive(true);
        }
        else
        {
            slot.iconImage.sprite = null;
            if (hideIconWhenMissing) slot.iconImage.gameObject.SetActive(false);
            else slot.iconImage.gameObject.SetActive(true);
        }
    }

    private Sprite LoadIconBySkillId(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return null;

        if (_iconCache.TryGetValue(skillId, out var cached))
            return cached;

        string path = $"{iconResourcesFolder}/{skillId}";
        Sprite sp = Resources.Load<Sprite>(path);

        if (sp == null)
        {
            string fallbackPath = $"{iconResourcesFolder}/Icon_{skillId}";
            sp = Resources.Load<Sprite>(fallbackPath);
        }

        _iconCache[skillId] = sp;
        return sp;
    }
}
