using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// ✅ HUD가 읽을 수 있도록 인터페이스 구현 (전이 쿨타임을 PlayerSkillSystem이 대표)
// ✅ Provider 방식으로 전이/조건부힐을 "자동 리스트화"해서 HUD에 제공
// ✅ Pause UI(획득 스킬) 제공: ISkillInventorySource 구현
public class PlayerSkillSystem : MonoBehaviour, ICooldownSkillSource, ICooldownSkillProvider, ISkillInventorySource
{
    [Header("Refs")]
    public SkillDatabase db;
    public SkillSelectUI ui;

    public PlayerHealth playerHealth;
    public PlayerCharacterStats playerStats;
    public AutoTargetArcAttack attackSystem;

    // ✅ 전용스킬(일섬) 담당 컴포넌트
    public CharacterSkill characterSkill;

    [Header("Localization")]
    public LocalizationTable loc;
    public bool useLocalization = true;

    [Header("Policy")]
    public int offerCount = 3;
    public int maxSkillLevel = 5;
    public bool pauseGameWhileSelecting = true;

    [Tooltip("현재는 전용 스킬 쿨감 스킬을 제외(요구사항)")]
    public bool excludeSkillCooldownSkill = true;
    public string skillCooldownSkillId = "Skill011";

    // ✅ 캐릭터별 전용스킬 목록(하드코딩 제거)
    [Header("Exclusive Skills (per character)")]
    [Tooltip("이 캐릭터의 전용 스킬 ID 목록. 예: 일섬=Skill014")]
    public List<string> exclusiveSkillIds = new List<string> { "Skill014" };

    [Header("Transfer (전이)")]
    public float transferSearchRadius = 8f;
    public int transferMaxTargets = 5;
    public LayerMask monsterMask;
    public string monsterTag = "Monster";

    [Header("Icon Load (Resources) - Pause UI")]
    public string iconResourcesFolder = "SkillIcons"; // SkillSelectUI와 동일 권장
    public Sprite defaultIconForPause;                // 선택(현재 요구사항상 아이콘 없으면 생성 X 이므로 보통 미사용)
    public bool useDefaultWhenMissing = false;        // 선택

    [Header("Debug")]
    public bool logPick = true;
    public bool logTransfer = false;

    // ✅ Pause UI 갱신용 이벤트
    public event Action OnOwnedSkillsChanged;

    // =========================
    // ✅ Proc Policy (중요)
    // =========================
    public enum DamageSource
    {
        Unknown = 0,
        BasicAttack = 1,
        CharacterSkill = 2,
    }

    [Tooltip("ON이면 흡혈/전이는 BasicAttack에서만 발동")]
    public bool procsOnlyOnBasicAttack = true;

    // skillId -> level
    private readonly Dictionary<string, int> _skillLevel = new(StringComparer.OrdinalIgnoreCase);
    // skillId -> rolledValue
    private readonly Dictionary<string, int> _skillRolledValue = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _expGainBySkill = new(StringComparer.OrdinalIgnoreCase);

    // ✅ Pause UI용: 획득 순서 기록
    private int _acquireCounter = 0;
    private readonly Dictionary<string, int> _acquiredIndex = new(StringComparer.OrdinalIgnoreCase);

    // ✅ 아이콘 캐시(Resources.Load 비용 절감)
    private readonly Dictionary<string, Sprite> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private bool _lifeStealEnabled = false;
    private float _lifeStealPercent = 0f;

    // =========================
    // Transfer (전이)
    // =========================
    private bool _transferEnabled = false;
    private float _transferPercent = 0f;
    private float _transferCooldown = 10f;
    private float _transferNextReadyTime = 0f;
    private string _transferSkillIdForHud = null;

    // =========================
    // ✅ Conditional Heal (쿨타임 기반, HP 30% 이하일 때만)
    // =========================
    private bool _conditionalHealEnabled = false;
    private float _conditionalHealCooldown = 30f;
    private float _conditionalHealNextReadyTime = 0f;
    private float _conditionalHealPercentOfCurrent = 10f;
    private float _conditionalHealThresholdRatio = 0.3f;
    private string _conditionalHealSkillIdForHud = null;

    private int _pendingSelections = 0;
    private float _prevTimeScale = 1f;

    [Serializable]
    public class OfferVM
    {
        public string skillId;
        public string title;
        public string desc;
        public int nextLevel;
        public int rolledValue;
        public bool isExclusive;
    }

    // ==========================================================
    // ✅ Provider용 런타임 Source (파일 추가 없이 내부 클래스)
    // ==========================================================
    private class RuntimeSource : ICooldownSkillSource
    {
        public string SkillId { get; set; }
        public bool IsUnlocked { get; set; }
        public bool IsExclusive { get; set; }
        public bool IsReady { get; set; }
        public float CooldownDuration { get; set; }
        public float CooldownRemaining { get; set; }
    }

    private readonly RuntimeSource _srcTransfer = new RuntimeSource();
    private readonly RuntimeSource _srcConditionalHeal = new RuntimeSource();

    private void Awake()
    {
        if (playerHealth == null) playerHealth = GetComponent<PlayerHealth>();
        if (playerStats == null) playerStats = GetComponent<PlayerCharacterStats>();
        if (attackSystem == null) attackSystem = GetComponent<AutoTargetArcAttack>();

        if (characterSkill == null) characterSkill = GetComponent<CharacterSkill>();

        if (attackSystem != null) attackSystem.skillSystem = this;
    }

    private void Update()
    {
        TickConditionalHeal();
    }

    // =========================
    // Public API
    // =========================
    public void RequestSelections(int count)
    {
        if (count <= 0) return;
        _pendingSelections += count;

        if (ui != null && ui.IsOpen) return;
        OpenOneSelection();
    }

    public float GetExpGainMultiplier()
    {
        float sum = 0f;
        foreach (var kv in _expGainBySkill)
            sum += kv.Value;
        return 1f + (sum / 100f);
    }

    /// <summary>
    /// ✅ 피해 발생 콜백
    /// - 요구사항: 흡혈/전이는 기본공격에서만 발동
    /// </summary>
    public void OnPlayerDealtDamage(Transform hitMonsterRoot, int damage, DamageSource source)
    {
        if (damage <= 0) return;

        if (procsOnlyOnBasicAttack && source != DamageSource.BasicAttack)
            return;

        // LifeSteal
        if (_lifeStealEnabled && playerHealth != null && _lifeStealPercent > 0f)
        {
            int heal = Mathf.Max(0, Mathf.RoundToInt(damage * (_lifeStealPercent / 100f)));
            if (heal > 0) playerHealth.Heal(heal);
        }

        // Transfer
        if (_transferEnabled && hitMonsterRoot != null)
            TryProcTransfer(hitMonsterRoot, damage);
    }

    [Obsolete("Use OnPlayerDealtDamage(hitMonsterRoot, damage, DamageSource). Old call will be treated as Unknown and procs will NOT run.")]
    public void OnPlayerDealtDamage(Transform hitMonsterRoot, int damage)
    {
        OnPlayerDealtDamage(hitMonsterRoot, damage, DamageSource.Unknown);
    }

    // =========================
    // Selection flow
    // =========================
    private void OpenOneSelection()
    {
        if (db == null || ui == null)
        {
            Debug.LogError("[PlayerSkillSystem] db/ui reference missing.");
            return;
        }

        if (_pendingSelections <= 0) return;

        var offers = RollOffers();
        if (offers.Count == 0)
        {
            _pendingSelections = 0;
            ResumeGameIfPaused();
            return;
        }

        if (pauseGameWhileSelecting) PauseGame();
        ui.Open(offers, OnPick);
    }

    private void OnPick(OfferVM picked)
    {
        if (picked == null) return;

        ApplyPickedSkill(picked);

        _pendingSelections = Mathf.Max(0, _pendingSelections - 1);

        if (_pendingSelections > 0) OpenOneSelection();
        else
        {
            ui.Close();
            ResumeGameIfPaused();
        }
    }

    private List<OfferVM> RollOffers()
    {
        var pool = new List<SkillRow>();

        foreach (var s in db.AllSkills)
        {
            if (excludeSkillCooldownSkill && s.SkillID == skillCooldownSkillId) continue;

            int lv = GetSkillLevel(s.SkillID);
            if (lv >= maxSkillLevel) continue;

            var effect = db.GetEffect(s.Effect);
            if (effect == null) continue;

            int nextLv = Mathf.Clamp(lv + 1, 1, maxSkillLevel);
            var ev = db.GetEffectValue(effect.EffectID, nextLv);
            if (ev == null) continue;

            pool.Add(s);
        }

        var results = new List<OfferVM>();
        for (int i = 0; i < offerCount && pool.Count > 0; i++)
        {
            int idx = UnityEngine.Random.Range(0, pool.Count);
            var skill = pool[idx];
            pool.RemoveAt(idx);

            int curLv = GetSkillLevel(skill.SkillID);
            int nextLv = Mathf.Clamp(curLv + 1, 1, maxSkillLevel);

            var effect = db.GetEffect(skill.Effect);
            var ev = db.GetEffectValue(effect.EffectID, nextLv);

            int min = Mathf.Min(ev.ValueMin, ev.ValueMax);
            int max = Mathf.Max(ev.ValueMin, ev.ValueMax);
            int rolled = UnityEngine.Random.Range(min, max + 1);

            string title = LocalizeOrFallback(skill.SkillName);
            string explainTemplate = LocalizeOrFallback(skill.SkillExplain);
            string desc = FormatExplain(explainTemplate, rolled);

            bool isExclusive = IsExclusiveSkillId(skill.SkillID);

            results.Add(new OfferVM
            {
                skillId = skill.SkillID,
                title = title,
                desc = desc,
                nextLevel = nextLv,
                rolledValue = rolled,
                isExclusive = isExclusive
            });
        }

        return results;
    }

    private string LocalizeOrFallback(string key)
    {
        if (!useLocalization) return key ?? "";
        if (loc == null || !loc.IsReady) return key ?? "";
        return loc.GetKO(key);
    }

    private void ApplyPickedSkill(OfferVM picked)
    {
        var skill = db.GetSkill(picked.skillId);
        if (skill == null) return;

        // ✅ “처음 획득”일 때만 획득순서 기록
        bool firstAcquired = (GetSkillLevel(picked.skillId) <= 0);
        if (firstAcquired) RegisterAcquiredIfFirstTime(picked.skillId);

        _skillLevel[picked.skillId] = picked.nextLevel;
        _skillRolledValue[picked.skillId] = picked.rolledValue;

        // ✅ 데이터 변경 이벤트( Pause 열려 있으면 UI 즉시 갱신 가능 )
        OnOwnedSkillsChanged?.Invoke();

        bool isExclusive = IsExclusiveSkillId(skill.SkillID);

        // =========================
        // ✅ 전용 스킬 선택 시 (일섬)
        // - 요구사항: 스킬에서 먹은 수치(예: 106%)를 바로 atkPercent로 반영
        // - sys: null로 넘기지 말 것(스크린샷처럼 None으로 덮임)
        // =========================
        if (isExclusive)
        {
            if (characterSkill == null)
            {
                Debug.LogError("[PlayerSkillSystem] CharacterSkill이 Player에 없습니다. Player 오브젝트에 CharacterSkill 컴포넌트를 추가하세요.");
                return;
            }

            float cd = ParseFloatOrDefault(skill.CoolTime, 10f);

            // ✅ 스킬에서 굴린 값(예: 106)을 그대로 전달
            int atkPercent = Mathf.Max(0, picked.rolledValue);

            // ✅ skillId 일치 보장(표시/추적 안정)
            characterSkill.skillId = skill.SkillID;

            characterSkill.EnableIlSum(
                stats: playerStats,
                sys: this,                 // ✅ null 금지
                newLevel: picked.nextLevel,
                newAtkPercent: atkPercent, // ✅ 100 고정 제거
                newCooldown: cd
            );

            characterSkill.monsterMask = monsterMask;
            characterSkill.monsterTag = monsterTag;

            if (logPick)
                Debug.Log($"[PlayerSkillSystem][Pick] Exclusive Skill Enabled id={skill.SkillID} Lv{picked.nextLevel} atk%(+)= {atkPercent} cd={cd}");

            return;
        }

        var effect = db.GetEffect(skill.Effect);
        if (effect == null) return;

        var type = (EffectType)effect.EffectType;
        var stat = (ReferenceStat)effect.ReferenseStat;
        var durationType = (DurationType)effect.DurationType;

        // ✅ Heal + Time => 쿨타임 기반 조건부 힐
        if (type == EffectType.Heal && durationType == DurationType.Time)
        {
            float cd = ParseFloatOrDefault(skill.CoolTime, 30f);

            _conditionalHealEnabled = true;
            _conditionalHealCooldown = Mathf.Max(0.1f, cd);

            _conditionalHealPercentOfCurrent = Mathf.Clamp(picked.rolledValue, 0f, 100f);
            _conditionalHealThresholdRatio = 0.3f;

            _conditionalHealNextReadyTime = Time.time;
            _conditionalHealSkillIdForHud = skill.SkillID;
            return;
        }

        switch (type)
        {
            case EffectType.Up:
                ApplyUp(stat, skill.SkillID, picked.rolledValue);
                break;

            case EffectType.Heal:
                ApplyHealPercent(picked.rolledValue);
                break;

            case EffectType.LifeSteal:
                _lifeStealEnabled = true;
                _lifeStealPercent = picked.rolledValue;
                break;

            case EffectType.Transfer:
                _transferEnabled = true;
                _transferPercent = picked.rolledValue;
                _transferCooldown = ParseFloatOrDefault(skill.CoolTime, 10f);
                _transferSkillIdForHud = skill.SkillID;
                break;

            default:
                Debug.LogWarning($"[PlayerSkillSystem] EffectType not implemented: {type}");
                break;
        }
    }

    // =========================
    // Effects
    // =========================
    private void ApplyUp(ReferenceStat stat, string sourceSkillId, int percent)
    {
        if (stat == ReferenceStat.SkillCooldown) return;

        if (stat == ReferenceStat.ExpGain)
        {
            _expGainBySkill[sourceSkillId] = percent;
            return;
        }

        if (playerStats == null)
        {
            Debug.LogError("[PlayerSkillSystem] playerStats(PlayerCharacterStats)가 연결되지 않았습니다.");
            return;
        }

        playerStats.SetPercentBuff(stat, sourceSkillId, percent);

        if (playerHealth != null) playerHealth.SyncMaxHp();
        if (attackSystem != null) attackSystem.ForceRefreshFromStats();
    }

    private void ApplyHealPercent(int percent)
    {
        if (playerHealth == null) return;
        if (percent <= 0) return;

        int amount = Mathf.RoundToInt(playerHealth.MaxHp * (percent / 100f));
        amount = Mathf.Max(0, amount);
        if (amount > 0) playerHealth.Heal(amount);
    }

    private void TickConditionalHeal()
    {
        if (!_conditionalHealEnabled) return;
        if (playerHealth == null) return;
        if (playerHealth.MaxHp <= 0) return;

        if (Time.time < _conditionalHealNextReadyTime) return;

        float ratio = playerHealth.Hp / (float)playerHealth.MaxHp;
        if (ratio > _conditionalHealThresholdRatio) return;

        int amount = Mathf.RoundToInt(playerHealth.Hp * (_conditionalHealPercentOfCurrent / 100f));
        _conditionalHealNextReadyTime = Time.time + _conditionalHealCooldown;

        if (amount <= 0) return;
        playerHealth.Heal(amount);
    }

    private void TryProcTransfer(Transform hitMonsterRoot, int originalDamage)
    {
        if (Time.time < _transferNextReadyTime) return;

        Transform firstNext = FindNearestMonster(hitMonsterRoot.position, exclude: new HashSet<Transform> { hitMonsterRoot });
        if (firstNext == null) return;

        _transferNextReadyTime = Time.time + Mathf.Max(0.01f, _transferCooldown);

        int transferDmg = Mathf.Max(1, Mathf.RoundToInt(originalDamage * (_transferPercent / 100f)));

        if (logTransfer)
            Debug.Log($"[Transfer] proc dmg={transferDmg}, cd={_transferCooldown}");

        var visited = new HashSet<Transform> { hitMonsterRoot };
        Transform current = hitMonsterRoot;

        for (int i = 0; i < transferMaxTargets; i++)
        {
            Transform next = FindNearestMonster(current.position, visited);
            if (next == null) break;

            visited.Add(next);

            var m = next.GetComponentInParent<Monster>();
            if (m != null) m.TakeFinalDamage(transferDmg);

            current = next;
        }
    }

    private Transform FindNearestMonster(Vector3 center, HashSet<Transform> exclude)
    {
        Collider[] hits = (monsterMask.value != 0)
            ? Physics.OverlapSphere(center, transferSearchRadius, monsterMask)
            : Physics.OverlapSphere(center, transferSearchRadius);

        Transform best = null;
        float bestDist = float.MaxValue;

        foreach (var c in hits)
        {
            if (c == null) continue;

            Transform t = c.transform;

            if (!string.IsNullOrWhiteSpace(monsterTag) && t.tag != monsterTag)
                continue;

            var root = t.GetComponentInParent<Monster>()?.transform ?? t;
            if (exclude.Contains(root)) continue;

            var ms = root.GetComponent<MonsterStat>();
            if (ms != null && ms.hp <= 0) continue;

            float d = (root.position - center).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = root;
            }
        }

        return best;
    }

    private int GetSkillLevel(string skillId) => _skillLevel.TryGetValue(skillId, out var lv) ? lv : 0;

    private void PauseGame()
    {
        _prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
    }

    private void ResumeGameIfPaused()
    {
        if (!pauseGameWhileSelecting) return;
        Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale;
    }

    private float ParseFloatOrDefault(string s, float def)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Trim() == "-") return def;

        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;

        if (float.TryParse(s, out v))
            return v;

        return def;
    }

    private string FormatExplain(string template, int x)
    {
        if (string.IsNullOrWhiteSpace(template)) return x.ToString();

        if (template.Contains("{0}"))
        {
            try { return string.Format(template, x); }
            catch { }
        }

        if (template.Contains("x") || template.Contains("X"))
            return template.Replace("x", x.ToString()).Replace("X", x.ToString());

        return $"{template} ({x})";
    }

    // ==========================================================
    // ✅ HUD(쿨타임) Getter 유지
    // ==========================================================
    public bool ConditionalHealEnabled => _conditionalHealEnabled;
    public float ConditionalHealCooldown => Mathf.Max(0.01f, _conditionalHealCooldown);
    public float ConditionalHealRemaining => Mathf.Max(0f, _conditionalHealNextReadyTime - Time.time);
    public string ConditionalHealSkillIdForHud => _conditionalHealSkillIdForHud;

    public bool TransferEnabled => _transferEnabled;
    public float TransferCooldown => Mathf.Max(0.01f, _transferCooldown);
    public float TransferRemaining => Mathf.Max(0f, _transferNextReadyTime - Time.time);
    public string TransferSkillIdForHud => _transferSkillIdForHud;

    // ==========================================================
    // ✅ ICooldownSkillProvider
    // ==========================================================
    public IEnumerable<ICooldownSkillSource> GetCooldownSkills()
    {
        _srcTransfer.SkillId = string.IsNullOrWhiteSpace(_transferSkillIdForHud) ? "SkillTransfer" : _transferSkillIdForHud;
        _srcTransfer.IsUnlocked = _transferEnabled;
        _srcTransfer.IsExclusive = false;
        _srcTransfer.CooldownDuration = Mathf.Max(0.01f, _transferCooldown);
        _srcTransfer.CooldownRemaining = Mathf.Max(0f, _transferNextReadyTime - Time.time);
        _srcTransfer.IsReady = _transferEnabled && (Time.time >= _transferNextReadyTime);

        if (_srcTransfer.IsUnlocked && _srcTransfer.CooldownDuration > 0.01f)
            yield return _srcTransfer;

        _srcConditionalHeal.SkillId = string.IsNullOrWhiteSpace(_conditionalHealSkillIdForHud) ? "SkillHeal" : _conditionalHealSkillIdForHud;
        _srcConditionalHeal.IsUnlocked = _conditionalHealEnabled;
        _srcConditionalHeal.IsExclusive = false;
        _srcConditionalHeal.CooldownDuration = Mathf.Max(0.01f, _conditionalHealCooldown);
        _srcConditionalHeal.CooldownRemaining = Mathf.Max(0f, _conditionalHealNextReadyTime - Time.time);
        _srcConditionalHeal.IsReady = _conditionalHealEnabled && (Time.time >= _conditionalHealNextReadyTime);

        if (_srcConditionalHeal.IsUnlocked && _srcConditionalHeal.CooldownDuration > 0.01f)
            yield return _srcConditionalHeal;
    }

    // ==========================================================
    // ✅ ICooldownSkillSource (호환 유지)
    // ==========================================================
    string ICooldownSkillSource.SkillId
        => string.IsNullOrWhiteSpace(_transferSkillIdForHud) ? "SkillTransfer" : _transferSkillIdForHud;

    bool ICooldownSkillSource.IsUnlocked => _transferEnabled;
    bool ICooldownSkillSource.IsExclusive => false;
    bool ICooldownSkillSource.IsReady => _transferEnabled && (Time.time >= _transferNextReadyTime);

    float ICooldownSkillSource.CooldownDuration => Mathf.Max(0.01f, _transferCooldown);
    float ICooldownSkillSource.CooldownRemaining => Mathf.Max(0f, _transferNextReadyTime - Time.time);

    // ==========================================================
    // ✅ ISkillInventorySource (Pause UI용)
    // ==========================================================
    public List<OwnedSkillIconInfo> GetOwnedSkillIcons()
    {
        var list = new List<OwnedSkillIconInfo>();

        foreach (var kv in _skillLevel)
        {
            string skillId = kv.Key;
            int lv = kv.Value;

            if (string.IsNullOrWhiteSpace(skillId)) continue;
            if (lv <= 0) continue;

            bool isExclusive = IsExclusiveSkillId(skillId);
            int acquiredIndex = GetAcquiredIndexOrRegister(skillId);

            Sprite icon = LoadIconBySkillId(skillId);
            if (icon == null && useDefaultWhenMissing && defaultIconForPause != null)
                icon = defaultIconForPause;

            list.Add(new OwnedSkillIconInfo(skillId, icon, isExclusive, acquiredIndex, lv));
        }

        return list;
    }

    private bool IsExclusiveSkillId(string skillId)
    {
        if (exclusiveSkillIds == null || exclusiveSkillIds.Count == 0) return false;
        for (int i = 0; i < exclusiveSkillIds.Count; i++)
        {
            if (string.Equals(exclusiveSkillIds[i], skillId, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void RegisterAcquiredIfFirstTime(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId)) return;
        if (_acquiredIndex.ContainsKey(skillId)) return;

        _acquiredIndex[skillId] = _acquireCounter;
        _acquireCounter++;
    }

    private int GetAcquiredIndexOrRegister(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId)) return int.MaxValue;
        if (_acquiredIndex.TryGetValue(skillId, out var idx)) return idx;

        RegisterAcquiredIfFirstTime(skillId);
        return _acquiredIndex.TryGetValue(skillId, out idx) ? idx : int.MaxValue;
    }

    // ✅ SkillSelectUI와 동일한 규칙(Resources.Load)
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
