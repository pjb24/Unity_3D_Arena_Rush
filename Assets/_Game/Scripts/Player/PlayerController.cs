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

    [Header("Move")]
    [SerializeField, Range(0.5f, 20f)] private float _moveSpeed = 6f;
    public float MoveSpeed { get { return _moveSpeed; } set { _moveSpeed = value; } }

    [Header("Facing")]
    [SerializeField] private bool _faceMoveDirection = true;
    [SerializeField, Range(1f, 30f)] private float _turnSpeed = 18f; // 회전 보간 속도
    [SerializeField, Range(0.0001f, 0.1f)] private float _faceDeadZone = 0.001f;

    [Header("Mouse Aim Facing")]
    [SerializeField] private Camera _mainCamera;
    [SerializeField] private LayerMask _aimGroundMask = ~0;     // Ground만 포함 권장
    [SerializeField] private float _aimRayMaxDistance = 300f;
    [SerializeField] private bool _useRaycastAim = true;        // true: Raycast / false: Plane

    // 카메라 Transform을 Inspector에서 연결
    [Header("Camera Reference")]
    [SerializeField] private Transform _cameraTransform;

    [Header("Input")]
    public InputActionReference _moveAction;    // Vector2(WASD)
    public InputActionReference _dashAction;    // Button (Space/Shift 등)
    public InputActionReference _aimAction;     // Button (Aim 또는 Fire에 연결)

    [Header("Debug")]
    [SerializeField] private bool _logDamage = false;

    // runtime
    private Vector2 _moveInput;         // WASD
    private Rigidbody _rb;

    private Health _health;
    private Dash _dash;                 // Dash 컴포넌트 연동

    private GameState _gs;

    // cached facing
    private Vector3 _lastMoveDir = Vector3.forward; // 입력 0일 때 유지할 마지막 방향(평면)

    private void Awake()
    {
        _gs = FindAnyObjectByType<GameState>();

        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        _health = GetComponent<Health>();
        _dash = GetComponent<Dash>();   // 동 위치 컴포넌트 권장
        if (_dash == null)
        {
            Debug.LogWarning("[PlayerController] Dash 컴포넌트가 없습니다. 대시 비활성.");
        }

        if (_mainCamera == null) _mainCamera = Camera.main;
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

        if (_aimAction != null) _aimAction.action.Enable();

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

        if (_aimAction != null) _aimAction.action.Disable();

        if (_health != null)
        {
            _health.RemoveListenerOnDamagedEvent(OnDamaged);
            _health.RemoveListenerOnPlayerDeathEvent(OnPlayerDied);
        }
    }

    private void Update()
    {
        // 입력 읽기 전 상태 확인
        if (_gs != null
            && (_gs.IsInputLocked() || !_gs.IsPlayable()))
        {
            _moveInput = Vector2.zero;
            return;
        }

        _moveInput = _moveAction != null ? _moveAction.action.ReadValue<Vector2>() : Vector2.zero;
    }

    private void FixedUpdate()
    {
        // PerkSelect/Paused/GameOver 동안 이동 차단
        if (_gs != null
            && (_gs.IsInputLocked() || !_gs.IsPlayable()))
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

        // ===== 회전 우선순위: Dash > Aim > Move =====
        if (_dash != null && _dash.IsDashing)
        {
            // Dash 방향 = _lastMoveDir (OnDashPerformed에서 dashDir로 갱신됨)
            FaceDirection(_lastMoveDir);
        }
        else if (IsAiming())
        {
            FaceMouseDirection();
        }
        else if (_faceMoveDirection)
        {
            FaceMoveDirection();
        }
    }

    private bool IsAiming()
    {
        if (_aimAction == null) return false;
        return _aimAction.action.IsPressed();
    }

    /// <summary>
    /// 카메라 기준 입력(Vector2)을 월드 평면 이동 방향(Vector3)으로 변환
    /// </summary>
    private Vector3 CalcMoveDirCameraRelative(Vector2 input)
    {
        if (_cameraTransform == null) return Vector3.zero;

        Vector3 camForward = _cameraTransform.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Vector3 camRight = _cameraTransform.right;
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

        Vector3 targetPos = _rb.position + moveDir * _moveSpeed * Time.fixedDeltaTime;
        _rb.MovePosition(targetPos);
    }

    /// <summary>
    /// 이동 방향(또는 대시 중이면 마지막 이동 방향)을 바라본다.
    /// </summary>
    private void FaceMoveDirection()
    {
        FaceDirection(_lastMoveDir);
    }

    private void FaceMouseDirection()
    {
        if (_mainCamera == null) return;

        if (!TryGetMouseWorldPoint(_rb.position.y, out Vector3 worldPoint))
            return;

        Vector3 dir = worldPoint - _rb.position;
        dir.y = 0f;
        if (dir.sqrMagnitude <= _faceDeadZone)
            return;

        FaceDirection(dir);
    }

    private void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude <= _faceDeadZone) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        Quaternion next = Quaternion.Slerp(_rb.rotation, targetRot, 1f - Mathf.Exp(-_turnSpeed * Time.fixedDeltaTime));
        _rb.MoveRotation(next);
    }

    private bool TryGetMouseWorldPoint(float planeY, out Vector3 worldPoint)
    {
        worldPoint = default;

        Vector2 mousePos = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : (Vector2)Input.mousePosition;

        Ray ray = _mainCamera.ScreenPointToRay(mousePos);

        if (_useRaycastAim)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, _aimRayMaxDistance, _aimGroundMask, QueryTriggerInteraction.Ignore))
            {
                worldPoint = hit.point;
                return true;
            }
            return false;
        }
        else
        {
            Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                worldPoint = ray.GetPoint(enter);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 플레이어는 항상 카메라가 바라보는 방향을 향한다.
    /// </summary>
    private void FaceCameraDirection()
    {
        if (_cameraTransform == null) return;

        Vector3 viewDir = _cameraTransform.forward;
        viewDir.y = 0f;

        if (viewDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(viewDir);
            _rb.MoveRotation(targetRot);
        }
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

        // 카메라 기준 벡터
        if (_cameraTransform == null) return;

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
