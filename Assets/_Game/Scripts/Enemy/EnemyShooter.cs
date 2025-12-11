// EnemyShooter.cs
// Arena Rush – 원거리형 적(AI + 사격; 조준 정렬각 조건 추가)
// - 가변 이동: NavMeshAgent 사용(있으면) / 없으면 단순 Steering 이동
// - 감지: _aggroRange 내, 시야(LOS) 확보 시 어그로
// - 이동: _desiredRange 유지(과접근 시 후퇴)
// - 조준: _maxAimYawPerSec로 회전, _fireAlignAngleDeg 이하로 정렬되면 발사
//
// - 교전 로직: 사거리 진입 → 정지/유지 → 시선 고정 → 사격(히트스캔 또는 발사체)
// - 사망/파괴 시 모든 코루틴 및 리스너 해제
// 의존: (선택) NavMeshAgent, (권장) Health.cs, (선택) Pooler.cs, (선택) Projectile.cs

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
    [Tooltip("desiredRange 주변에서 +- 몇 m까지 허용하는지 나타내는 범위 - 미세한 이동 떨림(Jitter)을 줄이기 위한 완충값.")]
    [SerializeField, Range(0f, 10f)] private float _keepDistanceTolerance = 2f;
    [SerializeField, Range(0f, 20f)] private float _moveSpeed = 5f;
    [Tooltip("1회 후퇴시 거리")]
    [SerializeField, Range(0f, 10f)] private float _retreatDist = 1.5f;
    [SerializeField] private bool _useNavMeshIfAvailable = true;

    [Header("Aim/LOS")]
    [SerializeField] private Transform _muzzle;                 // 총구(없으면 transform 기준)
    [Tooltip("시야 차단 레이어(벽/장애물)")]
    [SerializeField] private LayerMask _obstacleMask;
    [Tooltip("1초 동안 회전할 수 있는 최대 Yaw(수평 회전) 각도. - 시선을 얼마나 빨리 플레이어에게 고정할 수 있는지 정의.")]
    [SerializeField, Range(0f, 1080f)] private float _maxAimYawPerSec = 720f;
    [Tooltip("정렬각 임계값")]
    [SerializeField, Range(0.5f, 30f)] private float _fireAlignAngleDeg = 6f;

    [Header("Fire")]
    [SerializeField] private E_FireType _fireType = E_FireType.Hitscan;
    [SerializeField, Range(0.02f, 5f)] private float _fireCooldown = 0.6f;
    [Tooltip("교전 시작 후 최초 사격 지연")]
    [SerializeField, Range(0f, 10f)] private float _warmupDelay = 0f;
    [Tooltip("1번 공격 시 연속 사격할 탄수.")]
    [SerializeField, Range(1, 10)] private int _burstCount = 1;
    [SerializeField, Range(0f, 10f)] private float _burstInterval = 0.06f;
    [SerializeField, Range(0f, 10f)] private float _spreadDeg = 1.5f;
    [SerializeField, Range(1f, 200f)] private float _damage = 10f;

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
    private float _lastFireTime = -999f;
    private float _enterCombatTime = -999f;

    private Pooler _pooler;

    // 외부 공개 없이 구독 가능하도록 제공
    private event Action _onFired;
    public void AddFiredListener(Action listener) => _onFired += listener;
    public void RemoveFiredListener(Action listener) => _onFired -= listener;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<Health>();
        if (_health != null)
        {
            _health.AddListenerOnDeathEvent(OnOwnerDied);
        }
        if (_agent != null)
        {
            _agent.updateRotation = false; // 직접 회전 제어
            _agent.speed = _moveSpeed;
        }

        _pooler = FindAnyObjectByType<Pooler>();
    }

    private void OnEnable()
    {
        if (_target == null)
        {
            var player = GameObject.FindGameObjectWithTag(_playerTag);
            if (player != null) _target = player.transform;
        }

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

        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.ResetPath();
            _agent.isStopped = true;
        }

        // 본 오브젝트는 Health에서 처리(Disable/Pool Return)한다고 가정
    }

    private IEnumerator AI_Loop()
    {
        var wait = new WaitForFixedUpdate();

        while (true)
        {
            yield return wait;

            if (_target == null) continue;

            var toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            var dist = toTarget.magnitude;

            // 교전 범위 체크
            bool inAggro = dist <= _aggroRange;

            if (inAggro && _enterCombatTime < 0f) _enterCombatTime = Time.time;
            if (!inAggro) _enterCombatTime = -1f;

            // 회전(조준) – 수평면에서 목표 바라보기
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                var desiredRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, desiredRot, _maxAimYawPerSec * Time.fixedDeltaTime);
            }

            // 이동 – NavMesh 또는 단순 Steering
            if (inAggro)
            {
                float lower = _desiredRange - _keepDistanceTolerance;
                float upper = _desiredRange + _keepDistanceTolerance;

                if (dist > upper)
                {
                    MoveTowards(_target.position);
                }
                else if (dist < lower)
                {
                    // 과도하게 붙었으면 살짝 이탈(뒤로)
                    var away = (transform.position - _target.position).normalized;
                    MoveTowards(transform.position + away * _retreatDist);
                    // 단순히 away * 1로 이동하면 목표 위치가 너무 가깝기 때문에 NavMeshAgent나 Steering이 경로 재계산을 반복하며 오버슈트(들락날락) 할 수 있음.
                    // away * _retreatDist는 초기 목표점을 명확히 떨어뜨려, "후퇴 의도"가 더 큰 거리로 잡히게 함.
                    // 미세 떨림 없이 안정된 후퇴 경로 생성.
                }
                else
                {
                    StopMove();
                }
            }
            else
            {
                StopMove();
            }

            // 사격 – 어그로 / 쿨다운 / LOS / 워밍업 / 조준정렬
            if (inAggro && HasLineOfSight() && IsAimAligned())
            {
                // 워밍업
                if (_warmupDelay > 0f && (Time.time - _enterCombatTime) < _warmupDelay)
                    continue;

                if (Time.time >= _lastFireTime + _fireCooldown)
                {
                    _lastFireTime = Time.time;
                    yield return StartCoroutine(FireBurst());
                }
            }
        }
    }

    private bool IsAimAligned()
    {
        // Muzzle 기준 방향과 타깃 방향 각도 비교
        Vector3 muzzlePos = _muzzle ? _muzzle.position : transform.position + Vector3.up * 1.0f;
        Vector3 forward = _muzzle ? _muzzle.forward : transform.forward;

        Vector3 dirToTarget = (_target.position) - muzzlePos;
        if (dirToTarget.sqrMagnitude < 0.0001f) return false;

        float angle = Vector3.Angle(forward, dirToTarget.normalized);
        return angle <= _fireAlignAngleDeg;
    }

    private void MoveTowards(Vector3 worldPos)
    {
        if (_useNavMeshIfAvailable && _agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
            _agent.speed = _moveSpeed;
            _agent.SetDestination(worldPos);
        }
        else
        {
            // 단순 이동(충돌/슬라이딩 보정 없음 – 프로토타입 목적)
            var dir = (worldPos - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                var step = dir.normalized * _moveSpeed * Time.fixedDeltaTime;
                transform.position += step;
            }
        }
    }

    private void StopMove()
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
        }
        // 단순 이동 모드에선 아무 것도 하지 않음
    }

    private bool HasLineOfSight()
    {
        var muzzle = _muzzle != null ? _muzzle.position : transform.position + Vector3.up * 1.0f;
        var targetPos = _target.position + Vector3.up * 1.0f;
        var dir = (targetPos - muzzle);
        var dist = dir.magnitude;

        if (Physics.Raycast(muzzle, dir.normalized, out var hit, dist, _obstacleMask, QueryTriggerInteraction.Ignore))
        {
            // 장애물에 막힘
            return false;
        }
        return true;
    }

    private IEnumerator FireBurst()
    {
        for (int i = 0; i < _burstCount; i++)
        {
            // 발사 직전에도 정렬 확인(회전 중 흔들림 대비)
            if (!IsAimAligned()) yield break;

            FireOnce();
            _onFired?.Invoke();

            if (_burstInterval > 0f && i < _burstCount - 1)
                yield return new WaitForSeconds(_burstInterval);
        }
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
