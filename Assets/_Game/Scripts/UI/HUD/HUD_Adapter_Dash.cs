using UnityEngine;

public class HUD_Adapter_Dash : MonoBehaviour, IDashInfo
{
    private Dash _src;

    public bool IsDashing => _src != null && _src.IsDashing;

    public bool IsCooldown => _src != null && _src.IsCooldown;

    public float CooldownTotal => _src != null ? _src.Cooldown : 0f;

    public float CooldownRemaining => _src != null ? _src.CooldownRemaining : 0f;

    public float CooldownPercent => (CooldownTotal > 0f) ? Mathf.Clamp01(CooldownRemaining / CooldownTotal) : 0f;

    public static IDashInfo Attach(Dash src)
    {
        if (src == null) return null;
        var a = src.GetComponent<HUD_Adapter_Dash>();
        if (a == null) a = src.gameObject.AddComponent<HUD_Adapter_Dash>();
        a._src = src;
        return a;
    }
}
