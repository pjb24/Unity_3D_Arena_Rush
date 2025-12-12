// Scripts/UI/HUD_Interfaces.cs
// HUD에서 참조하는 최소 읽기 전용 인터페이스

public interface IHealthInfo
{
    public int MaxHP { get; }
    public int CurrentHP { get; }
}

public interface IGunInfo
{
    public float FireRate { get; }   // 초당 발사수
    public bool IsReloading { get; }
    public float ReloadRemaining { get; }  // s
}

public interface IWaveInfo
{
    public int CurrentWave { get; }
    public int AliveEnemies { get; }
}

public interface IDashInfo
{
    public bool IsDashing { get; }          // 현재 대시 중 여부
    public bool IsCooldown { get; }         // 쿨다운 중 여부
    public float CooldownTotal { get; }     // 총 쿨다운(초)
    public float CooldownRemaining { get; } // 남은 쿨다운(초)
    public float CooldownPercent { get; }   // 0~1 (남은/총)
}
