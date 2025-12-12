// DamageTextManager.cs
// 스폰/디스폰 관리. Pooler가 있으면 사용, 없으면 Instantiate/Destroy.
// 전역 접근을 위한 최소 싱글턴(테스트 용도).

using UnityEngine;

[DisallowMultipleComponent]
public class DamageTextManager : MonoBehaviour
{
    public static DamageTextManager Instance { get; private set; }

    [Header("Prefab & Parent")]
    [SerializeField] private DamageText _prefab;
    [SerializeField] private Transform _worldCanvasRoot; // World Space Canvas 추천

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
        if (_worldCanvasRoot == null)
        {
            // 없으면 임시 루트 생성(월드 스페이스 캔버스가 이상적)
            var go = new GameObject("DamageTextRoot");
            go.transform.SetParent(transform);
            _worldCanvasRoot = go.transform;
        }
    }

    public DamageText Spawn()
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

        if (_worldCanvasRoot != null) go.transform.SetParent(_worldCanvasRoot, worldPositionStays: false);
        var dt = go.GetComponent<DamageText>();
        go.SetActive(true);
        return dt;
    }

    public void Despawn(DamageText dt)
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

    public void ShowDamage(int amount, Vector3 worldPos, bool isCritical = false)
    {
        var dt = Spawn();
        if (dt == null) return;
        var style = isCritical ? DamageText.E_Style.Critical : DamageText.E_Style.Normal;
        var color = isCritical ? _colorCritical : _colorNormal;
        dt.Show(amount, worldPos, style, color);
    }

    public void ShowHeal(int amount, Vector3 worldPos)
    {
        var dt = Spawn();
        if (dt == null) return;
        dt.Show(amount, worldPos, DamageText.E_Style.Heal, _colorHeal);
    }
}
