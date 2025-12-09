using UnityEngine;

public class HUD_Adapter_Gun : MonoBehaviour, IGunInfo
{
    private Gun _src;

    public float FireRate => _src != null ? _src.FireRate : 0f;
    public bool IsReloading => _src != null && _src.IsReloading;
    public float ReloadRemaining => _src != null ? _src.ReloadRemaining : 0f;

    public static IGunInfo Attach(Gun src)
    {
        if (src == null) return null;
        var a = src.GetComponent<HUD_Adapter_Gun>();
        if (a == null) a = src.gameObject.AddComponent<HUD_Adapter_Gun>();
        a._src = src;
        return a;
    }
}
