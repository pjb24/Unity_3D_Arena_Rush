// Arena Rush – GameState.E_GamePlayState 매개변수 두 개를 받는 게임 이벤트 ScriptableObject

using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "GameEvent_StateChanged", menuName = "ArenaRush SO/Game Event (StateChanged Parameter)")]
public class GameEventSO_StateChanged : ScriptableObject
{
    // UnityAction은 최대 4개의 인자를 지원합니다.
    private UnityAction<GameStateSO.E_GamePlayState, GameStateSO.E_GamePlayState> _onEventRaised;

    public void Raise(GameStateSO.E_GamePlayState prev, GameStateSO.E_GamePlayState current)
    {
        _onEventRaised?.Invoke(prev, current);
    }

    public void AddListener(UnityAction<GameStateSO.E_GamePlayState, GameStateSO.E_GamePlayState> listener)
    {
        _onEventRaised += listener;
    }

    public void RemoveListener(UnityAction<GameStateSO.E_GamePlayState, GameStateSO.E_GamePlayState> listener)
    {
        _onEventRaised -= listener;
    }
}
