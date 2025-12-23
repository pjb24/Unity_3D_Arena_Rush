// EnemyBruiserAnimDriver.cs
// Bruiser(근접 돌진형) Animator Driver
// Parameters:
//  Bool   : IsMoving, IsDead
//  Trigger: DoAttack, DoHit
//  Int    : HitIndex (0~2)
//  Float  : AttackSpeed (Attack State Speed Multiplier)
//
// Integration:
// - NavMeshAgent velocity/desireVelocity로 이동 판정
// - Health OnDamaged → Hit 랜덤 분기 + Trigger
// - EnemyChaser 공격 성공 프레임에 NotifyAttack(attackCooldown) 호출
//   => AttackSpeed를 계산해 Attack 애니가 attackCooldown 안에 끝나도록 보정
//
// 이벤트:
// AnimEvent_AttackHitboxOn_All()
// AnimEvent_AttackHitboxOff_All()
// AnimEvent_AttackHitboxOn_Index(int index)
// AnimEvent_AttackHitboxOff_Index(int index)
//

///
/// 애니 이벤트 운영 방법(양손 공격 예시)
/// 
/// 방식 A) 두 손 동시에 유효 프레임이 겹침
/// 
/// AnimEvent_AttackHitboxOn_All
/// AnimEvent_AttackHitboxOff_All
/// AnimEvent_AttackEnd
/// 
/// 방식 B) 왼손 → 오른손 순차 타격(콤보 1클립)
/// 
/// 왼손 히트 구간 시작: AnimEvent_AttackHitboxOn_Index(0)
/// 왼손 히트 구간 종료: AnimEvent_AttackHitboxOff_Index(0)
/// 오른손 히트 구간 시작: AnimEvent_AttackHitboxOn_Index(1)
/// 오른손 히트 구간 종료: AnimEvent_AttackHitboxOff_Index(1)
/// 마지막: AnimEvent_AttackEnd
/// 
/// 인덱스(0/1)는 _meleeHitboxes 배열 순서다.
/// 프리팹에서 배열 순서를 왼손/오른손으로 고정해라.
/// 

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class EnemyBruiserAnimDriver : MonoBehaviour, IEnemyBruiserAnimEventListener
{
    [Header("Refs")]
    [SerializeField] private Animator _animator;
    [SerializeField] private NavMeshAgent _agent; // 없으면 이동 판정이 약해짐(가능하면 연결)

    [Header("Anim Event Bridge")]
    [SerializeField] private EnemyBruiserAnimEventBridge _animBridge;

    [Header("Hitboxes (Multiple)")]
    [Tooltip("양손/무기 등 여러 Hitbox를 넣는다. 각 Hitbox는 Trigger Collider여야 한다.")]
    [SerializeField] private EnemyMeleeHitbox[] _meleeHitboxes;
    [SerializeField] private string _targetTag = "Player";
    [Tooltip("여러 히트박스가 같은 타겟을 한 스윙에서 1번만 때리게 한다.")]
    [SerializeField] private bool _shareHitAcrossHitboxes = true;

    [Header("Moving Detect")]
    [SerializeField, Range(0.001f, 1f)] private float _movingThreshold = 0.05f;
    [SerializeField] private bool _useDesiredVelocity = true;

    [Header("Attack Timing (Fit clip into cooldown)")]
    [Tooltip("공격 애니메이션 클립(길이 측정용). 반드시 공격 State에서 실제로 쓰는 클립을 넣어라.")]
    [SerializeField] private AnimationClip _attackClip;
    [Tooltip("AttackSpeed 최대치(쿨다운이 너무 짧을 때 과가속 방지)")]
    [SerializeField, Range(1f, 6f)] private float _maxAttackSpeed = 3.0f;
    [Tooltip("쿨다운이 0에 가깝게 들어오는 상황 방지(분모 0 방지)")]
    [SerializeField, Range(0.01f, 1f)] private float _minCooldown = 0.05f;

    [Header("Random Index (0~2)")]
    [SerializeField] private bool _avoidSameIndexRepeat = true;
    [SerializeField, Range(1, 8)] private int _rerollMax = 4;

    [Header("Debug")]
    [SerializeField] private bool _log = false;

    // cached
    private Health _health;
    private EnemyChaser _chaser;
    private bool _isDead;

    private int _lastHitIndex = 0;

    private float _attackClipLength = 0.5f;

    // 이번 공격(스윙) 공유 중복 방지 집합
    private readonly HashSet<Health> _sharedHitSet = new HashSet<Health>();

    // Animator hashes
    private readonly int _hashIsMoving = Animator.StringToHash("IsMoving");
    private readonly int _hashIsDead = Animator.StringToHash("IsDead");
    private readonly int _hashDoAttack = Animator.StringToHash("DoAttack");
    private readonly int _hashDoHit = Animator.StringToHash("DoHit");
    private readonly int _hashHitIndex = Animator.StringToHash("HitIndex");
    private readonly int _hashAttackSpeed = Animator.StringToHash("AttackSpeed");

    private void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();
        if (_animBridge == null) _animBridge = GetComponentInChildren<EnemyBruiserAnimEventBridge>(true);

        _health = GetComponent<Health>();
        _chaser = GetComponent<EnemyChaser>();

        if (_meleeHitboxes == null || _meleeHitboxes.Length == 0)
            _meleeHitboxes = GetComponentsInChildren<EnemyMeleeHitbox>(true);

        if (_animator == null)
            Debug.LogError("[EnemyBruiserAnimDriver] Animator missing.");

        if (_attackClip != null)
            _attackClipLength = Mathf.Max(0.01f, _attackClip.length);
    }

    private void OnEnable()
    {
        _isDead = false;
        _lastHitIndex = 0;

        if (_animBridge != null)
            _animBridge.AddListener(this);

        if (_animator != null)
        {
            _animator.SetBool(_hashIsDead, false);
            _animator.SetBool(_hashIsMoving, false);
            _animator.SetInteger(_hashHitIndex, 0);

            // 기본 공격 속도는 1
            _animator.SetFloat(_hashAttackSpeed, 1f);
        }

        // 히트박스 초기화
        EndAllHitboxes();

        if (_chaser != null && _meleeHitboxes != null)
        {
            for (int i = 0; i < _meleeHitboxes.Length; i++)
            {
                if (_meleeHitboxes[i] == null) continue;
                _meleeHitboxes[i].Configure(_chaser.Damage, _chaser.DamageType, _targetTag);
            }
        }

        if (_health != null)
        {
            _health.AddListenerOnDamagedEvent(OnDamaged);
            _health.AddListenerOnDeathEvent(OnDeath);
        }
    }

    private void OnDisable()
    {
        if (_animBridge != null)
            _animBridge.RemoveListener(this);

        if (_health != null)
        {
            _health.RemoveListenerOnDamagedEvent(OnDamaged);
            _health.RemoveListenerOnDeathEvent(OnDeath);
        }

        EndAllHitboxes();
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
    /// attackCooldown 안에 Attack 애니가 끝나도록 AttackSpeed를 자동 보정한다.
    /// </summary>
    public void NotifyAttack(float attackCooldown)
    {
        if (_animator == null) return;
        if (_isDead) return;

        float cd = Mathf.Max(_minCooldown, attackCooldown);

        // "클립 길이 / 쿨다운" = 쿨다운 안에 끝내기 위한 배속
        float speed = _attackClipLength / cd;

        // 느리게 만들 필요는 없으니 1 미만은 1로 고정
        speed = Mathf.Clamp(speed, 1f, _maxAttackSpeed);

        _animator.SetFloat(_hashAttackSpeed, speed);

        // 트리거 중복 방지
        _animator.ResetTrigger(_hashDoAttack);
        _animator.SetTrigger(_hashDoAttack);

        if (_log) Debug.Log($"[{name}] Attack Trigger (cd={cd:0.###}, clip={_attackClipLength:0.###}, speed={speed:0.###})");
    }

    // ===== Animation Events =====
    // Attack 클립 이벤트로 아래를 호출한다.

    public void OnAttackHitboxOnAll()
    {
        if (_isDead) return;
        BeginAllHitboxes();
        if (_log) Debug.Log($"[{name}] Hitbox ON (ALL)");
    }

    public void OnAttackHitboxOffAll()
    {
        EndAllHitboxes();
        if (_log) Debug.Log($"[{name}] Hitbox OFF (ALL)");
    }

    public void OnAttackHitboxOnIndex(int index)
    {
        if (_isDead) return;
        BeginHitbox(index);
        if (_log) Debug.Log($"[{name}] Hitbox ON (idx={index})");
    }

    public void OnAttackHitboxOffIndex(int index)
    {
        EndHitbox(index);
        if (_log) Debug.Log($"[{name}] Hitbox OFF (idx={index})");
    }

    public void OnAttackEnd()
    {
        EndAllHitboxes();

        if (_chaser != null)
            _chaser.NotifyAttackAnimationEnded();

        if (_log) Debug.Log($"[{name}] Attack END");
    }
    // ============================

    private void BeginAllHitboxes()
    {
        if (_meleeHitboxes == null || _meleeHitboxes.Length == 0) return;

        _sharedHitSet.Clear();
        var attacker = _chaser != null ? _chaser.gameObject : gameObject;

        for (int i = 0; i < _meleeHitboxes.Length; i++)
        {
            if (_meleeHitboxes[i] == null) continue;
            _meleeHitboxes[i].BeginSwing(attacker, _shareHitAcrossHitboxes ? _sharedHitSet : null);
        }
    }

    private void EndAllHitboxes()
    {
        if (_meleeHitboxes == null) return;
        for (int i = 0; i < _meleeHitboxes.Length; i++)
        {
            if (_meleeHitboxes[i] == null) continue;
            _meleeHitboxes[i].EndSwing();
        }
        _sharedHitSet.Clear();
    }

    private void BeginHitbox(int index)
    {
        if (_meleeHitboxes == null) return;
        if (index < 0 || index >= _meleeHitboxes.Length) return;
        if (_meleeHitboxes[index] == null) return;

        // 첫 활성화 히트박스부터 공유집합 초기화
        if (_shareHitAcrossHitboxes && _sharedHitSet.Count == 0)
            _sharedHitSet.Clear();

        var attacker = _chaser != null ? _chaser.gameObject : gameObject;
        _meleeHitboxes[index].BeginSwing(attacker, _shareHitAcrossHitboxes ? _sharedHitSet : null);
    }

    private void EndHitbox(int index)
    {
        if (_meleeHitboxes == null) return;
        if (index < 0 || index >= _meleeHitboxes.Length) return;
        if (_meleeHitboxes[index] == null) return;

        _meleeHitboxes[index].EndSwing();
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

        _animator.SetBool(_hashIsDead, true);

        EndAllHitboxes();
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
