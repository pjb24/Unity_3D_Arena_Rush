// PlayerController.cs
// 입력 & 플레이어 이동(탑다운 3D) + Dash 연동
// 의존: PlayerInput (Unity Input System)
// 액션 이름: "Move"(Vector2), "Dash"(Button)

using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    [Header("Move")]
    [SerializeField, Range(0.5f, 20f)] private float _moveSpeed = 6f;
    public float MoveSpeed { get { return _moveSpeed; } set { _moveSpeed = value; } }

    // 카메라 Transform을 Inspector에서 연결
    [Header("Camera Reference")]
    [SerializeField] private Transform _cameraTransform;

    [Header("Input")]
    public InputActionReference _moveAction;    // Vector2(WASD)
    public InputActionReference _dashAction; // Button (Space/Shift 등)

    [Header("Debug")]
    [SerializeField] private bool _logDamage = false;

    // runtime
    private Vector2 _moveInput;         // WASD
    private Rigidbody _rb;

    private Health _health;
    private Dash _dash;                 // Dash 컴포넌트 연동

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        _health = GetComponent<Health>();
        _dash = GetComponent<Dash>();   // 동 위치 컴포넌트 권장
        if (_dash == null)
        {
            Debug.LogWarning("[PlayerController] Dash 컴포넌트가 없습니다. 대시 비활성.");
        }
    }

    private void OnEnable()
    {
        if (_moveAction != null) _moveAction.action.Enable();

        if (_dashAction != null)
        {
            _dashAction.action.Enable();
            _dashAction.action.performed += OnDashPerformed; // 버튼 눌림 시 대시 시도
        }

        if (_health != null)
        {
            _health.AddDamagedListener(OnDamaged);
        }
    }

    private void OnDisable()
    {
        if (_moveAction != null) _moveAction.action.Disable();

        if (_dashAction != null)
        {
            _dashAction.action.performed -= OnDashPerformed;
            _dashAction.action.Disable();
        }

        if (_health != null)
        {
            _health.RemoveDamagedListener(OnDamaged);
        }
    }

    private void Update()
    {
        _moveInput = _moveAction != null ? _moveAction.action.ReadValue<Vector2>() : Vector2.zero;
    }

    private void FixedUpdate()
    {
        // 대시 중에는 수동 이동 정지(대시 스크립트가 위치 이동을 담당)
        if (_dash == null || !_dash.IsDashing)
        {
            Move();
        }

        FaceCameraDirection();
    }

    /// <summary>
    /// 플레이어 이동 (평면 이동)
    /// </summary>
    private void Move()
    {
        if (_cameraTransform == null) return;

        // 카메라 기준의 앞/옆 방향을 평면 벡터로 변환
        Vector3 camForward = _cameraTransform.forward;
        camForward.y = 0;
        camForward.Normalize();

        Vector3 camRight = _cameraTransform.right;
        camRight.y = 0;
        camRight.Normalize();

        // 입력을 카메라 기준 벡터에 매핑
        Vector3 moveDir = camForward * _moveInput.y + camRight * _moveInput.x;

        Vector3 targetPos = _rb.position + moveDir * _moveSpeed * Time.fixedDeltaTime;
        _rb.MovePosition(targetPos);
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

        // 카메라 기준 벡터
        if (_cameraTransform == null) return;

        Vector3 camForward = _cameraTransform.forward;
        camForward.y = 0;
        camForward.Normalize();

        Vector3 camRight = _cameraTransform.right;
        camRight.y = 0;
        camRight.Normalize();

        // 이동 입력이 있으면 그 방향, 없으면 카메라 전방
        Vector3 dashDir = (_moveInput.sqrMagnitude > 0.0001f)
            ? (camForward * _moveInput.y + camRight * _moveInput.x)
            : camForward;

        if (dashDir.sqrMagnitude < 0.0001f)
        {
            dashDir = transform.forward;
        }

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
}
