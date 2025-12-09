// Scripts/UI/HUD_Interfaces.cs
// HUD에서 참조하는 최소 읽기 전용 인터페이스

public interface IHealthInfo
{
    public float MaxHP { get; }
    public float CurrentHP { get; }
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
