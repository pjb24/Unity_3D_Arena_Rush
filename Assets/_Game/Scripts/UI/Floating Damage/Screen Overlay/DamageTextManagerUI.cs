// DamageTextManager.cs
// 스폰/디스폰 관리. Pooler가 있으면 사용, 없으면 Instantiate/Destroy.
// 전역 접근을 위한 최소 싱글턴(테스트 용도).

using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class DamageTextManagerUI : MonoBehaviour
{
    public static DamageTextManagerUI Instance { get; private set; }

    [Header("Prefab & Parent")]
    [SerializeField] private DamageTextUI _prefab;
    [SerializeField] private Canvas _screenCanvas; // Screen Space(Overlay/Camera)

    [Header("Colors")]
    [SerializeField] private Color _colorNormal = new Color(1f, 0.95f, 0.2f); // 노란 톤
    [SerializeField] private Color _colorCritical = new Color(1f, 0.3f, 0.2f); // 붉은 톤
    [SerializeField] private Color _colorHeal = new Color(0.3f, 1f, 0.5f);     // 녹색 톤

    // 선택 의존
    private Pooler _pooler;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _pooler = FindAnyObjectByType<Pooler>();

        // Canvas 보정
        if (_screenCanvas == null)
        {
            // 없으면 자동 생성(Overlay)
            var go = new GameObject("DamageTextCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _screenCanvas = go.GetComponent<Canvas>();
            _screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.transform.SetParent(transform);
        }
    }

    public DamageTextUI Spawn()
    {
        GameObject go = null;

        if (_pooler != null && _prefab != null)
        {
            go = _pooler.Spawn(_prefab.gameObject, Vector3.zero, Quaternion.identity);
        }
        else if (_prefab != null)
        {
            go = Instantiate(_prefab.gameObject);
        }

        if (go == null) return null;

        var rt = go.transform as RectTransform;
        rt.SetParent(_screenCanvas.transform, worldPositionStays: false);
        go.SetActive(true);
        var dt = go.GetComponent<DamageTextUI>();
        return dt;
    }

    public void Despawn(DamageTextUI dt)
    {
        if (dt == null) return;
        var go = dt.gameObject;
        if (_pooler != null)
        {
            _pooler.Despawn(go);
        }
        else
        {
            Destroy(go);
        }
    }

    public void ShowDamage(int amount, Vector3 worldPos, bool isCritical = false, Camera cam = null)
    {
        var dt = Spawn();
        if (dt == null) return;

        var style = isCritical ? DamageTextUI.E_Style.Critical : DamageTextUI.E_Style.Normal;
        var color = isCritical ? _colorCritical : _colorNormal;
        dt.Show(amount, worldPos, cam != null ? cam : Camera.main, style, color);
    }

    public void ShowHeal(int amount, Vector3 worldPos, Camera cam = null)
    {
        var dt = Spawn();
        if (dt == null) return;

        dt.Show(amount, worldPos, cam != null ? cam : Camera.main, DamageTextUI.E_Style.Heal, _colorHeal);
    }
}
