using UnityEngine;

public class HUD_Adapter_Health : MonoBehaviour, IHealthInfo
{
    private Health _src;

    public int MaxHP => _src != null ? _src.MaxHP : 0;
    public int CurrentHP => _src != null ? _src.CurrentHP : 0;

    public static IHealthInfo Attach(Health src)
    {
        if (src == null) return null;
        var a = src.GetComponent<HUD_Adapter_Health>();
        if (a == null) a = src.gameObject.AddComponent<HUD_Adapter_Health>();
        a._src = src;
        return a;
    }
}
