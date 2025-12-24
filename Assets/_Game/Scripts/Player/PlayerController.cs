// PlayerController.cs
// 입력 & 플레이어 이동(탑다운 3D) + Dash 연동 + GameState 연동
// - GameState.IsPlayable()/IsInputLocked()에 따라 이동·대시·입력 처리 차단
// - Health 사망 이벤트 시 GameState.TriggerGameOver()
//
// - Aim 상태: 마우스 방향 바라봄
// - Dash 중: Aim보다 우선하여 "Dash 방향(= _lastMoveDir)"을 바라봄
// - 비 Aim 상태: 이동 방향 바라봄
//
// 의존: PlayerInput (Unity Input System)
// 액션 이름: "Move"(Vector2), "Dash"(Button), "Fire"(Button)

using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    // ===== ScriptableObject References =====
    [Header("ScriptableObject Events")]
    public GameEventSO OnPlayerDiedEvent; // 플레이어 사망 이벤트

    [Header("Refs")]
    [SerializeField] private Gun _gun;
    [SerializeField] private CrosshairAim _aim;

    [Header("Move")]
    [SerializeField, Range(0.5f, 20f)] private float _moveSpeed = 6f;
    public float MoveSpeed { get { return _moveSpeed; } set { _moveSpeed = value; } }
    [SerializeField] private LayerMask _obstacleMask = ~0;     // TODO: 자기 레이어 제외 권장

    [Header("Facing")]
    [SerializeField, Range(1f, 30f)] private float _turnSpeed = 18f; // 회전 보간 속도
    [SerializeField, Range(0.0001f, 0.1f)] private float _faceDeadZone = 0.001f;

    [Header("Input")]
    public InputActionReference _moveAction;    // Vector2(WASD)
    public InputActionReference _dashAction;    // Button (Space/Shift 등)

    [Header("Debug")]
    [SerializeField] private bool _logDamage = false;

    // runtime
    private Vector2 _moveInput;         // WASD
    private Rigidbody _rb;
    private Health _health;
    private Dash _dash;                 // Dash 컴포넌트 연동
    private GameState _gs;
    private CapsuleCollider _capsule;

    // cached facing
    private Vector3 _lastMoveDir = Vector3.forward; // 입력 0일 때 유지할 마지막 방향(평면)

    private readonly Collider[] _overlapBuf = new Collider[16];

    private void Awake()
    {
        if (_gs == null) _gs = FindAnyObjectByType<GameState>();

        if (_rb == null) _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        if (_health == null) _health = GetComponent<Health>();
        if (_dash == null) _dash = GetComponent<Dash>();   // 동 위치 컴포넌트 권장
        if (_dash == null)
        {
            Debug.LogWarning("[PlayerController] Dash 컴포넌트가 없습니다. 대시 비활성.");
        }

        if (_gun == null) _gun = GetComponentInChildren<Gun>();
        if (_aim == null) _aim = GetComponent<CrosshairAim>();

        _capsule = GetComponent<CapsuleCollider>();
    }

    private void OnEnable()
    {
        // Input
        if (_moveAction != null) _moveAction.action.Enable();

        if (_dashAction != null)
        {
            _dashAction.action.Enable();
            _dashAction.action.performed += OnDashPerformed; // 버튼 눌림 시 대시 시도
        }

        if (_health != null)
        {
            _health.AddListenerOnDamagedEvent(OnDamaged);
            _health.AddListenerOnPlayerDeathEvent(OnPlayerDied);
        }
    }

    private void OnDisable()
    {
        // Input
        if (_moveAction != null) _moveAction.action.Disable();

        if (_dashAction != null)
        {
            _dashAction.action.performed -= OnDashPerformed;
            _dashAction.action.Disable();
        }

        if (_health != null)
        {
            _health.RemoveListenerOnDamagedEvent(OnDamaged);
            _health.RemoveListenerOnPlayerDeathEvent(OnPlayerDied);
        }
    }

    private void Update()
    {
        // 입력 읽기 전 상태 확인
        if (!IsPlayableNow())
        {
            _moveInput = Vector2.zero;
            return;
        }

        _moveInput = _moveAction != null ? _moveAction.action.ReadValue<Vector2>() : Vector2.zero;
    }

    private void FixedUpdate()
    {
        // PerkSelect/Paused/GameOver 동안 이동 차단
        if (!IsPlayableNow())
        {
            return;
        }

        // 이동 (대시 중에는 Dash가 이동 담당)
        if (_dash == null || !_dash.IsDashing)
        {
            Vector3 moveDir = CalcMoveDirCameraRelative(_moveInput);
            Move(moveDir);

            // 이동 방향 캐싱
            if (moveDir.sqrMagnitude > _faceDeadZone)
                _lastMoveDir = moveDir;
        }

        // 기본 회전: 이동 방향(대시 중이면 마지막 방향)
        FaceDirection(_lastMoveDir);
    }

    private bool IsPlayableNow()
    {
        return _gs != null && (_gs.IsPlayable() && !_gs.IsInputLocked());
    }

    /// <summary>
    /// 카메라 기준 입력(Vector2)을 월드 평면 이동 방향(Vector3)으로 변환
    /// </summary>
    private Vector3 CalcMoveDirCameraRelative(Vector2 input)
    {
        if (_aim == null || _aim.Camera == null) return Vector3.zero;

        Transform camTr = _aim.Camera.transform;

        Vector3 camForward = camTr.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Vector3 camRight = camTr.right;
        camRight.y = 0f;
        camRight.Normalize();

        Vector3 dir = camForward * input.y + camRight * input.x;
        dir.y = 0f;

        // 입력 크기가 크면 정규화(대각선 속도 보정)
        float sqr = dir.sqrMagnitude;
        if (sqr > 1f) dir /= Mathf.Sqrt(sqr);

        return dir;
    }

    /// <summary>
    /// 플레이어 이동 (평면 이동)
    /// </summary>
    private void Move(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude <= 0f) return;

        Vector3 start = _rb.position;
        Vector3 target = start + moveDir * _moveSpeed * Time.fixedDeltaTime;

        Vector3 safeTarget = ComputeWallSafeTarget(start, target);

        _rb.MovePosition(safeTarget);
    }

    private Vector3 ComputeWallSafeTarget(Vector3 start, Vector3 target)
    {
        Vector3 dir = (target - start);
        float dist = dir.magnitude;
        if (dist < 0.0001f) return start;

        dir /= dist;

        float radius = 0.5f;
        float height = 2f;
        float skin = 0.02f;

        if (_capsule)
        {
            radius = _capsule.radius;
            height = Mathf.Max(_capsule.height, radius * 2f);
        }

        float half = Mathf.Max(0f, height * 0.5f - radius);

        Vector3 center = start + Vector3.up * half;

        Vector3 p1 = center + Vector3.up * half;
        Vector3 p2 = center - Vector3.up * half;

        // 1) 시작 겹침 체크
        int hitCount = Physics.OverlapCapsuleNonAlloc(
            p1, p2, radius, _overlapBuf, ~_obstacleMask, QueryTriggerInteraction.Ignore);

        // 2) 겹치면 depenetration으로 밖으로 밀기
        if (hitCount > 0)
        {
            Vector3 totalPush = Vector3.zero;
            int pushHits = 0;

            for (int i = 0; i < hitCount; i++)
            {
                var other = _overlapBuf[i];
                if (!other) continue;
                if (_capsule && other == _capsule) continue; // 안전장치(보통 레이어로 해결)

                // 내 캡슐을 하나의 CapsuleCollider로 직접 넣기 어렵기 때문에,
                // ComputePenetration에는 실제 내 Collider를 넘기는 게 가장 정확함.
                // 가능하면 캐릭터에 붙은 _capsule을 그대로 사용.
                if (_capsule)
                {
                    if (Physics.ComputePenetration(
                        _capsule, _capsule.transform.position, _capsule.transform.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 pushDir, out float pushDist))
                    {
                        totalPush += pushDir * (pushDist + skin);
                        pushHits++;
                    }
                }
            }

            if (pushHits > 0)
            {
                // start를 밀어낸 만큼 보정(여기서는 단순 합, 필요하면 반복/클램프)
                start += totalPush;

                // 보정 후 p1/p2 재계산
                center = start + Vector3.up * half;
                p1 = center + Vector3.up * half;
                p2 = center - Vector3.up * half;
            }
            else
            {
                // 겹쳤는데 penetration 계산이 실패하면 "막힘"으로 간주하고 start 유지
                return start;
            }
        }

        RaycastHit hit;

        // 캡슐 스윕
        bool blocked = Physics.CapsuleCast(p1, p2,
                                           radius, dir, out hit, dist, ~_obstacleMask,
                                           QueryTriggerInteraction.Ignore);

        if (blocked)
        {
            float safe = Mathf.Max(0f, hit.distance - skin);
            return start + dir * safe;
        }

        return target;
    }

    private void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude <= _faceDeadZone) return;
        if (_gun.IsFirePressed) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        Quaternion next = Quaternion.Slerp(_rb.rotation, targetRot, 1f - Mathf.Exp(-_turnSpeed * Time.fixedDeltaTime));
        _rb.MoveRotation(next);
    }

    // Fire 직전: 캐릭터가 카메라 정면을 보게 변경
    public void FaceCameraForwardImmediate()
    {
        if (_aim == null) return;

        Vector3 camF = _aim.GetCameraForwardFlat();
        if (camF.sqrMagnitude <= 1e-4f) return;

        _lastMoveDir = camF;
        _rb.MoveRotation(Quaternion.LookRotation(camF, Vector3.up));

        // Debug.Log("FaceCameraForwardImmediate");
    }

    /// <summary>
    /// Dash 액션 입력 처리: 현재 입력/카메라 기준으로 대시 방향 계산 후 시도
    /// </summary>
    private void OnDashPerformed(InputAction.CallbackContext ctx)
    {
        if (_dash == null) return;
        if (_gs != null
            && (!_gs.IsPlayable() || _gs.IsInputLocked()))
        {
            return; // 게임 상태상 입력 차단
        }

        Vector3 dashDir = CalcMoveDirCameraRelative(_moveInput);
        if (dashDir.sqrMagnitude < 0.0001f)
        {
            // 입력이 없으면 마지막 방향(없으면 전방)
            dashDir = (_lastMoveDir.sqrMagnitude > 0.0001f) ? _lastMoveDir : transform.forward;
            dashDir.y = 0f;
        }

        // 대시 방향 캐싱 → 대시 시작 시 즉시 그 방향을 보게 함
        _lastMoveDir = dashDir.normalized;

        // 대시 시작 프레임에 즉시 방향 반영(시각적 지연 제거)
        FaceDirection(_lastMoveDir);

        _dash.TryDash(dashDir);
    }

    private void OnDamaged(DamageInfo info, int currentHP)
    {
        if (_logDamage)
        {
            Debug.Log(gameObject.name + " Damaged. "
                + "DamageAmount: " + info.amount
                + ", " + "DamageType: " + info.type
                + ", " + "Attacker: " + info.attacker
                + ", " + "Knockback Power: " + info.knockback
                + ", " + "CurrentHP: " + currentHP);
        }
    }

    private void OnPlayerDied(Health h)
    {
        if (h == _health)
        {
            OnPlayerDiedEvent.Raise();
        }
    }
}
