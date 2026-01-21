using System;

[Serializable]
public class WaveDataRow
{
    public string WaveID;

    // 최대 5개 (비어있으면 미사용)
    public string[] WaveMobIDs = new string[5];

    public float Duration;
    public int ClearScore;

    public float HpMul = 1f;
    public float AtkMul = 1f;
    public float DefMul = 1f;
    public float ScoreMul = 1f;
}

[Serializable]
public class WaveMobRow
{
    public string WaveMobID;   // 예: "WM01"
    public string MobID;       // 예: "M001" 또는 "Goblin"
    public int MobCount;
    public float SpawnRate;
}
