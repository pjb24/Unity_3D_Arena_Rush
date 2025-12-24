using System.Collections.Generic;
using UnityEngine;

public enum E_FadeMode
{
    None = 0,
    FadeObstacles = 1,
}

[DisallowMultipleComponent]
public class ObstacleFaderDither : MonoBehaviour
{
    private static readonly int _pidFade = Shader.PropertyToID("_Fade");

    [Header("Refs")]
    [SerializeField] private Transform _target;
    [SerializeField] private Camera _renderCamera;

    [Header("Mode")]
    [SerializeField] private E_FadeMode _mode = E_FadeMode.FadeObstacles;

    [Header("Cast")]
    [SerializeField] private LayerMask _obstacleMask;
    [SerializeField, Range(0.01f, 1.0f)] private float _sphereRadius = 0.25f;
    [SerializeField, Range(-2f, 4f)] private float _targetOffsetY = 1.0f;

    [Header("Fade")]
    [SerializeField, Range(0.0f, 1.0f)] private float _fadeAlpha = 0.25f;     // 가릴 때 목표 Fade
    [SerializeField, Range(1f, 40f)] private float _fadeSpeed = 14f;          // 보간 속도
    [SerializeField] private bool _useUnscaledTime = true;

    [Header("Safety")]
    [SerializeField] private bool _ignoreTriggers = true;
    [SerializeField] private bool _includeParentRenderers = true;
    [SerializeField] private bool _includeChildRenderers = true;

    [Header("Debug")]
    [SerializeField] private bool _debugDraw = false;

    private readonly Dictionary<Renderer, FadeEntry> _entries = new(128);
    private readonly HashSet<Renderer> _hitThisFrame = new();

    private struct FadeEntry
    {
        public Renderer _renderer;
        public float _currentFade;
        public bool _hasFadeProperty;
        public MaterialPropertyBlock _mpb;
    }

    private void Awake()
    {
        if (_renderCamera == null) _renderCamera = Camera.main;
    }

    private void OnDisable()
    {
        // 안전: 비활성화 시 전부 원복
        foreach (var kv in _entries)
        {
            if (kv.Key == null) continue;
            SetFade(kv.Key, 1f);
        }
        _entries.Clear();
        _hitThisFrame.Clear();
    }

    private void LateUpdate()
    {
        if (_mode == E_FadeMode.None) return;
        if (_target == null) return;

        if (_renderCamera == null) _renderCamera = Camera.main;
        if (_renderCamera == null) return;

        _hitThisFrame.Clear();

        Vector3 camPos = _renderCamera.transform.position;
        Vector3 targetPos = _target.position + Vector3.up * _targetOffsetY;

        Vector3 dir = targetPos - camPos;
        float dist = dir.magnitude;
        if (dist <= 0.001f) return;
        dir /= dist;

        var qti = _ignoreTriggers ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide;
        var hits = Physics.SphereCastAll(camPos, _sphereRadius, dir, dist, _obstacleMask, qti);

        // 1) 이번 프레임 가림 대상 수집
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null) continue;

            CollectRenderers(col.transform);
        }

        // 2) 이번 프레임 가리는 대상: FadeOut
        float dt = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float t = Mathf.Clamp01(dt * _fadeSpeed);

        foreach (var ren in _hitThisFrame)
        {
            if (ren == null) continue;
            var e = EnsureEntry(ren);
            if (!e._hasFadeProperty) continue;

            float next = Mathf.Lerp(e._currentFade, _fadeAlpha, t);
            e._currentFade = next;
            _entries[ren] = e;

            ApplyFade(e);
        }

        // 3) 더 이상 가리지 않는 대상: FadeIn(원복)
        //    (Dictionary 순회 중 수정 안전: 키 리스트 복사)
        if (_entries.Count > 0)
        {
            _tmpKeys.Clear();
            foreach (var kv in _entries) _tmpKeys.Add(kv.Key);

            for (int k = 0; k < _tmpKeys.Count; k++)
            {
                var ren = _tmpKeys[k];
                if (ren == null) continue;
                if (_hitThisFrame.Contains(ren)) continue;

                var e = _entries[ren];
                if (!e._hasFadeProperty) continue;

                float next = Mathf.Lerp(e._currentFade, 1f, t);
                e._currentFade = next;
                _entries[ren] = e;

                ApplyFade(e);

                // 충분히 복귀하면 관리에서 제거(옵션)
                if (e._currentFade >= 0.995f)
                {
                    // 최종 값 1로 고정(드리프트 방지)
                    SetFade(ren, 1f);
                    _entries.Remove(ren);
                }
            }
        }

        if (_debugDraw)
        {
            Debug.DrawLine(camPos, targetPos, Color.yellow);
        }
    }

    // ----- 내부 유틸 -----

    private readonly List<Renderer> _tmpRenderers = new(16);
    private readonly List<Renderer> _tmpParents = new(8);
    private readonly List<Renderer> _tmpChildren = new(8);
    private readonly List<Renderer> _tmpKeys = new(256);

    private void CollectRenderers(Transform tr)
    {
        _tmpRenderers.Clear();

        if (_includeChildRenderers)
        {
            _tmpChildren.Clear();
            tr.GetComponentsInChildren(true, _tmpChildren);
            for (int i = 0; i < _tmpChildren.Count; i++)
                _tmpRenderers.Add(_tmpChildren[i]);
        }

        if (_includeParentRenderers)
        {
            _tmpParents.Clear();
            tr.GetComponentsInParent(true, _tmpParents);
            for (int i = 0; i < _tmpParents.Count; i++)
                _tmpRenderers.Add(_tmpParents[i]);
        }

        for (int i = 0; i < _tmpRenderers.Count; i++)
        {
            var ren = _tmpRenderers[i];
            if (ren == null) continue;

            // 성능: 같은 렌더러 중복 방지
            _hitThisFrame.Add(ren);
            if (!_entries.ContainsKey(ren))
            {
                // 엔트리 생성은 늦게(EnsureEntry에서)
                // 여기서는 hit만 체크
            }
        }
    }

    private FadeEntry EnsureEntry(Renderer ren)
    {
        if (_entries.TryGetValue(ren, out var e))
            return e;

        e = new FadeEntry
        {
            _renderer = ren,
            _currentFade = 1f,
            _mpb = new MaterialPropertyBlock(),
            _hasFadeProperty = HasFadeProperty(ren),
        };

        _entries[ren] = e;
        return e;
    }

    private bool HasFadeProperty(Renderer ren)
    {
        // Renderer가 여러 머티리얼을 가질 수 있음: 하나라도 _Fade 지원하면 적용
        var mats = ren.sharedMaterials;
        if (mats == null || mats.Length == 0) return false;

        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (m == null) continue;
            if (m.HasProperty(_pidFade)) return true;
        }
        return false;
    }

    private void ApplyFade(FadeEntry e)
    {
        if (e._renderer == null) return;

        e._renderer.GetPropertyBlock(e._mpb);
        e._mpb.SetFloat(_pidFade, e._currentFade);
        e._renderer.SetPropertyBlock(e._mpb);
    }

    private void SetFade(Renderer ren, float fade01)
    {
        if (ren == null) return;
        var mpb = new MaterialPropertyBlock();
        ren.GetPropertyBlock(mpb);
        mpb.SetFloat(_pidFade, fade01);
        ren.SetPropertyBlock(mpb);
    }
}
