using UnityEngine;

public class HUD_Adapter_Wave : MonoBehaviour, IWaveInfo
{
    private WaveManager _src;

    public int CurrentWave => _src != null ? _src.CurrentWave : 0;
    public int AliveEnemies => _src != null ? _src.AliveEnemies : 0;

    public static IWaveInfo Attach(WaveManager src)
    {
        if (src == null) return null;
        var a = src.GetComponent<HUD_Adapter_Wave>();
        if (a == null) a = src.gameObject.AddComponent<HUD_Adapter_Wave>();
        a._src = src;
        return a;
    }
}
