// Arena Rush – 게임 상태 데이터를 담는 ScriptableObject

using UnityEngine;

[CreateAssetMenu(fileName = "GameState", menuName = "ArenaRush SO/GameState")]
public class GameStateSO : ScriptableObject
{
    // ===== States =====
    public enum E_GamePlayState
    {
        Boot = 0,
        Playing = 1,
        PerkSelect = 2,
        Paused = 3,
        GameOver = 4,
    }

    // ===== Runtime Data =====
    [Header("State")]
    public E_GamePlayState CurrentState = E_GamePlayState.Boot;
    public E_GamePlayState PreviousState = E_GamePlayState.Boot;

    [Header("Run Data")]
    public bool IsRunActive = false;
    public bool IsInputLocked = false; // PerkSelect/Paused/GameOver에서 true
    public int CurrentWave = 0;
    public int EnemiesAlive = 0;

    // ===== Default Values (Editor Persistence Problem 해결용) =====
    [Header("Default Values")]
    [SerializeField] private E_GamePlayState _defaultState = E_GamePlayState.Boot;

    public void ResetToDefault()
    {
        CurrentState = _defaultState;
        PreviousState = _defaultState;
        IsRunActive = false;
        IsInputLocked = false;
        CurrentWave = 0;
        EnemiesAlive = 0;
    }

    public bool IsPlayable()
    {
        return CurrentState == E_GamePlayState.Playing;
    }
}
