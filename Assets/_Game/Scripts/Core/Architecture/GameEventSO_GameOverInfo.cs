// Arena Rush – GameOverInfo 매개변수 하나를 받는 게임 이벤트 ScriptableObject

using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "GameEvent_GameOverInfo", menuName = "ArenaRush SO/Game Event (GameOverInfo Parameter)")]
public class GameEventSO_GameOverInfo : ScriptableObject
{
    private UnityAction<GameOverInfo> _onEventRaised;

    public void Raise(GameOverInfo value)
    {
        if (_onEventRaised == null) return;

        // 죽은 UnityEngine.Object 타겟 제거
        foreach (var d in _onEventRaised.GetInvocationList())
        {
            var target = d.Target as Object;
            if (target == null) _onEventRaised -= (UnityAction<GameOverInfo>)d;
        }

        _onEventRaised?.Invoke(value);
    }

    public void AddListener(UnityAction<GameOverInfo> listener)
    {
        _onEventRaised += listener;
    }

    public void RemoveListener(UnityAction<GameOverInfo> listener)
    {
        _onEventRaised -= listener;
    }
}
