using System;
using System.Collections.Generic;
using UnityEngine;

public interface ICooldownSkillSource
{
    string SkillId { get; }
    bool IsUnlocked { get; }
    bool IsExclusive { get; }
    bool IsReady { get; }

    float CooldownDuration { get; }
    float CooldownRemaining { get; }
}

/// <summary>
/// ✅ 자동 리스트화(Provider)용 인터페이스
/// - 하나의 컴포넌트가 여러 쿨타임 스킬을 제공할 수 있음
/// </summary>
public interface ICooldownSkillProvider
{
    IEnumerable<ICooldownSkillSource> GetCooldownSkills();
}

public class SkillCooldownHUD : MonoBehaviour
{
    [Header("UI")]
    public Transform container;                // 아이콘들이 들어갈 부모(Vertical Layout Group 권장)
    public SkillCooldownIconView iconPrefab;   // View가 붙은 프리팹

    [Header("Icon Load (Resources)")]
    [Tooltip("Resources 기준 폴더. 예: Assets/Resources/Skillicons -> Skillicons")]
    public string iconResourcesFolder = "Skillicons";
    public Sprite defaultIcon;
    public bool hideIconWhenMissing = true;

    [Header("Sources")]
    [Tooltip("여기에 CharacterSkill, PlayerSkillSystem 등(=ICooldownSkillSource/Provider 구현한 컴포넌트)을 넣으세요.")]
    public MonoBehaviour[] skillSources;

    [Header("Debug")]
    public bool logRegister = false;

    private readonly Dictionary<string, Sprite> _iconCache = new();

    private class Entry
    {
        public ICooldownSkillSource src;
        public SkillCooldownIconView view;
        public int acquiredOrder;
    }

    private readonly Dictionary<string, Entry> _entriesById = new();
    private int _acquiredSeq = 0;

    private void Awake()
    {
        if (container == null) container = transform;
    }

    private void Update()
    {
        ScanAndRegisterUnlocked();
        RefreshAndSort();
    }

    private void ScanAndRegisterUnlocked()
    {
        if (skillSources == null) return;
        if (iconPrefab == null || container == null) return;

        for (int i = 0; i < skillSources.Length; i++)
        {
            var mb = skillSources[i];
            if (mb == null) continue;

            // ✅ Provider가 있으면 Provider를 우선(자동 리스트화)
            //    -> 중복 방지를 위해 Provider면 ICooldownSkillSource로는 다시 등록하지 않음
            if (mb is ICooldownSkillProvider provider)
            {
                var skills = provider.GetCooldownSkills();
                if (skills == null) continue;

                foreach (var s in skills)
                    TryRegister(s);

                continue;
            }

            // 기본: 단일 소스 등록(예: CharacterSkill)
            if (mb is ICooldownSkillSource src)
            {
                TryRegister(src);
            }
        }
    }

    private void TryRegister(ICooldownSkillSource src)
    {
        if (src == null) return;

        if (!src.IsUnlocked) return;

        // 쿨타임 없는 스킬은 HUD 대상 아님
        if (src.CooldownDuration <= 0.01f) return;

        string id = src.SkillId;
        if (string.IsNullOrWhiteSpace(id)) return;

        if (_entriesById.ContainsKey(id)) return;

        var view = Instantiate(iconPrefab, container);
        view.name = $"HUD_{id}";

        Sprite icon = LoadIconBySkillId(id);
        if (icon != null) view.SetIcon(icon, hideIconWhenMissing);
        else view.SetIcon(defaultIcon, hideIconWhenMissing);

        var e = new Entry
        {
            src = src,
            view = view,
            acquiredOrder = _acquiredSeq++
        };
        _entriesById[id] = e;

        if (logRegister)
            Debug.Log($"[SkillCooldownHUD] Registered: {id}, exclusive={src.IsExclusive}, order={e.acquiredOrder}");
    }

    private void RefreshAndSort()
    {
        if (_entriesById.Count == 0) return;

        List<Entry> list = new List<Entry>(_entriesById.Values);

        list.Sort((a, b) =>
        {
            // 1) 전용 스킬이 위로
            int ex = b.src.IsExclusive.CompareTo(a.src.IsExclusive);
            if (ex != 0) return ex;

            // 2) 획득 순서 빠른 게 위로
            return a.acquiredOrder.CompareTo(b.acquiredOrder);
        });

        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e.view == null || e.src == null) continue;

            e.view.transform.SetSiblingIndex(i);

            float dur = Mathf.Max(0.01f, e.src.CooldownDuration);
            float rem = Mathf.Clamp(e.src.CooldownRemaining, 0f, dur);

            // rem01: 1=방금 씀(가득), 0=완료
            float rem01 = rem / dur;

            e.view.SetCooldownRemaining01(rem01);
            e.view.SetReady(e.src.IsReady);
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
            string fallback = $"{iconResourcesFolder}/Icon_{skillId}";
            sp = Resources.Load<Sprite>(fallback);
        }

        _iconCache[skillId] = sp;
        return sp;
    }
}
