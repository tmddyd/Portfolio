using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SkillDatabase : MonoBehaviour
{
    [Header("Filled by loader (GoogleSheet/CSV parsing result)")]
    public List<SkillRow> skills = new();
    public List<EffectRow> effects = new();
    public List<EffectValueRow> effectValues = new();

    private Dictionary<string, SkillRow> _skillById;
    private Dictionary<string, EffectRow> _effectById;
    private Dictionary<(string effectId, int level), EffectValueRow> _evByEffectLevel;

    public IEnumerable<SkillRow> AllSkills => _skillById?.Values ?? Enumerable.Empty<SkillRow>();

    public void Build()
    {
        _skillById = skills.ToDictionary(s => s.SkillID);
        _effectById = effects.ToDictionary(e => e.EffectID);
        _evByEffectLevel = effectValues.ToDictionary(v => (v.EffectID, v.Level));

        Debug.Log($"[SkillDatabase] Build OK. Skills={skills.Count}, Effects={effects.Count}, EffectValues={effectValues.Count}");
    }

    public SkillRow GetSkill(string skillId) =>
        (_skillById != null && _skillById.TryGetValue(skillId, out var s)) ? s : null;

    public EffectRow GetEffect(string effectId) =>
        (_effectById != null && _effectById.TryGetValue(effectId, out var e)) ? e : null;

    public EffectValueRow GetEffectValue(string effectId, int level) =>
        (_evByEffectLevel != null && _evByEffectLevel.TryGetValue((effectId, level), out var v)) ? v : null;
}
