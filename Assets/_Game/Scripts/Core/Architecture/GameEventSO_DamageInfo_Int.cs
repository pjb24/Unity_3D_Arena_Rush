// Arena Rush – DamageInfo 매개변수와 int 매개변수 두 종류를 받는 게임 이벤트 ScriptableObject

using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "GameEvent_DamageInfo_Int", menuName = "ArenaRush SO/Game Event (DamageInfo And Int Parameter)")]
public class GameEventSO_DamageInfo_Int : ScriptableObject
{
    private UnityAction<DamageInfo, int> _onEventRaised;

    public void Raise(DamageInfo damageInfo, int value)
    {
        _onEventRaised?.Invoke(damageInfo, value);
    }

    public void AddListener(UnityAction<DamageInfo, int> listener)
    {
        _onEventRaised += listener;
    }

    public void RemoveListener(UnityAction<DamageInfo, int> listener)
    {
        _onEventRaised -= listener;
    }
}
