// Arena Rush – int 매개변수 둘을 받는 게임 이벤트 ScriptableObject

using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "GameEvent_Int_Int", menuName = "ArenaRush SO/Game Event (2 Int Parameter)")]
public class GameEventSO_Int_Int : ScriptableObject
{
    private UnityAction<int, int> _onEventRaised;

    public void Raise(int value1, int value2)
    {
        _onEventRaised?.Invoke(value1, value2);
    }

    public void AddListener(UnityAction<int, int> listener)
    {
        _onEventRaised += listener;
    }

    public void RemoveListener(UnityAction<int, int> listener)
    {
        _onEventRaised -= listener;
    }
}
