// DamageText.cs
// 대미지 수치 플로팅 텍스트(월드좌표 트래킹 + 화면상 상승/감쇠/스케일)
// 의존: TextMeshPro, Canvas(Screen Space)

using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class DamageTextUI : MonoBehaviour
{
    public enum E_Style { Normal, Critical, Heal }

    [Header("Refs")]
    [SerializeField] private TMP_Text _text;
    [SerializeField] private RectTransform _rt;

    [Header("Motion")]
    [SerializeField, Range(0.1f, 3f)] private float _lifetime = 1.1f;
    [SerializeField, Range(0f, 800f)] private float _riseSpeedPx = 120f; // 화면 픽셀/초
    [SerializeField] private AnimationCurve _alphaCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    [SerializeField] private AnimationCurve _scaleCurve = AnimationCurve.EaseInOut(0, 0.9f, 1, 1.1f);

    [Header("Spawn Spread (px)")]
    [SerializeField, Range(0f, 80f)] private float _spawnSpreadPx = 24f;

    // runtime
    private float _t;
    private Vector2 _screenBase;   // 스폰 기준 스크린 좌표
    private Vector2 _offset;       // 스폰 시 난수 오프셋(px)
    private Camera _cam;
    private Vector3 _worldPos;     // 트래킹할 월드 좌표(히트 지점)

    private void Awake()
    {
        if (_rt == null) _rt = transform as RectTransform;
        if (_text == null) _text = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnEnable()
    {
        _t = 0f;
        ApplyVisual(0f);
    }

    private void LateUpdate()
    {
        _t += Time.deltaTime;
        if (_t >= _lifetime)
        {
            DamageTextManagerUI.Instance?.Despawn(this);
            return;
        }

        // 월드→스크린 변환(카메라 이동 시 보정)
        if (_cam != null)
        {
            var sp = (Vector2)_cam.WorldToScreenPoint(_worldPos);
            _screenBase = sp;
        }

        // 상승 처리(스크린 공간)
        var rise = Vector2.up * (_riseSpeedPx * _t);
        _rt.anchoredPosition = _screenBase + _offset + rise;

        ApplyVisual(_t / _lifetime);
    }

    private void ApplyVisual(float normalizedTime)
    {
        float a = Mathf.Clamp01(_alphaCurve.Evaluate(normalizedTime));
        float s = Mathf.Max(0.01f, _scaleCurve.Evaluate(normalizedTime));

        if (_text != null)
        {
            var col = _text.color;
            col.a = a;
            _text.color = col;
        }

        transform.localScale = Vector3.one * s;
    }

    public void Show(int amount, Vector3 worldPos, Camera cam, E_Style style, Color colorOverride)
    {
        _worldPos = worldPos;
        _cam = cam != null ? cam : Camera.main;

        // 초기 스크린 좌표
        _screenBase = _cam != null ? (Vector2)_cam.WorldToScreenPoint(_worldPos) : Vector2.zero;

        // 스폰 시 난수 오프셋(px)
        _offset = Random.insideUnitCircle * _spawnSpreadPx;

        if (_text != null)
        {
            _text.text = amount.ToString();
            _text.color = colorOverride;
            _text.fontStyle = (style == E_Style.Critical) ? FontStyles.Bold : FontStyles.Normal;
            _text.outlineWidth = (style == E_Style.Critical) ? 0.15f : 0.05f;
        }

        _t = 0f;
        ApplyVisual(0f);
    }
}
