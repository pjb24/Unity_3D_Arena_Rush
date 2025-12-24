// Arena Rush – 게임 상태 데이터를 담는 ScriptableObject

using UnityEngine;

[CreateAssetMenu(fileName = "GameState", menuName = "ArenaRush SO/GameState")]
public class GameStateSO : ScriptableObject
{
    // ===== States =====
    public enum E_GamePlayState
    {
        None = 0,
        Boot = 1,
        Playing = 2,
        PerkSelect = 3,
        Paused = 4,
        GameOver = 5,
    }

    // ===== Runtime Data =====
    [Header("State")]
    public E_GamePlayState CurrentState = E_GamePlayState.None;
    public E_GamePlayState PreviousState = E_GamePlayState.None;

    [Header("Run Data")]
    public bool IsRunActive = false;
    public bool IsInputLocked = false; // PerkSelect/Paused/GameOver에서 true
    public int CurrentWave = 0;
    public int EnemiesAlive = 0;
    public int ClearedWave = 0;

    // ===== Default Values (Editor Persistence Problem 해결용) =====
    [Header("Default Values")]
    [SerializeField] private E_GamePlayState _defaultState = E_GamePlayState.None;

    public void ResetToDefault()
    {
        CurrentState = _defaultState;
        PreviousState = _defaultState;
        IsRunActive = false;
        IsInputLocked = false;
        CurrentWave = 0;
        EnemiesAlive = 0;
        ClearedWave = 0;
    }

    public bool IsPlayable()
    {
        return CurrentState == E_GamePlayState.Playing;
    }
}
