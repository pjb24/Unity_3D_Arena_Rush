///
/// (Animation Event가 직접 호출하는 브릿지)
/// Animation Clip에는 아래 public 함수만 이벤트로 등록
///

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyBruiserAnimEventBridge : MonoBehaviour
{
    [Header("Default Receivers (Optional)")]
    [SerializeField] private MonoBehaviour[] _defaultReceivers; // IEnemyBruiserAnimEventListener 구현체만 넣어라

    private readonly List<IEnemyBruiserAnimEventListener> _listeners = new List<IEnemyBruiserAnimEventListener>(8);

    private void Awake()
    {
        // 인스펙터로 지정된 리스너 자동 등록
        if (_defaultReceivers == null) return;

        for (int i = 0; i < _defaultReceivers.Length; i++)
        {
            var mb = _defaultReceivers[i];
            if (mb == null) continue;

            if (mb is IEnemyBruiserAnimEventListener l)
                AddListener(l);
            else
                Debug.LogWarning($"[EnemyBruiserAnimEventBridge] Receiver does not implement IEnemyBruiserAnimEventListener: {mb.name}", mb);
        }
    }

    public void AddListener(IEnemyBruiserAnimEventListener listener)
    {
        if (listener == null) return;
        if (_listeners.Contains(listener)) return;
        _listeners.Add(listener);
    }

    public void RemoveListener(IEnemyBruiserAnimEventListener listener)
    {
        if (listener == null) return;
        _listeners.Remove(listener);
    }

    // ===== Animation Events (Clip에서 호출) =====
    public void AttackHitboxOn_All()
    {
        for (int i = 0; i < _listeners.Count; i++)
            _listeners[i].OnAttackHitboxOnAll();
    }

    public void AttackHitboxOff_All()
    {
        for (int i = 0; i < _listeners.Count; i++)
            _listeners[i].OnAttackHitboxOffAll();
    }

    public void AttackHitboxOn_Index(int index)
    {
        for (int i = 0; i < _listeners.Count; i++)
            _listeners[i].OnAttackHitboxOnIndex(index);
    }

    public void AttackHitboxOff_Index(int index)
    {
        for (int i = 0; i < _listeners.Count; i++)
            _listeners[i].OnAttackHitboxOffIndex(index);
    }

    public void AttackEnd()
    {
        for (int i = 0; i < _listeners.Count; i++)
            _listeners[i].OnAttackEnd();
    }
    // ===========================================
}
