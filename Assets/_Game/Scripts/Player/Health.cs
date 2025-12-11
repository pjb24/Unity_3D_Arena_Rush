// Health.cs
// 체력/대미지/사망 공통 컴포넌트(플레이어/적 공용)
// - Max/Current HP, Heal/Damage/Kill/Revive
// - 피격 후 짧은 무적(i-Frame) 옵션
// - OnDamaged / OnHealed / OnDeath 이벤트
// - 사망 시 비활성/파괴 옵션

using System;
using System.Collections;
using UnityEngine;

public enum E_DamageType { Generic, Melee, Bullet, Explosion, Fire, Electric }

[System.Serializable]
public struct DamageInfo
{
    public int amount;  // 대미지 크기
    public E_DamageType type;   // 대미지 타입
    public GameObject attacker; // 공격자
    public Vector3 hitPoint;    // 피격 위치
    public Vector3 hitNormal;   // 피격 위치의 노말
    public bool critical;   // 크리티컬 여부
    public float knockback; // 넉백 크기

    public DamageInfo(int amount,
        E_DamageType type = E_DamageType.Generic,
        GameObject attacker = null,
        Vector3 hitPoint = default,
        Vector3 hitNormal = default,
        bool critical = false,
        float knockback = 0f)
    {
        this.amount = Mathf.Max(0, amount);
        this.type = type;
        this.attacker = attacker;
        this.hitPoint = hitPoint;
        this.hitNormal = hitNormal;
        this.critical = critical;
        this.knockback = knockback;
    }
}

[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Header("Player Check")]
    [Tooltip("Player에 부착된 Health 일 때 True로 설정.")]
    [SerializeField] private bool _isPlayer = false;

    [Header("HP")]
    [SerializeField] private int _maxHP = 100;
    [SerializeField] private int _currentHP = -1; // -1이면 OnEnable 시 Max로 채움

    [Header("Hit Invincibility Frame")]
    [SerializeField, Tooltip("피격 후 무적 시간(초)")]
    private float _invincibleDurationOnHit = 0.2f;

    [Header("Death Handling")]
    [SerializeField] private GameObject[] _disableOnDeath;

    // ===== ScriptableObject References =====
    [Header("ScriptableObject Events")]
    public GameEventSO_DamageInfo_Int OnDamagedEvent;    // (DamageInfo, remainingHP)
    public GameEventSO_Int_Int OnHealedEvent;   // (healedAmount, currentHP)
    public GameEventSO_Health OnPlayerDeathEvent;    // 사망 이벤트

    [Header("GameState Integration")]
    [Tooltip("Playing 상태에서만 데미지 처리. Perk/Pause/GameOver 동안 무적 취급.")]
    [SerializeField] private bool _respectGameState = true;

    [Tooltip("i-Frame을 TimeScale과 무관하게 흘려보낼지 여부(Perk/Pause 중에도 경과).")]
    [SerializeField] private bool _iFrameUseUnscaledTime = false;

    private Action<Health> _onDeathEvent;
    public void AddListenerOnDeathEvent(Action<Health> listener) => _onDeathEvent += listener;
    public void RemoveListenerOnDeathEvent(Action<Health> listener) => _onDeathEvent -= listener;

    private GameState _gs;

    public int MaxHP { get { return _maxHP; } set { _maxHP = value; } }
    public int CurrentHP { get { return _currentHP; } set { _currentHP = value; } }
    public bool IsDead { get; private set; }
    public bool IsInvulnerable { get; private set; }
    public void SetInvulnerable(bool flag)
    {
        IsInvulnerable = flag;
    }

    private Coroutine _invulnRoutine;
    private DamageInfo _lastHit;

    private void Awake()
    {
        _gs = FindAnyObjectByType<GameState>();
    }

    private void OnEnable()
    {
        if (_currentHP <= 0)
            _currentHP = _maxHP;

        IsDead = false;
        IsInvulnerable = false;
    }

    // -------------------------------------------------------------------
    // HP 관련
    // -------------------------------------------------------------------

    /// <summary>최대 체력 설정. fill이 true면 현재 체력도 갱신.</summary>
    public void SetMaxHP(int newMax, bool fill = false)
    {
        _maxHP = Mathf.Max(1, newMax);
        if (fill)
        {
            _currentHP = _maxHP;
        }
        else
        {
            _currentHP = Mathf.Clamp(_currentHP, 0, _maxHP);
        }
    }

    /// <summary>치유. 실제로 회복된 양을 반환.</summary>
    public int Heal(int amount)
    {
        if (IsDead || amount <= 0) return 0;

        int prev = _currentHP;
        _currentHP = Mathf.Min(_maxHP, _currentHP + amount);
        int healed = _currentHP - prev;

        if (healed > 0)
        {
            OnHealedEvent.Raise(healed, _currentHP);
        }

        return healed;
    }

    // -------------------------------------------------------------------
    // Damage 처리
    // -------------------------------------------------------------------
    /// <summary>데미지 처리(무적/사망 처리 포함). 성공 여부 반환.</summary>
    public bool TakeDamage(DamageInfo info)
    {
        // GameState 정책 적용: Playing + 입력잠금 아님일 때만 허용
        if (_respectGameState)
        {
            if (_gs != null && (!_gs.IsPlayable() || _gs.IsInputLocked()))
                return false;
        }

        if (IsDead || IsInvulnerable || info.amount <= 0)
            return false;

        _lastHit = info;

        _currentHP = Mathf.Max(0, _currentHP - info.amount);
        OnDamagedEvent.Raise(info, _currentHP);

        if (_currentHP == 0)
        {
            Die();
        }
        else if (_invincibleDurationOnHit > 0f)
        {
            if (_invulnRoutine != null) StopCoroutine(_invulnRoutine);

            _invulnRoutine = StartCoroutine(Co_Invulnerable(_invincibleDurationOnHit));
        }

        return true;
    }

    /// <summary>간편 오버로드.</summary>
    public bool TakeDamage(int amount, GameObject attacker = null, E_DamageType type = E_DamageType.Generic)
    {
        return TakeDamage(new DamageInfo(amount, type, attacker));
    }

    // -------------------------------------------------------------------
    // Death & Revive
    // -------------------------------------------------------------------
    /// <summary>즉시 사망 처리.</summary>
    public void Kill(GameObject killer = null, E_DamageType type = E_DamageType.Generic)
    {
        if (IsDead) return;

        _lastHit = new DamageInfo(_currentHP > 0 ? _currentHP : 0, type, killer);
        _currentHP = 0;
        Die();
    }

    /// <summary>부활. 지정 HP가 0 이하이면 Max로 채움.</summary>
    public void Revive(int hp = -1)
    {
        _currentHP = hp > 0 ? Mathf.Min(hp, _maxHP) : _maxHP;
        IsDead = false;
        IsInvulnerable = false;
        if (_invulnRoutine != null)
        {
            StopCoroutine(_invulnRoutine);
            _invulnRoutine = null;
        }
        SetObjectsActiveOnDeath(false); // 부활 시 비활성 해제
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // 사망 시 처리
        if (_isPlayer)
        {
            OnPlayerDeathEvent.Raise(this);
        }
        else
        {
            _onDeathEvent(this);
        }
        SetObjectsActiveOnDeath(true);

        // Pooler 사용 환경/정책에 따라 null 체크
        if (Pooler.Instance != null)
            Pooler.Instance.Despawn(gameObject);
        else
            gameObject.SetActive(false);
    }

    private void SetObjectsActiveOnDeath(bool die)
    {
        if (_disableOnDeath == null) return;

        for (int i = 0; i < _disableOnDeath.Length; i++)
        {
            if (_disableOnDeath[i] == null) continue;

            _disableOnDeath[i].SetActive(!die);
        }
    }

    // -------------------------------------------------------------------
    // i-Frame
    // -------------------------------------------------------------------
    private IEnumerator Co_Invulnerable(float duration)
    {
        IsInvulnerable = true;
        
        if (_iFrameUseUnscaledTime)
        {
            float end = Time.unscaledTime + duration;
            while (Time.unscaledTime < end)
                yield return null;
        }
        else
        {
            // PerkSelect/Paused(TimeScale=0)에서는 경과하지 않음
            yield return new WaitForSeconds(duration);
        }

        IsInvulnerable = false;
        _invulnRoutine = null;
    }

    /// <summary>마지막 피격 정보 조회(히트리액션/리플레이 등에 활용).</summary>
    public bool TryGetLastHit(out DamageInfo info)
    {
        info = _lastHit;
        return _lastHit.amount > 0 || _lastHit.attacker != null;
    }
}
