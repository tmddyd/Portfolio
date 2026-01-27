public interface IUnitStatProvider
{ //전투 스탯 인터페이스
 
    int MaxHp { get; }
    int Atk { get; }
    int Def { get; }

    // 0~1 (예: 0.25 = 25%)
    float CritChance01 { get; }

    // 추가 치명타 피해량(예: 0.8 = +80%)
    float ExtraCritBonus { get; }
}
