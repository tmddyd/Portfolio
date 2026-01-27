using System;

public enum TargetType { Player, Mob }
public enum EffectType { Damage = 1, Up = 2, Heal = 3, LifeSteal = 4, Transfer = 5 }
public enum DurationType { Once = 1, Unlimited = 2, Flooring = 3, Time = 4 }

public enum ReferenceStat
{
    Atk = 1,
    Def = 2,
    Spd = 3,
    Crd = 4,
    Crp = 5,
    As = 6,
    Ar = 7,    // ✅ 시트와 동일
    MaxHp = 8,  // ✅ 시트와 동일
    ExpGain = 9,
    SkillCooldown = 10
}


[Serializable]
public class SkillRow
{
    public string SkillID;
    public string SkillName;
    public string Effect;
    public string CoolTime;
    public string SkillExplain;
}

[Serializable]
public class EffectRow
{
    public string EffectID;
    public string EffectValue;
    public string TargetType;
    public int EffectType;
    public int ReferenseStat;
    public int DurationType;
    public string DurationTime;
}

[Serializable]
public class EffectValueRow
{
    public string EffectValueID;
    public string EffectID;
    public int Level;
    public int ValueMax;
    public int ValueMin;
}
