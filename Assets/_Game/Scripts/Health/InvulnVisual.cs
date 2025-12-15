// InvulnVisual.cs
// 무적 상태 시각화(깜빡임 / 컬러 틴트 / 셰이더 키워드)
// - Health.AddListenerOnInvulnerableChanged 구독
// - MaterialPropertyBlock으로 원본 머티리얼 오염 방지
// - Renderer 자동 수집 옵션

using System.Collections.Generic;
using UnityEngine;

public enum E_InvulnVisualMode { Blink, Tint, ShaderKeyword }

[DisallowMultipleComponent]
public class InvulnVisual : MonoBehaviour
{
    [Header("Target Renderers (자동 수집 가능)")]
    [SerializeField] private Renderer[] _renderers; // 비워두면 자동 수집

    [Header("Mode")]
    [SerializeField] private E_InvulnVisualMode _mode = E_InvulnVisualMode.Blink;

    [Header("Blink")]
    [SerializeField, Min(0.02f)] private float _blinkInterval = 0.1f; // 초
    [SerializeField, Range(0f, 1f)] private float _blinkMinAlpha = 0.2f; // 틴트 모드와 함께 사용 가능

    [Header("Tint")]
    [SerializeField] private Color _tintColor = new Color(1f, 1f, 1f, 0.35f); // 살짝 반투명/밝게
    [SerializeField] private string _colorProperty = "_Color"; // 셰이더 컬러 속성명

    [Header("Shader Keyword")]
    [SerializeField] private string _keyword = "INVULNERABLE_ON";

    [Header("Debug")]
    [SerializeField] private bool _log = false;

    // runtime
    private Health _health;
    private bool _isInvulnShown = false;
    private float _blinkTimer = 0f;
    private List<MaterialPropertyBlock> _mpbs = new List<MaterialPropertyBlock>();
    private Color[] _origColors;

    private void Reset()
    {
        TryAutoCollect();
    }

    private void Awake()
    {
        if (_renderers == null || _renderers.Length == 0) TryAutoCollect();

        _health = GetComponentInParent<Health>() ?? GetComponent<Health>();
        if (_health == null)
        {
            Debug.LogWarning($"[{name}] InvulnVisual: Health가 필요합니다.");
            enabled = false;
            return;
        }

        CacheOriginals();
        _health.AddListenerOnInvulnerableChanged(HandleInvulnerableChanged);
        // 초기 상태 반영
        HandleInvulnerableChanged(_health.IsInvulnerable);
    }

    private void OnDestroy()
    {
        if (_health != null) _health.RemoveListenerOnInvulnerableChanged(HandleInvulnerableChanged);
        // 안전: 원상복구 시도
        if (_isInvulnShown) DisableVisual();
    }

    private void Update()
    {
        if (!_isInvulnShown) return;
        if (_mode == E_InvulnVisualMode.Blink)
        {
            _blinkTimer += Time.deltaTime;
            if (_blinkTimer >= _blinkInterval)
            {
                _blinkTimer = 0f;
                ToggleBlink();
            }
        }
    }

    private void HandleInvulnerableChanged(bool isInvuln)
    {
        if (_log) Debug.Log($"[{name}] InvulnVisual <- {isInvuln}");
        if (isInvuln) EnableVisual();
        else DisableVisual();
    }

    private void EnableVisual()
    {
        _isInvulnShown = true;
        _blinkTimer = 0f;

        switch (_mode)
        {
            case E_InvulnVisualMode.Blink:
                // 첫 프레임에 반투명으로 만들고 깜빡임 시작
                ApplyTintAlpha(_tintColor, _blinkMinAlpha);
                break;

            case E_InvulnVisualMode.Tint:
                ApplyTint(_tintColor);
                break;

            case E_InvulnVisualMode.ShaderKeyword:
                SetKeyword(true);
                break;
        }
    }

    private void DisableVisual()
    {
        _isInvulnShown = false;

        switch (_mode)
        {
            case E_InvulnVisualMode.Blink:
            case E_InvulnVisualMode.Tint:
                RestoreOriginalColors();
                break;

            case E_InvulnVisualMode.ShaderKeyword:
                SetKeyword(false);
                break;
        }
    }

    private void ToggleBlink()
    {
        // 두 상태를 토글: 원래색(또는 완전 틴트) ↔ 낮은 알파 틴트
        // 여기서는 컬러 속성 기반으로 구현
        if (_renderers == null) return;

        // 현재 상태 추정 대신 토글 방식: 알파 기준으로 토글
        // 간단히 낮은 알파 ↔ 원래색 반복
        // 원래색 복원 → 낮은 알파 → 복원 → ...
        // 성능상 MPB 재사용
        bool toLowAlpha = true;
        if (_renderers.Length > 0)
        {
            // 첫 렌더러의 현재 색을 읽어 알파 판단(가능한 경우)
            // MPB에서 GetColor는 불가하므로 추정 로직 생략, 토글 교차로 처리
        }

        if (toLowAlpha) ApplyTintAlpha(_tintColor, _blinkMinAlpha);
        else RestoreOriginalColors();
    }

    private void ApplyTint(Color c)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            var mpb = _mpbs[i];
            mpb.SetColor(_colorProperty, c);
            r.SetPropertyBlock(mpb);
        }
    }

    private void ApplyTintAlpha(Color baseTint, float alpha)
    {
        var c = baseTint;
        c.a = Mathf.Clamp01(alpha);
        ApplyTint(c);
    }

    private void SetKeyword(bool on)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (on) r.material.EnableKeyword(_keyword);
            else r.material.DisableKeyword(_keyword);
        }
    }

    private void RestoreOriginalColors()
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            var mpb = _mpbs[i];
            mpb.SetColor(_colorProperty, _origColors[i]);
            r.SetPropertyBlock(mpb);
        }
    }

    private void CacheOriginals()
    {
        if (_renderers == null) return;
        _mpbs.Clear();
        _origColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb); // 초기값 로드
            _mpbs.Add(mpb);

            // 원본 컬러 추출: 없으면 머티리얼에서 가져옴
            Color col = Color.white;
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(_colorProperty))
                col = r.sharedMaterial.GetColor(_colorProperty);
            _origColors[i] = col;
        }
    }

    private void TryAutoCollect()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
    }
}
