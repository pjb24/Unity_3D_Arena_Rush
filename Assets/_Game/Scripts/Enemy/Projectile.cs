// Projectile.cs
// Enemy/Player 공용 발사체 통합 스크립트
// - 이동 모드: Rigidbody/Translate 선택
// - 소유자/팀 식별(Friendly Fire 방지)
// - 수명/피어스(관통)/중력/반발(옵션)/충돌 레이어 필터
// - Pooler 연동(있으면 사용), 없으면 Destroy
// - 이벤트: Fired/Hit/Despawn (AddListener/RemoveListener)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;

[DisallowMultipleComponent]
public class Projectile : MonoBehaviour
{
    // ===== Types =====
    public enum E_MoveType { Rigidbody, TransformTranslate }
    public enum E_Team { Neutral = 0, Player = 1, Enemy = 2 }

    // ===== Serialized =====
    [Header("Move")]
    [SerializeField] private E_MoveType _moveType = E_MoveType.Rigidbody;
    [SerializeField, Range(0f, 5f)] private float _gravityScale = 0f; // Rigidbody 모드일 때만 사용
    [Tooltip("Rigidbody 충돌 감지 모드를 continuous collision detection(연속 충돌 검출) 로 활성화할지 결정하는 옵션")]
    [SerializeField] private bool _useCCD = true;

    [Header("Lifetime")]
    [SerializeField, Range(0.05f, 10f)] private float _lifeSeconds = 3f;
    [SerializeField] private bool _destroyOnLifeEnd = false; // Pooler 없을 때 Destroy

    [Header("Damage/Hit")]
    [SerializeField, Range(0f, 500f)] private float _damage = 10f;
    [SerializeField, Range(0, 8)] private int _maxPenetration = 0; // 0=관통 없음
    [SerializeField] private LayerMask _hitLayers = ~0;            // 맞출 대상
    [SerializeField] private LayerMask _blockLayers = ~0;          // 지형 등 차단
    [SerializeField, Tooltip("OnTriggerEnter 사용(권장). Rigidbody 없는 Translate 모드일 때는 Collider(Trigger) 필요")]
    private bool _useTrigger = true;

    [Header("Ricochet (Optional)")]
    [SerializeField] private bool _ricochet = false;// 도탄
    [SerializeField, Range(0f, 1f)] private float _ricochetEnergyKeep = 0.6f;
    [SerializeField, Range(0.1f, 89f)] private float _ricochetMinAngle = 15f;

    [Header("VFX (Optional)")]
    [SerializeField] private GameObject _hitVfxPrefab;
    [SerializeField] private GameObject _trailObj; // 궤적 오브젝트(선택)

    [Header("Debug")]
    [SerializeField] private bool _log = false;

    // ===== Runtime =====
    private Rigidbody _rb;
    private Collider _col;
    private float _timer;
    private int _penetrated;
    private Vector3 _velocity;   // Translate 모드/리바운드 계산 공유
    private GameObject _owner;
    private E_Team _team = E_Team.Neutral;
    private bool _initialized;
    private readonly List<Collider> _ignored = new();
    private float _speed;

    private Pooler _pooler;

    // ===== Events (노출 금지) =====
    private event Action _onFired;
    private event Action<Vector3> _onHit;
    private event Action _onDespawn;

    public void AddFiredListener(Action listener) => _onFired += listener;
    public void RemoveFiredListener(Action listener) => _onFired -= listener;
    public void AddHitListener(Action<Vector3> listener) => _onHit += listener;
    public void RemoveHitListener(Action<Vector3> listener) => _onHit -= listener;
    public void AddDespawnListener(Action listener) => _onDespawn += listener;
    public void RemoveDespawnListener(Action listener) => _onDespawn -= listener;

    // ===== Public Init =====
    public void Init(Vector3 velocity, float damage, float lifeSeconds, GameObject owner, E_Team team = E_Team.Neutral)
    {
        _velocity = velocity;
        _damage = damage;
        _lifeSeconds = lifeSeconds;
        _owner = owner;
        _team = team;
        _timer = 0f;
        _penetrated = 0;
        _initialized = true;

        if (_rb != null)
        {
            _rb.linearVelocity = velocity;
            _rb.useGravity = _gravityScale > 0f;
            _rb.mass = Mathf.Max(0.01f, _rb.mass);
            if (_useCCD) _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        // 소유자와 초기 충돌 무시(짧은 시간)
        IgnoreOwnerColliders(true);

        _onFired?.Invoke();

        if (_log) Debug.Log($"[Projectile] Fired v={velocity} dmg={_damage} life={_lifeSeconds} team={_team}", this);
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        if (_col != null && _useTrigger) _col.isTrigger = true;

        _pooler = FindAnyObjectByType<Pooler>();
    }

    private void OnEnable()
    {
        _timer = 0f;
        _penetrated = 0;
        if (_trailObj != null) _trailObj.SetActive(true);

        _col.enabled = true;
    }

    private void OnDisable()
    {
        // 다음 스폰 대비 충돌 복원
        IgnoreOwnerColliders(false);
        _initialized = false;
        _owner = null;

        _col.enabled = false;
    }

    private void Update()
    {
        if (!_initialized) return;

        _timer += Time.deltaTime;
        if (_timer >= _lifeSeconds)
        {
            DespawnSelf();
            return;
        }

        if (_moveType == E_MoveType.TransformTranslate)
        {
            // Translate 모드 이동(간단)
            transform.position += _velocity * Time.deltaTime;

            // 중력 보정(옵션)
            if (_gravityScale > 0f)
            {
                _velocity += Physics.gravity * (_gravityScale * Time.deltaTime);
            }

            // 충돌 체크(간단 Raycast)
            var step = _velocity * Time.deltaTime;
            float dist = step.magnitude;
            if (dist > 0f)
            {
                if (Physics.Raycast(transform.position, step.normalized, out var hit, dist + 0.01f, _hitLayers | _blockLayers, QueryTriggerInteraction.Ignore))
                {
                    HandleImpact(hit);
                }
            }
        }
        else
        {
            // Rigidbody 모드에서도 회전 보정(전방 정렬)
            if (_rb != null && _rb.linearVelocity.sqrMagnitude > 0.0001f)
                transform.forward = _rb.linearVelocity.normalized;
        }

        // 초기 소유자 충돌 무시 해제(초기 프레임 0.1s)
        if (_timer > 0.1f) IgnoreOwnerColliders(false);
    }

    private void FixedUpdate()
    {
        if (!_initialized) return;

        if (_moveType == E_MoveType.Rigidbody && _rb != null)
        {
            if (_gravityScale > 0f) _rb.AddForce(Physics.gravity * (_gravityScale - 1f), ForceMode.Acceleration);
        }
    }

    // ===== Collision =====
    private void OnTriggerEnter(Collider other)
    {
        if (!_useTrigger) return;
        TryImpactFromCollider(other, other.ClosestPoint(transform.position), -transform.forward);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_useTrigger) return;
        var contact = collision.GetContact(0);
        TryImpactFromCollider(collision.collider, contact.point, contact.normal);
    }

    private void TryImpactFromCollider(Collider col, Vector3 point, Vector3 normal)
    {
        // 소유자/아군 무시
        if (IsOwnerOrFriendly(col)) return;

        // 레이어 필터
        int hitMask = 1 << col.gameObject.layer;
        bool isBlock = (_blockLayers.value & hitMask) != 0;
        bool isHit = (_hitLayers.value & hitMask) != 0;

        if (isHit) ApplyDamage(col.gameObject, point, normal);

        if (isBlock && _ricochet && CanRicochet(normal))
        {
            Ricochet(normal);
            return;
        }

        // 피어스 체크
        if (_maxPenetration > 0 && _penetrated < _maxPenetration)
        {
            _penetrated++;
            _onHit?.Invoke(point);
            return; // 계속 비행
        }

        // VFX
        SpawnHitVfx(point, normal);
        _onHit?.Invoke(point);
        DespawnSelf();
    }

    private void HandleImpact(RaycastHit hit)
    {
        if (IsOwnerOrFriendly(hit.collider)) return;

        int hitMask = 1 << hit.collider.gameObject.layer;
        bool isBlock = (_blockLayers.value & hitMask) != 0;
        bool isHit = (_hitLayers.value & hitMask) != 0;

        if (isHit) ApplyDamage(hit.collider.gameObject, hit.point, hit.normal);

        if (isBlock && _ricochet && CanRicochet(hit.normal))
        {
            Ricochet(hit.normal);
            return;
        }

        if (_maxPenetration > 0 && _penetrated < _maxPenetration)
        {
            _penetrated++;
            _onHit?.Invoke(hit.point);
            return;
        }

        SpawnHitVfx(hit.point, hit.normal);
        _onHit?.Invoke(hit.point);
        DespawnSelf();
    }

    private void ApplyDamage(GameObject victim, Vector3 point, Vector3 normal)
    {
        // 팀별 Friendly Fire 차단
        if (TryGetTeam(victim, out var victimTeam))
        {
            if (victimTeam == _team) return;
        }

        var hp = victim.GetComponent<Health>();
        if (hp != null)
        {
            // 프로젝트에 DamageInfo가 이미 있으면 동일 시그니처로 교체
            hp.TakeDamage(new DamageInfo(Mathf.RoundToInt(_damage), E_DamageType.Generic, _owner, point, normal));
        }
    }

    // ===== Ricochet =====
    private bool CanRicochet(Vector3 normal)
    {
        if (_velocity.sqrMagnitude < 0.01f && (_rb == null || _rb.linearVelocity.sqrMagnitude < 0.01f)) return false;

        Vector3 v = _moveType == E_MoveType.Rigidbody ? _rb.linearVelocity : _velocity;
        float angle = Vector3.Angle(-v.normalized, normal); // 입사각
        return angle >= _ricochetMinAngle;
    }

    private void Ricochet(Vector3 normal)
    {
        Vector3 v = _moveType == E_MoveType.Rigidbody ? _rb.linearVelocity : _velocity;
        Vector3 r = Vector3.Reflect(v, normal) * _ricochetEnergyKeep;

        if (_moveType == E_MoveType.Rigidbody && _rb != null)
            _rb.linearVelocity = r;
        else
            _velocity = r;

        transform.forward = r.sqrMagnitude > 0.0001f ? r.normalized : transform.forward;
    }

    // ===== Helpers =====
    private void SpawnHitVfx(Vector3 point, Vector3 normal)
    {
        if (_hitVfxPrefab == null) return;

        Quaternion rot = Quaternion.LookRotation(normal);
        GameObject fx;
        if (_pooler != null) fx = _pooler.Spawn(_hitVfxPrefab, point, rot);
        else fx = Instantiate(_hitVfxPrefab, point, rot);

        // 간이 자동 회수
        if (fx != null) StartCoroutine(AutoDespawnFx(fx, 1.2f, _pooler));
    }

    private IEnumerator AutoDespawnFx(GameObject fx, float t, Pooler pooler)
    {
        yield return new WaitForSeconds(t);
        if (fx == null) yield break;
        if (pooler != null) pooler.Despawn(fx);
        else Destroy(fx);
    }

    private void DespawnSelf()
    {
        _onDespawn?.Invoke();
        if (_trailObj != null) _trailObj.SetActive(false);

        if (_pooler != null) _pooler.Despawn(gameObject);
        else
        {
            if (_destroyOnLifeEnd) Destroy(gameObject);
            else gameObject.SetActive(false);
        }
    }

    private void IgnoreOwnerColliders(bool ignore)
    {
        if (_owner == null || _col == null) return;

        _ignored.RemoveAll(c => c == null);

        var ownerCols = _owner.GetComponentsInChildren<Collider>(true);
        foreach (var oc in ownerCols)
        {
            if (oc == null || oc == _col) continue;
            Physics.IgnoreCollision(_col, oc, ignore);
            if (ignore && !_ignored.Contains(oc)) _ignored.Add(oc);
        }

        if (!ignore)
        {
            foreach (var oc in _ignored)
            {
                if (oc != null) Physics.IgnoreCollision(_col, oc, false);
            }
            _ignored.Clear();
        }
    }

    private bool IsOwnerOrFriendly(Collider other)
    {
        if (_owner != null && other.attachedRigidbody != null && other.attachedRigidbody.gameObject == _owner)
            return true;

        if (TryGetTeam(other.gameObject, out var t))
        {
            if (t == _team) return true;
        }
        return false;
    }

    private bool TryGetTeam(GameObject go, out E_Team team)
    {
        // 팀 식별 컴포넌트 구현 예시: ITeamProvider 또는 TeamTag
        // 없으면 Layer/Tag로 폴백
        var provider = go.GetComponentInParent<ITeamProvider>();
        if (provider != null)
        {
            team = provider.Team;
            return true;
        }

        // 폴백(선택): 태그/레이어로 분기
        if (go.CompareTag("Player"))
        {
            team = E_Team.Player;
            return true;
        }
        if (go.CompareTag("Enemy"))
        {
            team = E_Team.Enemy;
            return true;
        }

        team = E_Team.Neutral;
        return false;
    }

    // ===== Public API (선택) =====
    public void SetTeam(E_Team team) => _team = team;
    public void SetOwner(GameObject owner) => _owner = owner;
    public void SetDamage(float damage) => _damage = damage;
    public void SetSpeed(float speed)
    {
        _speed = speed;
        if (_moveType == E_MoveType.Rigidbody && _rb != null && _rb.linearVelocity.sqrMagnitude > 0.001f)
        {
            _rb.linearVelocity = _rb.linearVelocity.normalized * _speed;
        }
        else if (_moveType == E_MoveType.TransformTranslate && _velocity.sqrMagnitude > 0.001f)
        {
            _velocity = _velocity.normalized * _speed;
        }
    }
}

// ===== 팀 제공 인터페이스(선택) =====
public interface ITeamProvider
{
    Projectile.E_Team Team { get; }
}
