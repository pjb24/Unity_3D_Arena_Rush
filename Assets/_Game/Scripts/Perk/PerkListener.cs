using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// - 각 대상 컴포넌트의 "기본값"을 캡처
/// - Perk 효과를 누적 버킷(Flat/Percent/Multiply)으로 관리
/// - 누적 공식으로 재계산 후 대상 컴포넌트에 직접 대입
/// </summary>
public class PerkListener : MonoBehaviour
{
    [Header("Targets (직접 참조)")]
    [SerializeField] private Gun _gun;                      // Damage, FireRate
    [SerializeField] private PlayerController _player;      // MoveSpeed
    [SerializeField] private Dash _dash;                    // CooldownSeconds
    [SerializeField] private Health _health;                // MaxHP (CurrentHP는 선택)

    // 체력 회복 정책 옵션(선택 적용)
    [Header("HP Policy")]
    [Tooltip("MaxHP 상승 시 현재 체력도 함께 비율 보정할지 여부, False로 하면 MaxHP 증가량 만큼 CurrentHP를 증가.")]
    [SerializeField] private bool scaleCurrentHpOnMaxUp = false;

    [Header("Debug")]
    [SerializeField] private bool _logPerkState = false;

    // --- 내부 상태 ---
    private readonly Dictionary<E_PerkStat, StatBucket> _mods = new Dictionary<E_PerkStat, StatBucket>(8);
    private readonly Dictionary<E_PerkStat, float> _base = new Dictionary<E_PerkStat, float>(8);

    private GameState _gs;
    private PerkUI _perkUI;

    private void Awake()
    {
        _gs = FindAnyObjectByType<GameState>();
        _perkUI = FindAnyObjectByType<PerkUI>();

        InitBuckets();
        CaptureBases();
    }

    private void OnEnable()
    {
        if (_perkUI != null)
        {
            _perkUI.OnPerkAppliedEvent.AddListener(ApplyPerk);
        }

        if (_gs != null)
        {
            _gs.OnRunStartedEvent.AddListener(OnRunStarted);
        }
    }

    private void OnDisable()
    {
        if (_perkUI != null)
        {
            _perkUI.OnPerkAppliedEvent.RemoveListener(ApplyPerk);
        }

        if (_gs != null)
        {
            _gs.OnRunStartedEvent.RemoveListener(OnRunStarted);
        }
    }

    // === GameState 연동: 런 시작 시 초기화 ===
    private void OnRunStarted()
    {
        InitBuckets();
        CaptureBases();
        RecalculateAll();
        if (_logPerkState) Debug.Log("[PerkListener] RunStarted → reset mods & recapture base.");
    }

    // 버킷 초기화
    private void InitBuckets()
    {
        _mods.Clear();
        foreach (E_PerkStat s in Enum.GetValues(typeof(E_PerkStat)))
            _mods[s] = StatBucket.Default();
    }

    // 기본값 캡처
    private void CaptureBases()
    {
        _base.Clear();

        if (_gun != null)
        {
            _base[E_PerkStat.Damage] = _gun.Damage;
            _base[E_PerkStat.FireRate] = _gun.FireRate;
        }

        if (_player != null)
            _base[E_PerkStat.MoveSpeed] = _player.MoveSpeed;

        if (_dash != null)
            _base[E_PerkStat.DashCooldown] = _dash.Cooldown;

        if (_health != null)
            _base[E_PerkStat.MaxHP] = _health.MaxHP;
    }

    private void ApplyPerk(Perk perk)
    {
        if (perk == null || perk.Effects == null) return;

        foreach (var e in perk.Effects)
        {
            if (!_mods.TryGetValue(e.stat, out var bucket))
            {
                bucket = StatBucket.Default();
            }

            switch (e.op)
            {
                case E_PerkOp.AddFlat:
                    bucket.Flat += e.value;
                    break;
                case E_PerkOp.AddPercent:
                    bucket.Percent += e.value;  // 0.2 => +20%
                    break;
                case E_PerkOp.Multiply:
                    bucket.Multiplier *= e.value; // 1.1 => ×1.1
                    break;
            }

            _mods[e.stat] = bucket;

        }

        if (_logPerkState)
        {
            Debug.Log("[PerkListener] ApplyPerk "
                + perk.BuildCompactSummary());
        }

        RecalculateAll();

        if (_logPerkState)
        {
            Debug.Log("Damage: " + _gun.Damage
                + ", FireRate: " + _gun.FireRate
                + ", MoveSpeed: " + _player.MoveSpeed
                + ", Cooldown: " + _dash.Cooldown
                + ", MaxHP: " + _health.MaxHP
                + ", CurrentHP: " + _health.CurrentHP);
        }
    }

    private void RecalculateAll()
    {
        // Damage
        if (_gun != null && _base.TryGetValue(E_PerkStat.Damage, out var baseDamage))
            _gun.Damage = Mathf.FloorToInt(Compose(baseDamage, _mods[E_PerkStat.Damage]));

        // FireRate
        if (_gun != null && _base.TryGetValue(E_PerkStat.FireRate, out var baseFR))
            _gun.FireRate = Mathf.Max(0.01f, Compose(baseFR, _mods[E_PerkStat.FireRate]));

        // MoveSpeed
        if (_player != null && _base.TryGetValue(E_PerkStat.MoveSpeed, out var baseMS))
            _player.MoveSpeed = Mathf.Max(0f, Compose(baseMS, _mods[E_PerkStat.MoveSpeed]));

        // DashCooldown (감소 퍼크는 음수 AddFlat/Percent로 정의 권장)
        if (_dash != null && _base.TryGetValue(E_PerkStat.DashCooldown, out var baseCD))
            _dash.Cooldown = Mathf.Max(0f, Compose(baseCD, _mods[E_PerkStat.DashCooldown]));

        // MaxHP (정수 처리 + 현재 체력 정책)
        if (_health != null && _base.TryGetValue(E_PerkStat.MaxHP, out var baseHPf))
        {
            var newMax = Mathf.Max(1, Mathf.FloorToInt(Compose(baseHPf, _mods[E_PerkStat.MaxHP])));
            if (scaleCurrentHpOnMaxUp && _health.MaxHP > 0)
            {
                float ratio = Mathf.Clamp01(_health.CurrentHP / (float)_health.MaxHP);
                _health.MaxHP = newMax;

                // 현재 HP 비율을 유지.
                _health.CurrentHP = Mathf.Clamp(Mathf.FloorToInt(newMax * ratio), 1, newMax);
            }
            else
            {
                int delta = newMax - _health.MaxHP;
                _health.MaxHP = newMax;

                // 늘어난 MaxHP 만큼 CurrentHP를 증가.
                _health.CurrentHP = Mathf.Clamp(_health.CurrentHP + delta, 1, newMax);
            }
        }
    }

    // 합성 공식
    private static float Compose(float baseValue, StatBucket b)
    {
        // (base + ΣFlat) × ΠMultiply × (1 + ΣPercent)
        return (baseValue + b.Flat) * b.Multiplier * (1f + b.Percent);
    }

    // 누적 버킷 구조체
    private struct StatBucket
    {
        public float Flat;        // +a
        public float Percent;     // +b (0.2 => +20%)
        public float Multiplier;  // ×c (기본 1)

        public static StatBucket Default()
        {
            return new StatBucket { Flat = 0f, Percent = 0f, Multiplier = 1f };
        }
    }
}
