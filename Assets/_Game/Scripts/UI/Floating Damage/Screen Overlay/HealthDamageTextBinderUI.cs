// HealthDamageTextBinder.cs
// Health 이벤트에 구독하여 대미지/회복 시 플로팅 텍스트를 생성.

using UnityEngine;
using System;

[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class HealthDamageTextBinderUI : MonoBehaviour
{
    [Header("Options")]
    [SerializeField] private bool _showDamage = true;
    [SerializeField] private bool _showHeal = true;
    [SerializeField] private Vector3 _fallbackOffset = new Vector3(0f, 1.6f, 0f);
    [SerializeField] private Camera _overrideCamera; // 비우면 Camera.main 사용

    private Health _health;

    private void Awake()
    {
        _health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        if (_health != null)
        {
            _health.AddListenerOnDamagedEvent(OnDamaged);
            _health.AddListenerOnHealedEvent(OnHealed);
        }
    }

    private void OnDisable()
    {
        if (_health != null)
        {
            _health.RemoveListenerOnDamagedEvent(OnDamaged);
            _health.RemoveListenerOnHealedEvent(OnHealed);
        }
    }

    private void OnDamaged(DamageInfo info, int remainHP)
    {
        if (!_showDamage || DamageTextManagerUI.Instance == null) return;

        int amount = Mathf.Max(0, info.amount);
        bool isCrit = false;
        try { isCrit = info.critical; } catch { /* 필드 없으면 기본 false */ }

        Vector3 pos = transform.position + _fallbackOffset;
        try
        {
            // info.point가 유효하면 히트 지점 사용
            if (info.hitPoint != Vector3.zero) pos = info.hitPoint;
        }
        catch { /* point 미구현 시 무시 */ }

        var cam = _overrideCamera != null ? _overrideCamera : Camera.main;
        DamageTextManagerUI.Instance.ShowDamage(amount, pos, isCritical: isCrit, cam: cam);
    }

    private void OnHealed(int healAmount, int remainHP)
    {
        if (!_showHeal || DamageTextManagerUI.Instance == null) return;

        int amount = Mathf.Max(0, healAmount);
        Vector3 pos = transform.position + _fallbackOffset;

        var cam = _overrideCamera != null ? _overrideCamera : Camera.main;
        DamageTextManagerUI.Instance.ShowHeal(amount, pos, cam: cam);
    }
}
