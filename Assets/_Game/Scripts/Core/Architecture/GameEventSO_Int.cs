// Arena Rush – int 매개변수 하나를 받는 게임 이벤트 ScriptableObject

using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "GameEvent_Int", menuName = "ArenaRush SO/Game Event (Int Parameter)")]
public class GameEventSO_Int : ScriptableObject
{
    private UnityAction<int> _onEventRaised;

    public void Raise(int value)
    {
        if (_onEventRaised == null) return;

        // 죽은 UnityEngine.Object 타겟 제거
        foreach (var d in _onEventRaised.GetInvocationList())
        {
            var target = d.Target as Object;
            if (target == null) _onEventRaised -= (UnityAction<int>)d;
        }

        _onEventRaised?.Invoke(value);
    }

    public void AddListener(UnityAction<int> listener)
    {
        _onEventRaised += listener;
    }

    public void RemoveListener(UnityAction<int> listener)
    {
        _onEventRaised -= listener;
    }
}
