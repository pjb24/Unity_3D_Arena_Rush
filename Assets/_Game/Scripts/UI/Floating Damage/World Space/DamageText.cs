// DamageText.cs
// 대미지 수치 플로팅 텍스트(월드 좌표 → 카메라 빌보드, 상승/감쇠/스케일)
// 의존: TextMeshPro

using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class DamageText : MonoBehaviour
{
    public enum E_Style { Normal, Critical, Heal }

    [Header("Refs")]
    [SerializeField] private TMP_Text _text;

    [Header("Motion")]
    [SerializeField, Range(0.1f, 3f)] private float _lifetime = 1.1f;
    [SerializeField, Range(0f, 8f)] private float _riseSpeed = 1.5f;
    [SerializeField, Range(0f, 5f)] private float _gravity = 0.0f;
    [SerializeField] private AnimationCurve _alphaCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    [SerializeField] private AnimationCurve _scaleCurve = AnimationCurve.EaseInOut(0, 0.9f, 1, 1.1f);

    // runtime
    private float _t;
    private Vector3 _velocity;
    private Transform _cam;

    private void Awake()
    {
        if (_text == null) _text = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnEnable()
    {
        _t = 0f;
        _velocity = Vector3.up * _riseSpeed;
        if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
        UpdateBillboard();
        ApplyVisual(0f);
    }

    private void LateUpdate()
    {
        _t += Time.deltaTime;
        if (_t >= _lifetime)
        {
            DamageTextManager.Instance?.Despawn(this);
            return;
        }

        _velocity += Vector3.down * _gravity * Time.deltaTime;
        transform.position += _velocity * Time.deltaTime;

        UpdateBillboard();
        ApplyVisual(_t / _lifetime);
    }

    private void UpdateBillboard()
    {
        if (_cam == null) { var c = Camera.main; if (c) _cam = c.transform; }
        if (_cam != null)
        {
            transform.forward = -(_cam.position - transform.position).normalized;
        }
    }

    private void ApplyVisual(float normalizedTime)
    {
        float a = Mathf.Clamp01(_alphaCurve.Evaluate(normalizedTime));
        float s = Mathf.Max(0.01f, _scaleCurve.Evaluate(normalizedTime));
        var col = _text.color; col.a = a; _text.color = col;
        transform.localScale = Vector3.one * s;
    }

    public void Show(int amount, Vector3 worldPos, E_Style style, Color colorOverride, float randomSpread = 0.2f)
    {
        // 약간의 노이즈로 겹침 방지
        Vector2 n = Random.insideUnitCircle * randomSpread;
        transform.position = worldPos + new Vector3(n.x, 0f, n.y);

        if (_text != null)
        {
            _text.text = amount.ToString();
            _text.color = colorOverride;
            _text.fontStyle = (style == E_Style.Critical) ? FontStyles.Bold : FontStyles.Normal;
            _text.outlineWidth = (style == E_Style.Critical) ? 0.15f : 0.05f;
        }

        _t = 0f;
        _velocity = Vector3.up * _riseSpeed;
        ApplyVisual(0f);
    }
}
