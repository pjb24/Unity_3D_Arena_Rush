/// <summary>
/// EnemyChaser (NavMesh 기반 추적형 최소 구현)
/// - 플레이어 자동 추적(SetDestination 주기 갱신)
/// - 근접 공격(쿨다운/사거리)
/// - LOS(시야차단) 옵션
/// - 풀링/재활용 대응(OnEnable 초기화)
/// - 탑다운 환경(updateUpAxis 옵션) 대응
/// </summary>

using UnityEngine;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class EnemyChaser : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform _target;     // 미지정 시 런타임에 Player 태그로 찾음
    [SerializeField] private string _playerTag = "Player";

    [Header("Agent Tuning")]
    [SerializeField] private bool _topDown = true;          // 탑다운(평면) 환경이면 true
    [SerializeField] private float _repathInterval = 0.1f;  // 목적지 재설정 간격(초)
    [SerializeField] private float _stoppingDistance = 1.2f;// 이동 정지 거리(에이전트)

    [Header("Attack (Melee)")]
    [SerializeField] private float _attackRange = 1.5f;     // 중심-대상 거리
    [SerializeField] private float _attackCooldown = 0.8f;
    [SerializeField] private int _damage = 10;
    [SerializeField] private E_DamageType _damageType = E_DamageType.Melee; // 프로젝트 공용 enum 사용

    [Header("Line of Sight (Optional)")]
    [SerializeField] private bool _useLOS = false;
    [SerializeField] private float _losHeight = 0.9f;
    [SerializeField] private LayerMask _losBlockMask;       // 벽/장애물 레이어

    [Header("Knockback")]
    [SerializeField] private bool _stunDuringKnockback = true;  // 넉백 중 이동/공격 금지
    [SerializeField] private float _controllerHeight = 1.8f;    // 충돌 가드용 캡슐 높이
    [SerializeField] private float _controllerRadius = 0.35f;   // 충돌 가드용 반경
    [SerializeField] private LayerMask _collisionMask;          // 벽/장애물 레이어
    [SerializeField]
    private AnimationCurve _knockbackCurve =
        AnimationCurve.EaseInOut(0, 1, 1, 0); // 0~1: 힘 감쇠

    [Header("Misc")]
    [SerializeField] private bool _drawGizmos = true;

    [Header("Rigidbody Move (Legacy)")]
    [SerializeField] private bool _rigidbodyMove;
    [SerializeField] private float _maxSpeed = 6f;          // m/s
    [SerializeField] private float _accel = 30f;            // m/s^2
    [SerializeField] private float _turnSpeed = 18f;        // yaw 보간 속도
    [SerializeField] private bool _faceMoveDir = true;      // 이동 방향으로 바라보기

    [Header("Physics (Legacy)")]
    [SerializeField] private float _gravity = -30f;         // 탑다운이라면 0으로 설정 가능
    [SerializeField] private bool _freezeXZRotation = true;

    // Cache
    private NavMeshAgent _agent;
    private Health _targetHealth;
    private Health _health;

    // timers (차감 방식)
    private float _attackTimer = 0f;
    private float _repathTimer = 0f;

    // runtime
    private bool _hasTarget;
    private bool _isKnockback = false;
    private float _stunUntil = 0f;
    private Coroutine _knockRoutine;

    // Legacy
    private Rigidbody _rb;

    #region Unity
    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();

        // 탑다운 3D(수평 평면) 환경 대응
        if (_topDown)
        {
            _agent.updateUpAxis = false; // 수직축 고정
            _agent.updateRotation = true; // 필요시 false 후 수동 회전도 가능
        }

        _agent.stoppingDistance = _stoppingDistance;
        _agent.autoBraking = true; // 목표 근접 시 속도 감속

        _health = GetComponent<Health>();

        if (_rigidbodyMove)
        {
            _rb = GetComponent<Rigidbody>();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.constraints = _freezeXZRotation
                ? RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ
                : RigidbodyConstraints.None;
        }
    }

    private void OnEnable()
    {
        // 타깃 캐싱
        if (_target == null)
        {
            var p = GameObject.FindGameObjectWithTag(_playerTag);
            if (p != null) _target = p.transform;
        }
        _targetHealth = _target ? _target.GetComponent<Health>() : null;
        _hasTarget = _target != null;

        // 타이머 초기화
        _attackTimer = 0f;
        _repathTimer = 0f;

        // Agent 초기화(풀링 복원 대비)
        _agent.isStopped = false;
        _agent.ResetPath();
        // 위치/회전은 풀링 매니저가 되돌린 상태로 가정

        if (_health != null)
        {
            _health.AddDamagedListener(OnDamaged);
        }

        if (_rigidbodyMove)
        {
            // 초기 속도 정리(풀링 대응)
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    private void OnDisable()
    {
        if (_health != null)
        {
            _health.RemoveDamagedListener(OnDamaged);
        }
    }

    private void Update()
    {
        if (!_hasTarget)
            return;

        // 스턴/넉백 상태
        bool stunned = (_stunDuringKnockback && (Time.time < _stunUntil)) || _isKnockback;

        // 타깃 유효성 체크(사망 시 추적 중단)
        if (_targetHealth != null && _targetHealth.IsDead)
        {
            StopChase();
            return;
        }

        // 경로 갱신(스파이크 방지용 간격)
        _repathTimer -= Time.deltaTime;
        if (!stunned && _repathTimer <= 0f)
        {
            _repathTimer = Mathf.Max(0.02f, _repathInterval);
            // SetDestination 비용 분산을 위해 간헐 갱신
            _agent.SetDestination(_target.position);
        }

        // 공격 처리
        _attackTimer -= Time.deltaTime;

        if (!stunned && _attackTimer <= 0f)
        {
            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist <= _attackRange)
            {
                if (!_useLOS || HasLineOfSight(transform.position, _target.position))
                {
                    TryDealDamage(); // 내부에서 _attackTimer 재설정
                }
            }
        }
    }

    private void FixedUpdate()
    {
        if (_rigidbodyMove)
        {
            if (!_hasTarget) return;

            // 타깃 유효성
            if (_targetHealth != null && _targetHealth.IsDead)
            {
                // 대상 사망 시 추적 해제(정지)
                BrakeToStop();
                return;
            }

            var pos = _rb.position;
            var tpos = _target.position;

            // 이동 계산
            Vector3 toTarget = tpos - pos;
            toTarget.y = 0f; // 탑다운 기준
            float dist = toTarget.magnitude;

            if (dist > 0.001f)
            {
                Vector3 dir = toTarget / dist;

                // 정지 거리 내에서는 속도 감쇠
                float targetSpeed = (dist > _stoppingDistance)
                    ? _maxSpeed
                    : Mathf.Lerp(0f, _maxSpeed, Mathf.InverseLerp(0f, _stoppingDistance, dist));
                Vector3 targetVel = dir * targetSpeed;

                // 가속도 제한(steering)
                Vector3 dv = targetVel - _rb.linearVelocity;
                float maxDelta = _accel * Time.fixedDeltaTime;
                if (dv.sqrMagnitude > maxDelta * maxDelta)
                {
                    dv = dv.normalized * maxDelta;
                }

                // 중력(원한다면 0으로)
                Vector3 gravity = Vector3.up * _gravity * Time.fixedDeltaTime;

                _rb.linearVelocity += dv + gravity;

                // 회전
                if (_faceMoveDir)
                {
                    Vector3 v = _rb.linearVelocity;
                    v.y = 0f;
                    if (v.sqrMagnitude > 0.0001f)
                    {
                        Quaternion look = Quaternion.LookRotation(v.normalized, Vector3.up);
                        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, 1f - Mathf.Exp(-_turnSpeed * Time.fixedDeltaTime)));
                    }
                }
            }
            else
            {
                BrakeToStop();
            }

            // 공격
            _attackTimer -= Time.fixedDeltaTime;
            if (_attackTimer <= 0f && dist <= _attackRange)
            {
                if (!_useLOS || HasLineOfSight(_rb.position, tpos))
                {
                    TryDealDamage();
                }
            }
        }
    }
    #endregion

    #region Public API
    /// <summary>외부에서 타깃 변경 주입</summary>
    public void SetTarget(Transform t)
    {
        _target = t;
        _targetHealth = _target ? _target.GetComponent<Health>() : null;
        _hasTarget = _target != null;
        if (_hasTarget)
        {
            _agent.isStopped = false;
            _agent.ResetPath();
            _agent.SetDestination(_target.position);
        }
    }

    /// <summary>추적 정지(게임 상태 전환/사망 등)</summary>
    public void StopChase()
    {
        _agent.isStopped = true;
        _agent.ResetPath();
    }

    /// <summary>
    /// 넉백 적용. sourcePos: 타격원(가해자) 위치, force: 1초 기준 이동거리(m/s 느낌), duration: 지속 시간(sec)
    /// </summary>
    public void ApplyKnockback(Vector3 sourcePos, float force, float duration)
    {
        if (duration <= 0f || force <= 0f) return;

        if (_knockRoutine != null) StopCoroutine(_knockRoutine);
        _knockRoutine = StartCoroutine(Co_Knockback(sourcePos, force, duration));
    }

    /// <summary>풀링 복귀 전/후 상태 초기화 용도</summary>
    public void ResetState()
    {
        _attackTimer = 0f;
        _repathTimer = 0f;
        _agent.isStopped = false;
        _agent.ResetPath();

        if (_rigidbodyMove)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }
    #endregion

    #region Internal
    // Legacy
    private void BrakeToStop()
    {
        // 간단 감속
        _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, Vector3.zero, 0.2f);
    }

    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 origin = from + Vector3.up * _losHeight;
        Vector3 dest = to + Vector3.up * _losHeight;
        Vector3 dir = (dest - origin);
        float len = dir.magnitude;
        if (len < 0.001f)
        {
            return true;
        }

        dir /= len;

        return !Physics.Raycast(origin, dir, len, _losBlockMask, QueryTriggerInteraction.Ignore);
    }

    private void TryDealDamage()
    {
        if (_targetHealth == null) return;

        // 프로젝트의 DamageInfo/E_DamageType 시그니처에 맞춰 생성
        var info = new DamageInfo(_damage, _damageType, gameObject);

        _targetHealth.TakeDamage(info);

        // 공격 시도 후 쿨다운 적용
        _attackTimer = _attackCooldown;
    }

    private IEnumerator Co_Knockback(Vector3 sourcePos, float force, float duration)
    {
        _isKnockback = true;

        // Agent 정지
        _agent.isStopped = true;

        float endTime = Time.time + duration;
        if (_stunDuringKnockback) _stunUntil = Mathf.Max(_stunUntil, endTime);

        Vector3 dir = (transform.position - sourcePos);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        dir.Normalize();

        Vector3 pos = transform.position;

        // 충돌 가드용 반높이
        float halfH = Mathf.Max(0.1f, _controllerHeight * 0.5f);

        while (Time.time < endTime)
        {
            float tNorm = 1f - ((endTime - Time.time) / duration); // 0~1
            float k = Mathf.Max(0f, _knockbackCurve.Evaluate(tNorm));   // 감쇠 계수
            float step = force * k * Time.deltaTime;    // 프레임 이동량

            if (step > 0f)
            {
                Vector3 delta = dir * step;
                Vector3 next = pos + delta;

                // 벽 충돌 가드(간단 캡슐 체크)
                Vector3 p1 = next + Vector3.up * (halfH * 0.5f);
                Vector3 p2 = next + Vector3.up * (_controllerHeight - halfH * 0.5f);
                bool blocked = Physics.CheckCapsule(p1, p2, _controllerRadius, _collisionMask, QueryTriggerInteraction.Ignore);

                if (!blocked)
                {
                    pos = next;
                }
                else
                {
                    // 측면으로 미끄러지기(간단): 평면 투영(슬라이드) 시도
                    Vector3 slide = Vector3.ProjectOnPlane(delta, GetHitNormal(pos, next));
                    next = pos + slide;
                    p1 = next + Vector3.up * (halfH * 0.5f);
                    p2 = next + Vector3.up * (_controllerHeight - halfH * 0.5f);
                    if (!Physics.CheckCapsule(p1, p2, _controllerRadius, _collisionMask, QueryTriggerInteraction.Ignore))
                        pos = next;
                    else
                        break; // 더 진행 불가
                }

                // NavMesh 위 좌표로 보정(오프메시 방지)
                // 오프메시 : NavMeshAgent가 NavMesh 영역 밖으로 벗어난 상태
                if (NavMesh.SamplePosition(pos, out var hit, 0.6f, NavMesh.AllAreas))
                    pos = hit.position;

                transform.position = pos;
            }

            yield return null;
        }

        // Agent 위치 동기화 및 재개
        _agent.Warp(transform.position);
        _agent.isStopped = false;

        _isKnockback = false;
        _knockRoutine = null;
    }

    // 전방 레이로 간략한 충돌면 노멀 추정
    private Vector3 GetHitNormal(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        dir.y = 0f;
        float dist = dir.magnitude;
        if (dist < 0.0001f)
        {
            return Vector3.up;
        }
        dir /= dist;

        if (Physics.Raycast(from + Vector3.up * (_controllerHeight * 0.5f), dir, out var hit, dist + 0.2f, _collisionMask, QueryTriggerInteraction.Ignore))
        {
            return hit.normal;
        }

        return Vector3.up;
    }

    private void OnDamaged(DamageInfo info, int currentHP)
    {
        Debug.Log(gameObject.name + " Damaged. "
            + "DamageAmount: " + info.amount
            + ", " + "DamageType: " + info.type
            + ", " + "Attacker: " + info.attacker
            + ", " + "Knockback Power: " + info.knockback
            + ", " + "CurrentHP: " + currentHP);

        ApplyKnockback(info.attacker.transform.position, info.knockback, 0.5f);
    }
    #endregion

    #region Gizmos
    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos) return;

        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, _attackRange);

        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, _stoppingDistance);

        if (_useLOS && _target != null)
        {
            Vector3 a = transform.position + Vector3.up * _losHeight;
            Vector3 b = _target.position + Vector3.up * _losHeight;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(a, b);
        }
    }
    #endregion
}
