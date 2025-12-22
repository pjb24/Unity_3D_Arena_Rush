// EnemyShooter.cs
// Arena Rush – 원거리형 적(AI + 사격; 조준 정렬각 조건 추가)
// - 가변 이동: NavMeshAgent 사용
// - 감지: _aggroRange 내, 시야(LOS) 확보 시 어그로
// - 이동: _desiredRange 유지(과접근 시 후퇴)
// - 조준: _maxAimYawPerSec로 회전, _fireAlignAngleDeg 이하로 정렬되면 발사
//
// - 교전 로직: 사거리 진입 → 정지/유지 → 시선 고정 → 사격(히트스캔 또는 발사체)
// - 사망/파괴 시 모든 코루틴 및 리스너 해제
// 의존: (선택) NavMeshAgent, (권장) Health.cs, (선택) Pooler.cs, (선택) Projectile.cs
//
// Arena Rush – Shooter (설정 요구사항 반영 통합)
// 요구사항 매핑
// 1) Spawn 시 Player 탐색: _playerTag 기반 자동 바인딩
// 2) 거리 > _aggroRange : Player를 향해 이동(추적)
// 3) 거리 <= _aggroRange : Player를 향해 사격(단, 너무 가까우면 후퇴 우선)
// 4) 사격 동작 시작 시 애니 끝날 때까지 이동 금지: _fireLockTime 동안 이동/후퇴/경로갱신 차단
// 5) 사격 애니 플레이 시간 제한: _fireLockTime(최대 허용 시간)
// 6) 거리 < _desiredRange : 뒷걸음질로 거리 유지(후퇴)
// 7) 회전은 NavMesh 미사용: transform.rotation을 코드로 직접 제어(Agent updateRotation=false)
//
// 의존(선택): NavMeshAgent, Pooler, Projectile, Animator(클립 길이 검증용)
// 의존(권장): Health.cs (AddListenerOnDeathEvent / RemoveListenerOnDeathEvent)

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class EnemyShooter : MonoBehaviour
{
    // ===== Types =====
    public enum E_FireType { Hitscan, Projectile }

    // ===== Serialized Fields =====
    [Header("Target")]
    [SerializeField] private Transform _target;                 // 미지정 시 런타임에 Player 태그로 자동 바인딩
    [SerializeField] private string _playerTag = "Player";

    [Header("AI – Range/Move")]
    [Tooltip("전투 상태로 들어갈 최대 감지 거리 - 범위 안에 들어오면 추적, 조준, 사격 루프 활성화")]
    [SerializeField, Range(0.1f, 100f)] private float _aggroRange = 20f;
    [Tooltip("유지하려고 하는 평균 교전 거리 - 너무 멀면 다가가고, 너무 가까우면 후퇴하는 기준 거리.")]
    [SerializeField, Range(1f, 50f)] private float _desiredRange = 12f;
    [SerializeField, Range(0f, 20f)] private float _moveSpeed = 5f;
    [Tooltip("1회 후퇴시 거리")]
    [SerializeField, Range(0f, 10f)] private float _retreatDist = 1.5f;
    [Tooltip("뒷걸음질 속도")]
    [SerializeField, Range(0.1f, 20f)] private float _retreatSpeed = 4.5f;

    [Header("Aim/LOS")]
    [SerializeField] private Transform _rayPoint;    // LOS 시작점
    [SerializeField] private Transform _muzzle;         // 발사 기준점
    [Tooltip("시야 차단 레이어(벽/장애물)")]
    [SerializeField] private LayerMask _obstacleMask;
    [Tooltip("1초 동안 회전할 수 있는 최대 Yaw(수평 회전) 각도. - 시선을 얼마나 빨리 플레이어에게 고정할 수 있는지 정의.")]
    [SerializeField, Range(0f, 1080f)] private float _maxAimYawPerSec = 720f;
    [Tooltip("정렬각 임계값")]
    [SerializeField, Range(0.5f, 30f)] private float _fireAlignAngleDeg = 6f;

    [Header("Fire")]
    [SerializeField] private E_FireType _fireType = E_FireType.Projectile;
    [SerializeField, Range(0.02f, 5f)] private float _fireCooldown = 0.6f;
    [Tooltip("교전 시작 후 최초 사격 지연")]
    [SerializeField, Range(0f, 10f)] private float _warmupDelay = 0f;
    [Tooltip("1번 공격 시 연속 사격할 탄수.")]
    [SerializeField, Range(1, 10)] private int _burstCount = 1;
    [SerializeField, Range(0f, 10f)] private float _burstInterval = 0.06f;
    [SerializeField, Range(0f, 10f)] private float _spreadDeg = 1.5f;
    [SerializeField, Range(1f, 200f)] private float _damage = 10f;

    [Header("Fire Lock (Movement Forbidden While Fire Animation Plays)")]
    [Tooltip("사격 동작 시작 시, 이 시간 동안 이동/후퇴/경로갱신 금지(애니 재생 시간 상한).")]
    [SerializeField, Range(0.05f, 3f)] private float _fireLockTime = 0.45f;

    [Header("Projectile (when Projectile mode)")]
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField, Range(1f, 200f)] private float _projectileSpeed = 40f;
    [Tooltip("발사체(Projectile)가 자동 파괴되기까지의 생존 시간.")]
    [SerializeField, Range(0.2f, 8f)] private float _projectileLife = 3f;

    [Header("Hitscan (when Hitscan mode)")]
    [SerializeField, Range(1f, 200f)] private float _hitscanRange = 40f;
    [SerializeField] private bool _drawHitscanLine = true;
    [SerializeField, Range(0.02f, 0.2f)] private float _hitscanLineTime = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool _log = false;

    // ===== Runtime =====
    private NavMeshAgent _agent;
    private Health _health;
    private Coroutine _aiLoop;
    private Pooler _pooler;

    private float _lastFireTime = -999f;
    private float _combatStartTime = -999f;

    private bool _isFireLocked;
    private float _fireLockUntil = -1f;
    public float FireLockTime => _fireLockTime;

    // 외부 공개 없이 구독 가능하도록 제공
    private event Action _onFired;
    public void AddFiredListener(Action listener) => _onFired += listener;
    public void RemoveFiredListener(Action listener) => _onFired -= listener;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<Health>();
        _pooler = FindAnyObjectByType<Pooler>();

        if (_health != null)
        {
            _health.AddListenerOnDeathEvent(OnOwnerDied);
        }
        if (_agent != null)
        {
            _agent.updateRotation = false; // 직접 회전 제어
            _agent.speed = _moveSpeed;
        }
    }

    private void OnEnable()
    {
        if (_target == null)
        {
            var player = GameObject.FindGameObjectWithTag(_playerTag);
            if (player != null) _target = player.transform;
        }

        _combatStartTime = -1f;
        _isFireLocked = false;
        _fireLockUntil = -1f;

        _aiLoop = StartCoroutine(AI_Loop());
    }

    private void OnDisable()
    {
        if (_aiLoop != null) StopCoroutine(_aiLoop);
        _aiLoop = null;
        if (_health != null) _health.RemoveListenerOnDeathEvent(OnOwnerDied);
    }

    private void OnDestroy()
    {
        if (_health != null) _health.RemoveListenerOnDeathEvent(OnOwnerDied);
    }

    private void OnOwnerDied(Health health)
    {
        // 코루틴/이동 정리
        if (_aiLoop != null) StopCoroutine(_aiLoop);
        _aiLoop = null;

        StopMove();
        _isFireLocked = true;
        _fireLockUntil = float.PositiveInfinity;
    }

    private IEnumerator AI_Loop()
    {
        var wait = new WaitForFixedUpdate();

        while (true)
        {
            yield return wait;

            if (_health != null && _health.IsDead) { StopMove(); continue; }

            if (_target == null) continue;

            // fire lock 처리
            if (_isFireLocked && Time.time >= _fireLockUntil)
                _isFireLocked = false;

            var toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            var dist = toTarget.magnitude;

            // 회전(항상 코드로 직접 제어)
            RotateTowards(toTarget);

            // ===== 이동/사격 분기 =====
            if (_isFireLocked)
            {
                // 사격 동작 중 이동 금지
                StopMove();
                continue;
            }

            // 1) 거리 > aggroRange : 추적 이동
            if (dist > _aggroRange)
            {
                _combatStartTime = -1f;
                MoveTowards(_target.position, _moveSpeed);
                continue;
            }

            // 2) 거리 <= aggroRange : 교전 시작
            if (_combatStartTime < 0f) _combatStartTime = Time.time;

            // 3) 너무 가까우면 후퇴(거리 유지)
            if (dist < _desiredRange)
            {
                Vector3 away = (transform.position - _target.position);
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = -transform.forward;

                Vector3 retreatGoal = transform.position + away.normalized * _retreatDist;
                MoveTowards(retreatGoal, _retreatSpeed);
                continue;
            }

            // 4) 사격 거리 안 & 유지거리 이상이면 정지 후 사격
            StopMove();

            if (!HasLineOfSight()) continue;
            if (!IsAimAligned()) continue;

            // 워밍업
            if (_warmupDelay > 0f && (Time.time - _combatStartTime) < _warmupDelay)
                continue;

            if (Time.time >= _lastFireTime + _fireCooldown)
            {
                _lastFireTime = Time.time;

                // 사격 시작 = 이동 금지 구간 진입(애니 재생 시간 상한)
                StartFireLock();

                yield return StartCoroutine(FireBurst_RequestAnim());
                // FireBurst가 끝나도 이동은 _fireLockTime이 끝날 때까지 금지
            }
        }
    }

    private void StartFireLock()
    {
        _isFireLocked = true;
        _fireLockUntil = Time.time + _fireLockTime;
        StopMove();
    }

    private void RotateTowards(Vector3 toTargetFlat)
    {
        if (toTargetFlat.sqrMagnitude <= 0.0001f) return;

        Quaternion desired = Quaternion.LookRotation(toTargetFlat.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, desired, _maxAimYawPerSec * Time.fixedDeltaTime);
    }

    private bool IsAimAligned()
    {
        // RayPoint 기준 방향과 타깃 방향 각도 비교
        Vector3 rayPos = _rayPoint ? _rayPoint.position : transform.position + Vector3.up * 1.0f;
        Vector3 forward = _rayPoint ? _rayPoint.forward : transform.forward;

        Vector3 dirToTarget = (_target.position) - rayPos;
        dirToTarget.y = 0;
        if (dirToTarget.sqrMagnitude < 0.0001f) return false;

        float angle = Vector3.Angle(forward, dirToTarget.normalized);
        return angle <= _fireAlignAngleDeg;
    }

    private void MoveTowards(Vector3 worldPos, float speed)
    {
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.speed = speed;
            _agent.SetDestination(worldPos);
        }
    }

    private void StopMove()
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
        }
    }

    private bool HasLineOfSight()
    {
        var rayPos = _rayPoint != null ? _rayPoint.position : transform.position + Vector3.up * 1.0f;
        var targetPos = _target.position + Vector3.up * 1.0f;
        var dir = (targetPos - rayPos);
        var dist = dir.magnitude;

        if (Physics.Raycast(rayPos, dir.normalized, out var hit, dist, _obstacleMask, QueryTriggerInteraction.Ignore))
        {
            // 장애물에 막힘
            return false;
        }
        return true;
    }

    // ===== "발사 요청"만 하고 실제 발사는 애니 이벤트에서 =====
    private IEnumerator FireBurst_RequestAnim()
    {
        for (int i = 0; i < _burstCount; i++)
        {
            if (!IsAimAligned()) yield break;

            // AnimDriver가 DoFire 트리거를 올리도록 이벤트 호출
            _onFired?.Invoke();
            if (_burstInterval > 0f && i < _burstCount - 1)
                yield return new WaitForSeconds(_burstInterval);
        }
    }

    // ===== Animation Event 수신 지점 =====
    // EnemyShooterAnimEventRelay.AE_FireShoot()가 호출한다.
    public void OnFireAnimEvent()
    {
        if (_health != null && _health.IsDead) return;

        FireOnce();
    }

    private void FireOnce()
    {
        var muzzlePos = _muzzle != null ? _muzzle.position : transform.position + Vector3.up * 1.0f;
        var fwd = (_target != null ? (_target.position - muzzlePos).normalized : transform.forward);

        // 간이 스프레드
        var spreadRot = Quaternion.Euler(UnityEngine.Random.Range(-_spreadDeg, _spreadDeg),
                                         UnityEngine.Random.Range(-_spreadDeg, _spreadDeg),
                                         0f);
        var shootDir = spreadRot * fwd;

        if (_fireType == E_FireType.Hitscan)
        {
            DoHitscan(muzzlePos, shootDir);
        }
        else
        {
            DoProjectile(muzzlePos, shootDir);
        }

        if (_log) Debug.Log($"[EnemyShooter] Fired ({_fireType}) at {_target?.name}", this);
    }

    private void DoHitscan(Vector3 origin, Vector3 dir)
    {
        var maxDist = _hitscanRange;
        var hasHit = Physics.Raycast(origin, dir, out var hit, maxDist, ~0, QueryTriggerInteraction.Ignore);

        if (_drawHitscanLine)
            StartCoroutine(DrawLineFrame(origin, hasHit ? hit.point : origin + dir * maxDist));

        if (!hasHit) return;

        // 데미지 처리
        var hp = hit.collider.GetComponent<Health>();
        if (hp != null)
        {
            hp.TakeDamage(new DamageInfo(Mathf.RoundToInt(_damage), E_DamageType.Generic, gameObject, hit.point, hit.normal));
        }
    }

    private IEnumerator DrawLineFrame(Vector3 a, Vector3 b)
    {
        var lr = GetComponent<LineRenderer>();
        if (lr == null)
        {
            lr = gameObject.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = lr.endWidth = 0.02f;
            lr.useWorldSpace = true;
            lr.material = new Material(Shader.Find("Sprites/Default"));
        }

        lr.enabled = true;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        yield return new WaitForSeconds(_hitscanLineTime);
        lr.enabled = false;
    }

    private void DoProjectile(Vector3 origin, Vector3 dir)
    {
        if (_projectilePrefab == null)
        {
            // 프리팹 없으면 히트스캔으로 대체
            DoHitscan(origin, dir);
            return;
        }

        GameObject go = null;

        // Pooler가 있다면 사용
        if (_pooler != null)
        {
            go = _pooler.Spawn(_projectilePrefab, origin, Quaternion.LookRotation(dir));
        }
        else
        {
            go = Instantiate(_projectilePrefab, origin, Quaternion.LookRotation(dir));
            Debug.Log("[EnemyShooter] Instantiate Projectile");
        }

        // Projectile 컴포넌트가 있으면 초기화, 없으면 Rigidbody로 대체
        if (go.TryGetComponent(out Projectile projectile))
        {
            projectile.Init(dir * _projectileSpeed, _damage, _projectileLife, gameObject);
        }
        else if (go.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = dir * _projectileSpeed;
            StartCoroutine(AutoDespawn(go, _projectileLife, _pooler));
            // 피격 처리는 탄 프리팹의 충돌 스크립트에 위임
        }
        else
        {
            // 어떤 구성도 없다면 즉시 파괴
            if (_pooler != null) _pooler.Despawn(go);
            else Destroy(go);
        }
    }

    private IEnumerator AutoDespawn(GameObject go, float life, Pooler pooler)
    {
        yield return new WaitForSeconds(life);
        if (go == null) yield break;
        if (pooler != null) pooler.Despawn(go);
        else Destroy(go);
    }

    // ===== Gizmos =====
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, _aggroRange);

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, _desiredRange);

        // 조준 정렬각(시각화)
        if (_muzzle)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
            Vector3 f = _muzzle.forward;
            Quaternion left = Quaternion.AngleAxis(+_fireAlignAngleDeg, Vector3.up);
            Quaternion right = Quaternion.AngleAxis(-_fireAlignAngleDeg, Vector3.up);
            Gizmos.DrawRay(_muzzle.position, (left * f) * 2f);
            Gizmos.DrawRay(_muzzle.position, (right * f) * 2f);
        }
    }
}
