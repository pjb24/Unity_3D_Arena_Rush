///
/// 공격 범위 Trigger Collider를 켜는 동안만 충돌 데미지 처리
/// 
/// 적 모델의 자식 Hitbox 오브젝트에 SphereCollider(isTrigger=true) 붙이고 이 스크립트 붙임.
///

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyMeleeHitbox : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Collider _hitCollider;      // isTrigger = true
    [SerializeField] private Health _ownerHealth;        // 적 본체

    [Header("Rules")]
    [SerializeField] private bool _hitOncePerSwing = true;

    private int _damage = 10;
    private E_DamageType _damageType = E_DamageType.Melee;
    private string _targetTag = "Player";

    private bool _active;
    private GameObject _attacker;

    // 같은 히트박스에서 스윙당 1회
    private readonly HashSet<Health> _hitSet = new HashSet<Health>();

    // 드라이버가 넘겨주는 “이번 스윙 공유 중복 방지 집합”
    private HashSet<Health> _sharedHitSet;
    private bool _useSharedHitSet;

    private void Awake()
    {
        if (_hitCollider == null) _hitCollider = GetComponent<Collider>();
        if (_ownerHealth == null) _ownerHealth = GetComponentInParent<Health>();

        if (_hitCollider != null) _hitCollider.enabled = false;
    }

    private void OnEnable()
    {
        _active = false;
        _attacker = null;
        _hitSet.Clear();
        _sharedHitSet = null;
        _useSharedHitSet = false;

        if (_hitCollider != null) _hitCollider.enabled = false;
    }

    public void Configure(int damage, E_DamageType damageType, string targetTag)
    {
        _damage = damage;
        _damageType = damageType;
        _targetTag = targetTag;
    }

    /// <summary>
    /// 스윙 시작. sharedHitSet을 넘기면 여러 히트박스가 동일 타겟을 1번만 때리도록 공유한다.
    /// </summary>
    public void BeginSwing(GameObject attacker, HashSet<Health> sharedHitSet = null)
    {
        if (_hitCollider == null) return;
        if (_ownerHealth != null && _ownerHealth.IsDead) return;

        _active = true;
        _attacker = attacker;

        _hitSet.Clear();

        _sharedHitSet = sharedHitSet;
        _useSharedHitSet = (_sharedHitSet != null);

        _hitCollider.enabled = true;
    }

    public void EndSwing()
    {
        _active = false;
        _attacker = null;
        _sharedHitSet = null;
        _useSharedHitSet = false;

        if (_hitCollider != null) _hitCollider.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_active) return;
        TryDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!_active) return;
        TryDamage(other);
    }

    private void TryDamage(Collider other)
    {
        if (other == null) return;

        if (!string.IsNullOrEmpty(_targetTag) && !other.CompareTag(_targetTag))
            return;

        var targetHealth = other.GetComponentInParent<Health>();
        if (targetHealth == null) return;
        if (targetHealth.IsDead) return;

        // 1) 히트박스 자체 중복 방지
        if (_hitOncePerSwing && _hitSet.Contains(targetHealth))
            return;

        // 2) 공유 집합(다중 히트박스 간) 중복 방지
        if (_useSharedHitSet && _sharedHitSet.Contains(targetHealth))
            return;

        var info = new DamageInfo(_damage, _damageType, _attacker != null ? _attacker : gameObject);
        targetHealth.TakeDamage(info);

        if (_hitOncePerSwing)
            _hitSet.Add(targetHealth);

        if (_useSharedHitSet)
            _sharedHitSet.Add(targetHealth);
    }
}
