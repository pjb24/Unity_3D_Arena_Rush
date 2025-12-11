// Arena Rush – 매개변수 없는 범용 게임 이벤트 ScriptableObject

using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "GameEvent", menuName = "ArenaRush SO/Game Event (No Parameter)")]
public class GameEventSO : ScriptableObject
{
    private UnityAction _onEventRaised;

    public void Raise()
    {
        _onEventRaised?.Invoke();
    }

    public void AddListener(UnityAction listener)
    {
        _onEventRaised += listener;
    }

    public void RemoveListener(UnityAction listener)
    {
        _onEventRaised -= listener;
    }
}
