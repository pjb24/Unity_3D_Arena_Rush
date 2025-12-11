// Arena Rush – Health 매개변수 하나를 받는 게임 이벤트 ScriptableObject

using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "GameEvent_Health", menuName = "ArenaRush SO/Game Event (Health Parameter)")]
public class GameEventSO_Health : ScriptableObject
{
    private UnityAction<Health> _onEventRaised;

    public void Raise(Health value)
    {
        _onEventRaised?.Invoke(value);
    }

    public void AddListener(UnityAction<Health> listener)
    {
        _onEventRaised += listener;
    }

    public void RemoveListener(UnityAction<Health> listener)
    {
        _onEventRaised -= listener;
    }
}
