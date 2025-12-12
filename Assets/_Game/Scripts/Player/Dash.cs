using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class Dash : MonoBehaviour
{
    [Header("Dash Core")]
    [SerializeField, Min(0.1f)] private float _dashDistance = 6f;
    [SerializeField, Min(0.05f)] private float _dashDuration = 0.15f;
    [SerializeField, Min(0f)] private float _cooldown = 0.8f;
    public float Cooldown { get { return _cooldown; } set { _cooldown = value; } }
    [Tooltip("가속/감속 곡선(시간 0~1)")]
    [SerializeField] private AnimationCurve _speedCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Collision & i-Frame")]
    [Tooltip("대시 중 벽을 관통하지 않도록 스윕.")]
    [SerializeField] private bool _stopAtWalls = true;
    [Tooltip("대시 중 무적 플래그를 Health에 전달")]
    [SerializeField] private bool _setIFrameOnHealth = true;
    [Tooltip("대시 동안 무시할 레이어 (예: Enemy, Projectile)")]
    [SerializeField] private LayerMask _ignoreLayersDuringDash;

    [Header("Movement Backend (자동 탐색)")]
    [SerializeField] private Rigidbody _rb;

    // ===== ScriptableObject References =====
    [Header("GameState Integration")]
    [SerializeField, Tooltip("Playing 상태에서만 대시 허용/진행")]
    private bool _respectGameState = true;
    [SerializeField, Tooltip("대시 진행 시간에 UnscaledTime 사용(보통 꺼둠)")]
    private bool _useUnscaledTimeForDash = false;
    [SerializeField, Tooltip("쿨다운에 UnscaledTime 사용(일시정지 중에도 쿨다운이 흐르게 하려면 켜기)")]
    private bool _useUnscaledTimeForCooldown = false;

    [Header("Debug")]
    [SerializeField] private bool _useLegacyInputForTest = false; // Space로 테스트
    [SerializeField] private KeyCode _legacyKey = KeyCode.Space;
    [SerializeField] private bool _drawGizmos = true;

    private GameState _gs;

    // 상태
    public bool IsDashing { get; private set; }
    public float CooldownRemaining
    {
        get
        {
            float now = NowTimeCD;
            return Mathf.Max(0f, (_lastDashEndTime + _cooldown) - now);
        }
    }
    public bool IsCooldown => CooldownRemaining > 0f;

    private Action _onDashUsed;
    public void AddListenerOnDashUsed(Action listener) => _onDashUsed += listener;
    public void RemoveListenerOnDashUsed(Action listener) => _onDashUsed -= listener;
    private Action _onDashReady;
    public void AddListenerOnDashReady(Action listener) => _onDashReady += listener;
    public void RemoveListenerOnDashReady(Action listener) => _onDashReady -= listener;

    // 내부
    private CapsuleCollider _capsule;
    private Collider[] _selfColliders;
    private Health _health; // 선택적 컴포넌트(무적 연동)
    private float _lastDashEndTime = -999f;
    private Vector3 _dashStart;
    private Vector3 _dashTarget;
    private Vector3 _dashDir;
    private float _dashT;   // 대시 진행도(Progress) 를 0→1 범위로 표현하는 정규화된 시간 값
    private bool _cooldownReadyFired;

    // 레이어 충돌 무시 복구용
    private int _selfLayer;
    private readonly bool[] _cachedIgnores = new bool[32];

    // 시간 소스
    private float DeltaTimeDash => _useUnscaledTimeForDash ? Time.unscaledDeltaTime : Time.deltaTime;
    private float NowTimeDash => _useUnscaledTimeForDash ? Time.unscaledTime : Time.time;
    private float NowTimeCD => _useUnscaledTimeForCooldown ? Time.unscaledTime : Time.time;

    private void Reset()
    {
        _rb = GetComponent<Rigidbody>();
    }

    private void Awake()
    {
        _gs = FindAnyObjectByType<GameState>();

        if (!_rb) _rb = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();
        _selfColliders = GetComponentsInChildren<Collider>(true);
        _health = GetComponent<Health>();
        _selfLayer = gameObject.layer;
    }

    private void Update()
    {
        // (옵션) 레거시 입력 테스트
        if (_useLegacyInputForTest && Input.GetKeyDown(_legacyKey))
        {
            if (CanOperateByGameState())
            {
                var dir = GetPlanarForwardToCursor() ?? transform.forward;
                TryDash(dir);
            }
        }

        // 진행/쿨다운
        bool canOperate = CanOperateByGameState();

        // 대시 중 이동 처리
        if (IsDashing)
        {
            // 상태상 진행 금지(Perk/Pause/GameOver)면 정지 유지
            if (!canOperate) return;

            _dashT += DeltaTimeDash / Mathf.Max(0.0001f, _dashDuration);
            float t = Mathf.Clamp01(_dashT);
            float speedScale = Mathf.Max(0f, _speedCurve.Evaluate(t));

            // 현재 목표 지점까지의 방향 및 잔여 거리
            Vector3 pos = GetPosition();
            Vector3 toTarget = _dashTarget - pos;
            float remain = toTarget.magnitude;

            // 프레임 이동량
            float totalDist = Vector3.Distance(_dashStart, _dashTarget);
            float idealStep = (totalDist / _dashDuration) * speedScale * DeltaTimeDash;
            float step = Mathf.Min(idealStep, remain);

            if (step > 0.0001f)
            {
                Vector3 next = pos + (remain > 0f ? toTarget / remain * step : Vector3.zero);
                SetPosition(next);
            }

            // 종료
            if (t >= 1f || remain <= 0.001f)
            {
                EndDash();
            }
        }
        else
        {
            // 쿨다운 끝났을 때 알림 1회
            if (!_cooldownReadyFired && NowTimeCD >= _lastDashEndTime + _cooldown)
            {
                _cooldownReadyFired = true;
            }
        }
    }

    /// <summary>
    /// 대시 시도: 이미 대시 중이거나 쿨다운이면 실패.
    /// dir은 평면 방향(정규화 필요 없음).
    /// </summary>
    public bool TryDash(Vector3 dir)
    {
        if (IsDashing) return false;
        if (NowTimeCD < _lastDashEndTime + _cooldown) return false;
        if (!CanOperateByGameState()) return false;

        _dashDir = dir;
        _dashDir.y = 0f;
        if (_dashDir.sqrMagnitude < 0.0001f)
        {
            _dashDir = transform.forward; // 방향 없는 경우 전방
        }

        _dashDir.Normalize();
        _dashStart = GetPosition();
        _dashTarget = _dashStart + _dashDir * _dashDistance;

        if (_stopAtWalls)
        {
            _dashTarget = ComputeWallSafeTarget(_dashStart, _dashTarget);
        }

        StartCoroutine(CoDash());

        return true;
    }

    private IEnumerator CoDash()
    {
        // 시작
        CooldownArm();
        BeginDashSideEffects();

        IsDashing = true;
        _dashT = 0f;

        // 종료까지 대기(Update에서 EndDash 호출)
        yield return new WaitUntil(() => !IsDashing);
    }

    private void EndDash()
    {
        if (!IsDashing) return;

        IsDashing = false;
        _lastDashEndTime = NowTimeCD;
        EndDashSideEffects();
    }

    private void CooldownArm()
    {
        _cooldownReadyFired = false;
    }

    private void BeginDashSideEffects()
    {
        // 물리 백엔드 보정
        if (_rb)
        {
            // 물리 간섭 줄이기: 순간이동형 MovePosition 기반 → 속도 0
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        // i-Frame
        if (_setIFrameOnHealth && _health != null)
        {
            _health.SetInvulnerable(true);
        }

        // 레이어 충돌 무시
        if (_ignoreLayersDuringDash.value != 0)
        {
            SetLayerIgnores(true);
        }
    }

    private void EndDashSideEffects()
    {
        if (_setIFrameOnHealth && _health != null)
        {
            _health.SetInvulnerable(false);
        }

        if (_ignoreLayersDuringDash.value != 0)
        {
            SetLayerIgnores(false);
        }
    }

    private void SetLayerIgnores(bool enable)
    {
        int myLayer = _selfLayer;
        for (int layer = 0; layer < 32; layer++)
        {
            if ((_ignoreLayersDuringDash.value & (1 << layer)) == 0)
                continue;

            if (enable)
            {
                _cachedIgnores[layer] = Physics.GetIgnoreLayerCollision(myLayer, layer);
                Physics.IgnoreLayerCollision(myLayer, layer, true);
            }
            else
            {
                // 이전 상태 복구(원래 무시 상태를 캐시)
                Physics.IgnoreLayerCollision(myLayer, layer, _cachedIgnores[layer]);
            }
        }
    }

    private Vector3 ComputeWallSafeTarget(Vector3 start, Vector3 target)
    {
        Vector3 dir = (target - start);
        float dist = dir.magnitude;
        if (dist < 0.0001f) return start;

        dir /= dist;

        float radius = 0.3f;
        float height = 2f;
        float skin = 0.02f;

        if (_capsule)
        {
            radius = _capsule.radius;
            height = Mathf.Max(_capsule.height, radius * 2f);
        }

        Vector3 center = start + Vector3.up * (height * 0.5f - radius);
        RaycastHit hit;

        // 캡슐 스윕
        bool blocked = Physics.CapsuleCast(center + Vector3.up * (height * 0.5f - radius),
                                           center - Vector3.up * (height * 0.5f - radius),
                                           radius, dir, out hit, dist, ~0,
                                           QueryTriggerInteraction.Ignore);

        if (blocked)
        {
            float safe = Mathf.Max(0f, hit.distance - skin);
            return start + dir * safe;
        }

        return target;
    }

    private Vector3 GetPosition()
    {
        if (_rb && _rb.interpolation != RigidbodyInterpolation.None)
        {
            return _rb.position;
        }

        return transform.position;
    }

    private void SetPosition(Vector3 pos)
    {
        if (_rb)
        {
            _rb.MovePosition(pos);
        }
        else
        {
            transform.position = pos;
        }
    }

    /// <summary>마우스 커서 기준 전방(탑다운 테스트용). 실패 시 null.</summary>
    private Vector3? GetPlanarForwardToCursor()
    {
        if (!Camera.main) return null;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane plane = new Plane(Vector3.up, new Vector3(0, transform.position.y, 0));
        if (plane.Raycast(ray, out float d))
        {
            Vector3 hit = ray.GetPoint(d);
            Vector3 dir = (hit - transform.position);
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f) return null;

            return dir.normalized;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos) return;

        if (IsDashing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(_dashStart, _dashTarget);
            Gizmos.DrawWireSphere(_dashStart, 0.1f);
            Gizmos.DrawWireSphere(_dashTarget, 0.1f);
        }
    }
#endif

    // === GameState ===
    private bool CanOperateByGameState()
    {
        if (!_respectGameState) return true;
        if (_gs == null) return true;
        bool result = false;
        result = _gs.IsPlayable()
            && !_gs.IsInputLocked();

        return result;
    }
}
