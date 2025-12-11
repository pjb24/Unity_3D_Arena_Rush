// Arena Rush – Perk 매개변수 하나를 받는 게임 이벤트 ScriptableObject

using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "GameEvent_Perk", menuName = "ArenaRush SO/Game Event (Perk Parameter)")]
public class GameEventSO_Perk : ScriptableObject
{
    private UnityAction<Perk> _onEventRaised;

    public void Raise(Perk value)
    {
        _onEventRaised?.Invoke(value);
    }

    public void AddListener(UnityAction<Perk> listener)
    {
        _onEventRaised += listener;
    }

    public void RemoveListener(UnityAction<Perk> listener)
    {
        _onEventRaised -= listener;
    }
}
