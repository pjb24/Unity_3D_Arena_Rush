// PlayerController.cs
// 입력 & 플레이어 이동(탑다운 3D)
// 의존: PlayerInput (Unity Input System), 액션 이름: "Move"(Vector2)

using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    [Header("Move")]
    [SerializeField, Range(0.5f, 20f)] private float _moveSpeed = 6f;

    // 카메라 Transform을 Inspector에서 연결
    [Header("Camera Reference")]
    [SerializeField] private Transform _cameraTransform;

    [Header("Input")]
    public InputActionReference _moveAction;    // Vector2(WASD)

    // runtime
    private Vector2 _moveInput;         // WASD
    private Rigidbody _rb;

    private Health _health;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        _health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        if (_moveAction != null) _moveAction.action.Enable();

        if (_health != null)
        {
            _health.AddDamagedListener(OnDamaged);
        }
    }

    private void OnDisable()
    {
        if (_moveAction != null) _moveAction.action.Disable();

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
        Move();
        FaceCameraDirection();
    }

    /// <summary>
    /// 플레이어 이동 (평면 이동)
    /// </summary>
    private void Move()
    {
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
        Vector3 viewDir = _cameraTransform.forward;
        viewDir.y = 0f;

        if (viewDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(viewDir);
            _rb.MoveRotation(targetRot);
        }
    }

    private void OnDamaged(DamageInfo info, int currentHP)
    {
        Debug.Log("Player Damaged. "
            + "DamageAmount: " + info.amount
            + ", " + "DamageType: " + info.type
            + ", " + "Attacker: " + info.attacker
            + ", " + "Knockback Power: " + info.knockback
            + ", " + "CurrentHP: " + currentHP);
    }
}
