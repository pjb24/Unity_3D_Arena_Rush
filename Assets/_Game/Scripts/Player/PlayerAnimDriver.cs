// PlayerAnimDriver.cs
// Arena Rush – Player Animator Driver (단일 레이어 Animator 전환 제어)
// - Parameters: IsMoving, IsAiming, IsDead, DoHit, DoDash
// - Health 피격/사망 이벤트 구독
// - Dash 시작을 감지하여 DoDash 1회 트리거
// - Fire 입력 기반 IsAiming 제어(래치 옵션)
// 의존: Unity Input System(선택), Health, Dash, GameState(선택)

using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerAnimDriver : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Animator _animator;
    [SerializeField] private Rigidbody _rb;                // 없으면 moveInput 기반으로만 판단

    [Header("Input (Optional)")]
    [SerializeField] private InputActionReference _moveAction; // Vector2
    [SerializeField] private InputActionReference _fireAction; // Button (hold)

    [Header("Tuning")]
    [SerializeField, Range(0.01f, 1f)] private float _movingSpeedThreshold = 0.10f;
    [SerializeField, Range(0f, 1f)] private float _aimLatchSeconds = 0.20f; // 최근 발사 유지 시간(0이면 hold만)
    [SerializeField] private bool _useVelocityForMoving = true;

    // runtime refs
    private Health _health;
    private Dash _dash;
    private GameState _gs;

    // cached state
    private bool _wasDashing;
    private bool _isDead;
    private float _aimLatchTimer;

    // Animator hashes (성능/오타 방지)
    private static readonly int _hashIsMoving = Animator.StringToHash("IsMoving");
    private static readonly int _hashIsAiming = Animator.StringToHash("IsAiming");
    private static readonly int _hashIsDead = Animator.StringToHash("IsDead");
    private static readonly int _hashDoHit = Animator.StringToHash("DoHit");
    private static readonly int _hashDoDash = Animator.StringToHash("DoDash");

    private void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_rb == null) _rb = GetComponent<Rigidbody>();

        _health = GetComponent<Health>();
        _dash = GetComponent<Dash>();
        _gs = FindAnyObjectByType<GameState>();

        if (_animator == null)
            Debug.LogError("[PlayerAnimDriver] Animator reference missing.");
    }

    private void OnEnable()
    {
        if (_moveAction != null) _moveAction.action.Enable();
        if (_fireAction != null) _fireAction.action.Enable();

        if (_health != null)
        {
            // Damage 이벤트: 트리거 발화
            _health.AddListenerOnDamagedEvent(OnDamaged);

            // Death 이벤트: bool/trigger 처리
            _health.AddListenerOnPlayerDeathEvent(OnPlayerDied);
        }

        _wasDashing = false;
        _isDead = false;
        _aimLatchTimer = 0f;

        // 초기값 반영
        if (_animator != null)
        {
            _animator.SetBool(_hashIsDead, false);
            _animator.SetBool(_hashIsMoving, false);
            _animator.SetBool(_hashIsAiming, false);
        }
    }

    private void OnDisable()
    {
        if (_moveAction != null) _moveAction.action.Disable();
        if (_fireAction != null) _fireAction.action.Disable();

        if (_health != null)
        {
            _health.RemoveListenerOnDamagedEvent(OnDamaged);
            _health.RemoveListenerOnPlayerDeathEvent(OnPlayerDied);
        }
    }

    private void Update()
    {
        if (_animator == null) return;
        if (_isDead)
        {
            bool temp = _animator.GetBool(_hashIsDead);
            if (temp != _isDead)
            {
                // 사망 후에는 locomotion 업데이트 불필요(종단 상태)
                _animator.SetBool(_hashIsDead, true);
            }
            return;
        }

        // GameState가 입력/플레이를 막으면: 이동/조준 false로 고정(애니 안정화)
        if (_gs != null && (_gs.IsInputLocked() || !_gs.IsPlayable()))
        {
            _animator.SetBool(_hashIsMoving, false);
            _animator.SetBool(_hashIsAiming, false);
            _aimLatchTimer = 0f;
            return;
        }

        bool isMoving = CalcIsMoving();
        bool isAiming = CalcIsAiming();

        _animator.SetBool(_hashIsMoving, isMoving);
        _animator.SetBool(_hashIsAiming, isAiming);

        // Dash 트리거: "대시 시작 프레임"에 1회만 쏨
        if (_dash != null)
        {
            bool isDashing = _dash.IsDashing;
            if (!_wasDashing && isDashing)
            {
                _animator.SetTrigger(_hashDoDash);
            }
            _wasDashing = isDashing;
        }
    }

    private bool CalcIsMoving()
    {
        // 1) Velocity 기반(권장: Rigidbody 이동이면 가장 정확)
        if (_useVelocityForMoving && _rb != null)
        {
            Vector3 v = _rb.linearVelocity;
            v.y = 0f;
            return v.magnitude > _movingSpeedThreshold;
        }

        // 2) 입력 기반(보조)
        if (_moveAction != null)
        {
            Vector2 mv = _moveAction.action.ReadValue<Vector2>();
            return mv.sqrMagnitude > 0.01f;
        }

        return false;
    }

    private bool CalcIsAiming()
    {
        bool fireHeld = false;

        if (_fireAction != null)
        {
            // Button action: 0/1 또는 눌림 유지
            fireHeld = _fireAction.action.IsPressed();
        }

        // 래치: 발사/조준 상태를 잠깐 유지하고 싶으면 사용
        if (_aimLatchSeconds > 0f)
        {
            if (fireHeld) _aimLatchTimer = _aimLatchSeconds;
            else _aimLatchTimer = Mathf.Max(0f, _aimLatchTimer - Time.deltaTime);

            return _aimLatchTimer > 0f;
        }

        return fireHeld;
    }

    private void OnDamaged(DamageInfo info, int currentHP)
    {
        if (_animator == null) return;
        if (_isDead) return;

        // 피격 트리거 1회
        _animator.SetTrigger(_hashDoHit);

        // HP가 0 이하로 떨어지는 케이스를 여기서도 방어
        if (currentHP <= 0)
        {
            SetDeadAndDieTrigger();
        }
    }

    private void OnPlayerDied(Health h)
    {
        if (h != _health) return;
        SetDeadAndDieTrigger();
    }

    private void SetDeadAndDieTrigger()
    {
        if (_animator == null) return;
        if (_isDead) return;

        _isDead = true;
        _animator.SetBool(_hashIsDead, true);
    }
}
