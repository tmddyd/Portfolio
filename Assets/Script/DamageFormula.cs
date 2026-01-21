using UnityEngine;

public struct DamageResult
{
    public int damage;
    public bool isCrit;
    public float raw;
}

public static class DamageFormula
{
    // ((Atk) * (Coef) * { 1 - Def/(Def+K) } * [ 1 + Crit*(BaseCrit+ExtraCrit) ]) * ExtraDamage
    public static DamageResult Compute(
        IUnitStatProvider attacker,
        IUnitStatProvider defender,
        float attackCoef,
        float defenseK,
        float baseCritBonus,
        float extraDamageMul,
        bool? forceCrit = null
    )
    {
        float atk = Mathf.Max(0f, attacker != null ? attacker.Atk : 0f);
        float def = Mathf.Max(0f, defender != null ? defender.Def : 0f);

        float coef = Mathf.Max(0f, attackCoef);
        float k = Mathf.Max(0.0001f, defenseK);
        float extraMul = Mathf.Max(0f, extraDamageMul);

        float defenseFactor = 1f - (def / (def + k));
        defenseFactor = Mathf.Clamp01(defenseFactor);

        float p = attacker != null ? Mathf.Clamp01(attacker.CritChance01) : 0f;

        // ✅ 핵심: 외부에서 강제한 crit이 있으면 그걸 사용
        bool isCrit = forceCrit ?? (UnityEngine.Random.value < p);

        float extraCrit = attacker != null ? Mathf.Max(0f, attacker.ExtraCritBonus) : 0f;
        float critFactor = 1f + (isCrit ? 1f : 0f) * (Mathf.Max(0f, baseCritBonus) + extraCrit);

        float raw = (atk * coef * defenseFactor * critFactor) * extraMul;

        int dmg = Mathf.FloorToInt(raw);
        if (dmg < 1) dmg = 1;

        return new DamageResult { damage = dmg, isCrit = isCrit, raw = raw };
    }
}
