// EnemyBruiserAnimDriver.cs
// Bruiser(근접 돌진형) Animator Driver
// Parameters:
//  Bool   : IsMoving, IsDead
//  Trigger: DoAttack, DoHit
//  Int    : HitIndex (1~3), DieIndex (1~3)
//
// Integration:
// - NavMeshAgent velocity/desireVelocity로 이동 판정
// - Health OnDamaged → Hit 랜덤 분기 + Trigger
// - Health OnDeath   → Die 랜덤 분기 + Dead 락
// - EnemyChaser 공격 성공 프레임에 NotifyAttack() 호출하여 Attack Trigger 1회 발화
//
// NOTE: 이벤트 구독은 외부 노출 없이 AddListener/RemoveListener 패턴을 사용한다는 전제(Health 구현에 맞춤)

using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class EnemyBruiserAnimDriver : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Animator _animator;
    [SerializeField] private NavMeshAgent _agent; // 없으면 이동 판정이 약해짐(가능하면 연결)

    [Header("Moving Detect")]
    [SerializeField, Range(0.001f, 1f)] private float _movingThreshold = 0.05f;
    [SerializeField] private bool _useDesiredVelocity = true;

    [Header("Random Index (1~3)")]
    [SerializeField] private bool _avoidSameIndexRepeat = true;
    [SerializeField, Range(1, 8)] private int _rerollMax = 4;

    [Header("Debug")]
    [SerializeField] private bool _log = false;

    // cached
    private Health _health;
    private bool _isDead;

    private int _lastHitIndex = 0;
    private int _lastDieIndex = 0;

    // Animator hashes
    private static readonly int _hashIsMoving = Animator.StringToHash("IsMoving");
    private static readonly int _hashIsDead = Animator.StringToHash("IsDead");
    private static readonly int _hashDoAttack = Animator.StringToHash("DoAttack");
    private static readonly int _hashDoHit = Animator.StringToHash("DoHit");
    private static readonly int _hashHitIndex = Animator.StringToHash("HitIndex");
    private static readonly int _hashDieIndex = Animator.StringToHash("DieIndex");

    private void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();

        _health = GetComponent<Health>();

        if (_animator == null)
            Debug.LogError("[EnemyBruiserAnimDriver] Animator missing.");
    }

    private void OnEnable()
    {
        _isDead = false;
        _lastHitIndex = 0;
        _lastDieIndex = 0;

        if (_animator != null)
        {
            _animator.SetBool(_hashIsDead, false);
            _animator.SetBool(_hashIsMoving, false);
            _animator.SetInteger(_hashHitIndex, 0);
            _animator.SetInteger(_hashDieIndex, 0);
        }

        if (_health != null)
        {
            _health.AddListenerOnDamagedEvent(OnDamaged);
            _health.AddListenerOnDeathEvent(OnDeath);
        }
    }

    private void OnDisable()
    {
        if (_health != null)
        {
            _health.RemoveListenerOnDamagedEvent(OnDamaged);
            _health.RemoveListenerOnDeathEvent(OnDeath);
        }
    }

    private void Update()
    {
        if (_animator == null) return;
        if (_isDead)
        {
            _animator.SetBool(_hashIsDead, true);
            _animator.SetBool(_hashIsMoving, false);
            return;
        }

        bool moving = CalcIsMoving();
        _animator.SetBool(_hashIsMoving, moving);
    }

    /// <summary>
    /// 공격 애니메이션 트리거 발화 지점.
    /// EnemyChaser가 실제로 데미지를 넣는 순간(쿨다운 통과 후) 호출해야 한다.
    /// </summary>
    public void NotifyAttack()
    {
        if (_animator == null) return;
        if (_isDead) return;

        _animator.SetTrigger(_hashDoAttack);

        if (_log) Debug.Log($"[{name}] Attack Trigger");
    }

    private bool CalcIsMoving()
    {
        if (_agent == null) return false;

        Vector3 v = _useDesiredVelocity ? _agent.desiredVelocity : _agent.velocity;
        v.y = 0f;
        return v.magnitude > _movingThreshold;
    }

    private void OnDamaged(DamageInfo info, int currentHP)
    {
        if (_animator == null) return;
        if (_isDead) return;

        // 사망 판정이 여기서 들어올 수도 있으니 선제 처리
        if (currentHP <= 0)
        {
            SetDead();
            return;
        }

        int hitIndex = RollIndex0To2(ref _lastHitIndex);
        _animator.SetInteger(_hashHitIndex, hitIndex);
        _animator.SetTrigger(_hashDoHit);

        if (_log) Debug.Log($"[{name}] Hit Trigger (index={hitIndex})");
    }

    private void OnDeath(Health health)
    {
        // Health.AddDeathListener(Action) 패턴 기준
        if (_animator == null) return;
        if (_isDead) return;

        SetDead();
    }

    private void SetDead()
    {
        _isDead = true;

        int dieIndex = RollIndex0To2(ref _lastDieIndex);
        _animator.SetInteger(_hashDieIndex, dieIndex);

        _animator.SetBool(_hashIsDead, true);

        if (_log) Debug.Log($"[{name}] Die Trigger (index={dieIndex})");
    }

    private int RollIndex0To2(ref int last)
    {
        // 0~2
        int v = Random.Range(0, 3);
        if (!_avoidSameIndexRepeat)
        {
            last = v;
            return v;
        }

        int tries = 0;
        while (v == last && tries < _rerollMax)
        {
            v = Random.Range(0, 3);
            tries++;
        }

        last = v;
        return v;
    }
}
