// EnemyShooterAnimDriver.cs
// Arena Rush – Shooter Animator Driver (Idle/Walk + Fire + Hit(3) + Die(3))
// - Parameters:
//   Bool   : IsMoving, IsDead
//   Trigger: DoFire, DoHit
//   Int    : HitIndex(0~2), DieIndex(0~2)
//   Float  : FirePlaySpeed  (Fire state의 Speed Multiplier에 연결)
// - Priority: Die > Hit > Fire > Locomotion
//
// 동작
// - EnemyShooter.FireLockTime 동안 Fire 애니가 끝나도록 FirePlaySpeed를 계산해 세팅 후 DoFire 트리거
//
// 의존:
// - Animator
// - EnemyShooter.cs (AddFiredListener/RemoveFiredListener 제공)
// - Health.cs (AddListenerOnDamagedEvent/RemoveListenerOnDamagedEvent, AddListenerOnDeathEvent/RemoveListenerOnDeathEvent)

using System;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class EnemyShooterAnimDriver : MonoBehaviour, IEnemyShooterAnimEventListener
{
    [Header("Refs")]
    [SerializeField] private Animator _animator;
    [SerializeField] private EnemyShooter _shooter;
    [SerializeField] private Health _health;
    [SerializeField] private NavMeshAgent _agent; // 있으면 velocity 기반 IsMoving 계산

    [Header("Anim Event Bridge")]
    [SerializeField] private EnemyShooterAnimEventBridge _animBridge;

    [Header("Moving 판단")]
    [SerializeField, Range(0f, 1f)] private float _moveSqrThreshold = 0.01f;

    [Header("Fire Speed Control")]
    [Tooltip("Animator Float 파라미터 이름. Fire state의 Speed Multiplier에 연결.")]
    [SerializeField] private string _firePlaySpeedParam = "FirePlaySpeed";
    [Tooltip("RuntimeAnimatorController 내 Fire 클립(길이 추출용).")]
    [SerializeField] private AnimationClip _fireClip;
    [Tooltip("계산된 배속의 하한/상한(비정상 방지)")]
    [SerializeField, Range(0.1f, 10f)] private float _fireSpeedMin = 0.25f;
    [SerializeField, Range(0.1f, 10f)] private float _fireSpeedMax = 4.0f;

    [Header("Random Variation")]
    [SerializeField] private bool _avoidSameHitTwice = true;
    [SerializeField] private bool _avoidSameDieTwice = true;

    // ===== Animator Hashes =====
    private readonly int _hashIsMoving = Animator.StringToHash("IsMoving");
    private readonly int _hashIsDead = Animator.StringToHash("IsDead");
    private readonly int _hashDoFire = Animator.StringToHash("DoFire");
    private readonly int _hashDoHit = Animator.StringToHash("DoHit");
    private readonly int _hashHitIndex = Animator.StringToHash("HitIndex");
    private readonly int _hashDieIndex = Animator.StringToHash("DieIndex");
    private int _hashFirePlaySpeed;

    // ===== Runtime =====
    private bool _isDead;
    private Vector3 _prevPos;
    private int _lastHitIndex = 0;
    private int _lastDieIndex = 0;

    private void Reset()
    {
        _animator = GetComponentInChildren<Animator>();
        _shooter = GetComponent<EnemyShooter>();
        _health = GetComponent<Health>();
        _agent = GetComponent<NavMeshAgent>();
        _animBridge = GetComponentInChildren<EnemyShooterAnimEventBridge>(true);
    }

    private void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_shooter == null) _shooter = GetComponent<EnemyShooter>();
        if (_health == null) _health = GetComponent<Health>();
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();
        if (_animBridge == null) _animBridge = GetComponentInChildren<EnemyShooterAnimEventBridge>(true);

        _hashFirePlaySpeed = Animator.StringToHash(_firePlaySpeedParam);

        _prevPos = transform.position;
    }

    private void OnEnable()
    {
        _prevPos = transform.position;
        _isDead = false;

        if (_animBridge != null)
        {
            _animBridge.AddListener(this);
        }

        // 초기 파라미터 정리
        if (_animator != null)
        {
            _animator.SetBool(_hashIsDead, false);
            _animator.SetBool(_hashIsMoving, false);
            _animator.ResetTrigger(_hashDoFire);
            _animator.ResetTrigger(_hashDoHit);
            _animator.SetFloat(_hashFirePlaySpeed, 1f);
        }

        if (_shooter != null)
        {
            _shooter.AddFiredListener(OnShooterFired);
        }

        if (_health != null)
        {
            // Health 구현에 맞춰 아래 시그니처가 존재해야 함.
            _health.AddListenerOnDamagedEvent(OnDamaged);
            _health.AddListenerOnDeathEvent(OnDied);
        }
    }

    private void OnDisable()
    {
        if (_animBridge != null)
        {
            _animBridge.RemoveListener(this);
        }

        if (_shooter != null)
        {
            _shooter.RemoveFiredListener(OnShooterFired);
        }

        if (_health != null)
        {
            _health.RemoveListenerOnDamagedEvent(OnDamaged);
            _health.RemoveListenerOnDeathEvent(OnDied);
        }
    }

    private void Update()
    {
        if (_animator == null) return;

        // Die가 최우선: 죽으면 움직임/사격 상태 갱신하지 않음
        if (_isDead)
        {
            _animator.SetBool(_hashIsMoving, false);
            return;
        }

        bool isMoving = ComputeIsMoving();
        _animator.SetBool(_hashIsMoving, isMoving);

        _prevPos = transform.position;
    }

    private bool ComputeIsMoving()
    {
        // 1) NavMeshAgent가 있으면 velocity 기준
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            return _agent.velocity.sqrMagnitude > _moveSqrThreshold;

        // 2) 없으면 위치 변화량 기준(steering/movePosition 모두 대응)
        Vector3 delta = transform.position - _prevPos;
        delta.y = 0f;
        return delta.sqrMagnitude > _moveSqrThreshold;
    }

    // ===== Animation Events =====
    // Attack 클립 이벤트로 아래를 호출한다.

    public void OnAE_Fire()
    {
        if (_isDead) return;
        _shooter.OnFireAnimEvent();
    }

    // ===== Shooter Event =====
    private void OnShooterFired()
    {
        if (_animator == null) return;
        if (_isDead) return;

        // FireLockTime 동안 Fire가 끝나도록 배속 계산
        float lockTime = (_shooter != null) ? _shooter.FireLockTime : 0f;
        float speed = ComputeFirePlaySpeed(lockTime);

        _animator.SetFloat(_hashFirePlaySpeed, speed);

        // Hit가 Fire보다 우선이므로: Hit 트리거가 최근 들어갔으면 Fire는 그냥 눌러도 Animator가 Hit를 먼저 처리함(조건/전이 우선순위가 맞다면).
        _animator.SetTrigger(_hashDoFire);
    }

    private float ComputeFirePlaySpeed(float fireLockTime)
    {
        // 기준:
        //  - Fire 클립 길이(_fireClip.length)를 fireLockTime에 맞추려면 speed = clipLen / lockTime
        //  - lockTime <= 0 이거나 clipLen 미확정이면 1.0 사용
        if (fireLockTime <= 0.0001f) return 1f;
        if (_fireClip.length <= 0.0001f) return 1f;

        float speed = _fireClip.length / fireLockTime;
        if (speed < _fireSpeedMin) speed = _fireSpeedMin;
        if (speed > _fireSpeedMax) speed = _fireSpeedMax;
        return speed;
    }

    // ===== Health Event =====
    private void OnDamaged(DamageInfo info, int remainHP)
    {
        if (_animator == null) return;
        if (_isDead) return;

        // HP가 0 이하로 떨어지는 프레임에서는 Die가 우선.
        if (remainHP <= 0) return;

        int hitIndex = PickIndex0To2(_avoidSameHitTwice ? _lastHitIndex : 0);
        _lastHitIndex = hitIndex;

        _animator.SetInteger(_hashHitIndex, hitIndex);
        _animator.SetTrigger(_hashDoHit);
    }

    private void OnDied(Health health)
    {
        if (_animator == null) return;
        if (_isDead) return;

        _isDead = true;

        int dieIndex = PickIndex0To2(_avoidSameDieTwice ? _lastDieIndex : 0);
        _lastDieIndex = dieIndex;

        // Die 최우선 고정
        _animator.ResetTrigger(_hashDoFire);
        _animator.ResetTrigger(_hashDoHit);

        _animator.SetInteger(_hashDieIndex, dieIndex);
        _animator.SetBool(_hashIsDead, true);
        _animator.SetBool(_hashIsMoving, false);
    }

    // ===== Util =====
    private static int PickIndex0To2(int exclude)
    {
        // 0,1,2 중 exclude 하나를 피해서 선택
        if (exclude < 0 || exclude > 2)
            return UnityEngine.Random.Range(0, 3);

        int r = UnityEngine.Random.Range(0, 2); // 0..1
        // exclude가 0이면 {1,2}, 1이면 {0,2}, 2이면 {0,1}
        if (exclude == 0) return r + 1;           // 1 or 2
        if (exclude == 1) return (r == 0) ? 0 : 2;
        return r;                                  // 0 or 1
    }
}
