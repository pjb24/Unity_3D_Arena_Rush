/// <summary>
/// EnemyChaser (NavMesh 기반 추적형 최소 구현)
/// - 플레이어 자동 추적(SetDestination 주기 갱신)
/// - 근접 공격(쿨다운/사거리)
/// - (공격: 이동 정지 → 애니 재생 → 이벤트로 히트박스/종료)
/// - LOS(시야차단) 옵션
/// - 풀링/재활용 대응(OnEnable 초기화)
/// - 탑다운 환경(updateUpAxis 옵션) 대응
/// </summary>

using NUnit.Framework.Interfaces;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
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

    [Header("Rotation")]
    [SerializeField] private float _rotationSpeed = 720f;   // 이동 중 회전 속도(도/초)
    [SerializeField] private bool _instantRotateOnAttack = true; // 공격 시 즉시 스냅 회전

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

    [Header("Animation")]
    [SerializeField] private EnemyBruiserAnimDriver _enemyBruiserAnimDriver;

    // ===== ScriptableObject References =====
    [Header("GameState Integration")]
    [SerializeField] private bool _respectGameState = true;                 // Playing에서만 동작
    [SerializeField] private bool _stopAgentWhenNotPlayable = true;         // 비플레이 상태에서 Agent 정지
    [SerializeField] private bool _resetPathWhenNotPlayable = true;         // Agent 경로 리셋
    [SerializeField] private bool _useUnscaledTimeForKnockback = false;     // 일시정지 중에도 넉백 진행

    [Header("Misc")]
    [SerializeField] private bool _drawGizmos = true;

    [Header("Debug")]
    [SerializeField] private bool _logDamage = false;

    // 외부 드라이버가 필요로 하는 값(Driver에서 Configure에 사용)
    public int Damage => _damage;
    public E_DamageType DamageType => _damageType;

    // Cache
    private NavMeshAgent _agent;
    private Health _targetHealth;

    // timers (차감 방식)
    private float _attackTimer = 0f;
    private float _repathTimer = 0f;

    // runtime
    private bool _hasTarget;
    private bool _isAttacking;
    private bool _isKnockback = false;
    private float _stunUntil = 0f;
    private Coroutine _knockRoutine;

    private GameState _gs;
    private Health _health;

    // 시간 소스(넉백에서만 사용)
    private float NowKB => _useUnscaledTimeForKnockback ? Time.unscaledTime : Time.time;
    private float DeltaKB => _useUnscaledTimeForKnockback ? Time.unscaledDeltaTime : Time.deltaTime;

    #region Unity
    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();

        // NavMeshAgent의 회전은 쓰지 않고, 항상 수동 회전
        _agent.updateRotation = false;

        // 탑다운 3D(수평 평면) 환경 대응
        if (_topDown)
        {
            _agent.updateUpAxis = false; // 수직축 고정
        }

        _agent.stoppingDistance = _stoppingDistance;
        _agent.autoBraking = true; // 목표 근접 시 속도 감속

        _gs = FindAnyObjectByType<GameState>();
        _health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        // GameState 구독
        if (_gs != null)
        {
            _gs.AddListenerStateChanged(OnGameStateChanged);
        }

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
        _isAttacking = false;

        // Agent 초기화(풀링 복원 대비)
        ResetState();
        // 위치/회전은 풀링 매니저가 되돌린 상태로 가정

        if (_health != null)
        {
            _health.AddListenerOnDamagedEvent(OnDamaged);
        }

        // 현재 상태 정책 즉시 반영
        ApplyPlayablePolicy(_respectGameState ? CanOperateByGameState() : true);
    }

    private void OnDisable()
    {
        if (_health != null)
        {
            _health.RemoveListenerOnDamagedEvent(OnDamaged);
        }

        if (_gs != null)
        {
            _gs.RemoveListenerStateChanged(OnGameStateChanged);
        }
    }

    private void Update()
    {
        if (!_hasTarget)
            return;

        // 상태 체크(Playing 아닐 때 추적/공격/타이머 금지)
        if (_respectGameState && !CanOperateByGameState())
            return;

        // 스턴/넉백 상태
        bool stunned = (_stunDuringKnockback && (Time.time < _stunUntil)) || _isKnockback;

        // 타깃 유효성 체크(사망 시 추적 중단)
        if (_targetHealth != null && _targetHealth.IsDead)
        {
            StopChase();
            return;
        }

        // 공격 쿨다운 감소 처리
        _attackTimer -= Time.deltaTime;

        // 공격 중이면: 이동 정지 유지(애니 이벤트로 종료됨)
        if (_isAttacking)
        {
            // 공격 애니메이션 동안 측면을 보는 문제 방지
            UpdateFacing(_instantRotateOnAttack);
            StopChase();
            return;
        }

        // 공격 거리 이내면: 이동 정지 + 공격 트리거
        if (!stunned && _attackTimer <= 0f)
        {
            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist <= _attackRange)
            {
                // 공격 들어가기 직전, 강제로 플레이어 쪽 보게 스냅 or 빠른 회전
                UpdateFacing(_instantRotateOnAttack);

                if (!_useLOS || HasLineOfSight(transform.position, _target.position))
                {
                    StopChase();
                    StartAttack();
                }

                return;
            }
        }

        // 이동/대기 상태에서도 항상 플레이어를 향하도록 회전
        // (공격 범위 밖 추적 중 포함)
        //if (!_isKnockback) // 넉백 중에는 회전 고정하고 싶으면 이 조건 유지
        {
            UpdateFacing(false);
        }

        // 경로 갱신(스파이크 방지용 간격)
        // 공격 거리 밖: 추적
        _repathTimer -= Time.deltaTime;
        if (!stunned && _repathTimer <= 0f)
        {
            _repathTimer = Mathf.Max(0.02f, _repathInterval);
            // SetDestination 비용 분산을 위해 간헐 갱신
            if (_agent.enabled)
            {
                _agent.SetDestination(_target.position);
            }
        }
    }

    private void StartAttack()
    {
        _isAttacking = true;

        // “다음 공격까지 최소 간격”은 여기서 바로 걸어둔다.
        _attackTimer = _attackCooldown;

        // 애니 재생 + 쿨다운 안에 끝나도록 속도 보정
        if (_enemyBruiserAnimDriver != null)
            _enemyBruiserAnimDriver.NotifyAttack(_attackCooldown);
    }
    #endregion

    /// <summary>
    /// Attack 애니 끝 프레임(AnimEvent_AttackEnd)에서 호출된다.
    /// </summary>
    public void NotifyAttackAnimationEnded()
    {
        _isAttacking = false;

        if (!_hasTarget || !_agent.enabled) return;

        // 공격 종료 시점에도 다시 한번 플레이어를 향해 정렬
        if (_instantRotateOnAttack)
        {
            UpdateFacing(true);
        }

        float dist = Vector3.Distance(transform.position, _target.position);

        // 아직 공격 거리면 계속 정지(다음 쿨다운까지 대기)
        if (dist <= _attackRange)
        {
            StopChase();
            return;
        }

        // 벗어났으면 추적 재개
        _agent.isStopped = false;
        _agent.SetDestination(_target.position);
    }

    #region Public API
    /// <summary>추적 정지(게임 상태 전환/사망 등)</summary>
    public void StopChase()
    {
        if (_agent == null || !_agent.enabled) return;
        _agent.isStopped = true;
        _agent.ResetPath();
    }

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

            // 타깃 바뀌는 즉시 방향 정렬
            UpdateFacing(true);
        }
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
        _isAttacking = false;
        _isKnockback = false;
    }
    #endregion

    #region Internal Rotation
    /// <summary>
    /// 플레이어를 바라보도록 회전.
    /// forceInstant = true면 즉시 스냅, false면 _rotationSpeed로 스무스 회전.
    /// </summary>
    private void UpdateFacing(bool forceInstant)
    {
        if (!_hasTarget)
            return;

        Vector3 dir = _target.position - transform.position;

        if (_topDown)
        {
            // 탑다운 환경에서는 Y를 고정하고 수평면에서만 회전
            dir.y = 0f;
        }

        if (dir.sqrMagnitude < 0.0001f)
            return;

        dir.Normalize();

        Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);

        if (forceInstant || _rotationSpeed <= 0f)
        {
            transform.rotation = targetRot;
        }
        else
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                _rotationSpeed * Time.deltaTime);
        }
    }
    #endregion

    #region Internal
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

    private IEnumerator Co_Knockback(Vector3 sourcePos, float force, float duration)
    {
        _isKnockback = true;

        // Agent 정지
        _agent.isStopped = true;

        if (_stunDuringKnockback) _stunUntil = Mathf.Max(_stunUntil, Time.time + duration);

        Vector3 dir = (transform.position - sourcePos);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        dir.Normalize();

        Vector3 pos = transform.position;

        // 충돌 가드용 반높이
        float halfH = Mathf.Max(0.1f, _controllerHeight * 0.5f);

        // 진행도(0~duration), 스냅샷 타임
        float elapsed = 0f;
        float lastT = NowKB;

        while (elapsed < duration)
        {
            // 비플레이(PerkSelect/Paused/GameOver 등)면 그 위치에서 정지 대기
            if (_respectGameState && !CanOperateByGameState())
            {
                // 경로는 멈춘 상태 유지, 타임스탬프 재동기화
                yield return new WaitUntil(CanOperateByGameState);
                lastT = NowKB;           // 재개 시점으로 기준 재설정(시간 누수 방지)
                continue;               // 이동/시간 적산 없이 다음 루프
            }

            // 진행 시간 적산(스케일드 또는 언스케일드)
            float now = NowKB;
            float dt = Mathf.Max(0f, now - lastT);
            lastT = now;

            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            elapsed = Mathf.Min(duration, elapsed + dt);
            float tNorm = Mathf.Clamp01(elapsed / duration);       // 0~1
            float k = Mathf.Max(0f, _knockbackCurve.Evaluate(tNorm));
            float step = force * k * dt;

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
                        elapsed = duration; // 더 진행 불가 → 즉시 종료
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

        // 재개 시점이 비플레이일 수 있으므로 정책 적용
        if (_respectGameState && !CanOperateByGameState())
        {
            // 비플레이면 멈춘 상태 유지(상태 변경 콜백에서 재개)
            _agent.isStopped = true;
        }
        else
        {
            _agent.isStopped = false;
            if (_hasTarget) _agent.SetDestination(_target.position);
        }

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
        if (_logDamage)
        {
            Debug.Log(gameObject.name + " Damaged. "
                + "DamageAmount: " + info.amount
                + ", " + "DamageType: " + info.type
                + ", " + "Attacker: " + info.attacker
                + ", " + "Knockback Power: " + info.knockback
                + ", " + "CurrentHP: " + currentHP);
        }

        Vector3 sourcePos = info.attacker != null ? info.attacker.transform.position : transform.position - transform.forward;
        ApplyKnockback(sourcePos, info.knockback, 0.5f);
    }
    #endregion

    #region GameState Hooks
    private bool CanOperateByGameState()
    {
        if (!_respectGameState) return true;

        if (_gs == null) return true;

        // 입력 잠금 시에도 AI 정지
        bool result = false;
        result = _gs.IsPlayable()
            && !_gs.IsInputLocked();

        return result;
    }

    private void OnGameStateChanged(GameStateSO.E_GamePlayState prev, GameStateSO.E_GamePlayState cur)
    {
        bool playable = CanOperateByGameState();
        ApplyPlayablePolicy(playable);
    }

    private void ApplyPlayablePolicy(bool playable)
    {
        if (!_stopAgentWhenNotPlayable || _agent == null || !_agent.enabled) return;

        if (!playable)
        {
            _agent.isStopped = true;
            if (_resetPathWhenNotPlayable)
                _agent.ResetPath();
        }
        else
        {
            _agent.isStopped = false;
            if (_hasTarget)
                _agent.SetDestination(_target.position);
        }
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
