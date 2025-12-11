// GameState.cs
// Arena Rush – 게임 루프/상태 관리자
// - GameStateSO 에셋을 통해 상태 데이터 관리
// - GameEventSO 에셋들을 통해 상태/데이터 변경 이벤트 방송
// - TimeScale 기반 일시정지(PerkSelect/Paused/GameOver)
//
// 의존: GameStateSO, GameEventSO, GameEventSO_Int, GameEventSO_StateChanged

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public struct GameOverInfo
{
    public int wave;
    public float survivedSeconds;
    public string reason;
    public int bestWave;
    public float bestSeconds;
    public bool isNewBestWave;
    public bool isNewBestTime;
}

[DisallowMultipleComponent]
public class GameState : MonoBehaviour
{
    // ===== ScriptableObject References =====
    [Header("ScriptableObject Data")]
    [SerializeField] private GameStateSO _gameStateData; // 씬에서 생성된 GameState.asset 연결

    [Header("ScriptableObject Events")]
    public GameEventSO_StateChanged OnStateChangedEvent; // 상태 변경 이벤트
    public GameEventSO OnRunStartedEvent;             // 런 시작 이벤트
    public GameEventSO OnRunEndedEvent;               // 런 종료 이벤트
    public GameEventSO OnPerkSelectOpenedEvent;       // Perk 선택 화면 열림 이벤트
    public GameEventSO_GameOverInfo OnGameOverEvent;               // 게임오버 이벤트

    // ===== Config =====
    [Header("Config")]
    [SerializeField] private bool _autoStartOnAwake = true;
    [SerializeField] private bool _dontDestroyOnLoad = true;
    [SerializeField] private bool _useTimeScalePause = true;
    [SerializeField, Range(0f, 1f)] private float _pausedTimeScale = 0f;
    [SerializeField, Range(0.1f, 2f)] private float _playingTimeScale = 1f;

    [Header("Dev Hotkeys")]
    [SerializeField] private bool _enableDevHotkeys = true;
    [SerializeField] private KeyCode _keyStartRun = KeyCode.F5;
    [SerializeField] private KeyCode _keyOpenPerk = KeyCode.F6;
    [SerializeField] private KeyCode _keyGameOver = KeyCode.F7;
    [SerializeField] private KeyCode _keyRestart = KeyCode.F8;

    private PerkUI _perkUI;
    private PlayerController _playerController;
    private WaveManager _waveManager;

    private float _sessionStartTime = 0f;
    private bool _gameOverTriggered = false;

    // PlayerPrefs Keys
    private const string _PP_BestWave = "AR_BestWave";
    private const string _PP_BestTime = "AR_BestTime";

    private void Awake()
    {
        // ScriptableObject 데이터 초기화 (에디터 데이터 유지 문제 해결)
        if (_gameStateData == null)
        {
            Debug.LogError("[GameState] GameStateSO 에셋이 연결되지 않았습니다. 인스펙터에서 연결해주세요.");
            return;
        }
        _gameStateData.ResetToDefault();

        if (_dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        _perkUI = FindAnyObjectByType<PerkUI>();
        _playerController = FindAnyObjectByType<PlayerController>();
        _waveManager = FindAnyObjectByType<WaveManager>();

        // 초기 상태
        ChangeState(GameStateSO.E_GamePlayState.Boot);
        ApplyTimeScaleForState(_gameStateData.CurrentState);
    }

    private void OnEnable()
    {
        _perkUI.OnPerkSelectConfirmedEvent.AddListener(HandlePerkSelectConfirmed);
        _playerController.OnPlayerDiedEvent.AddListener(HandlePlayerDied);
        _waveManager.OnWaveStartedEvent.AddListener(HandleWaveStarted);
        _waveManager.OnWaveClearedEvent.AddListener(HandleWaveCleared);
        _waveManager.OnEnemySpawnedEvent.AddListener(HandleEnemySpawned);
        _waveManager.OnEnemyDiedEvent.AddListener(HandleEnemyDied);
    }

    private void OnDisable()
    {
        _perkUI.OnPerkSelectConfirmedEvent.RemoveListener(HandlePerkSelectConfirmed);
        _playerController.OnPlayerDiedEvent.RemoveListener(HandlePlayerDied);
        _waveManager.OnWaveStartedEvent.RemoveListener(HandleWaveStarted);
        _waveManager.OnWaveClearedEvent.RemoveListener(HandleWaveCleared);
        _waveManager.OnEnemySpawnedEvent.RemoveListener(HandleEnemySpawned);
        _waveManager.OnEnemyDiedEvent.RemoveListener(HandleEnemyDied);
    }

    private void Start()
    {
        if (_autoStartOnAwake)
        {
            StartRun(1);
        }
    }

    // ===== Public API (GameStateSO 데이터를 조작하고 이벤트 방송) =====

    /// <summary>런 시작. waveStart부터 진행.</summary>
    public void StartRun(int waveStart = 1)
    {
        _gameStateData.IsRunActive = true;
        _gameOverTriggered = false;
        SetWave(waveStart);
        SetEnemiesAlive(0); // 적 수 초기화
        _sessionStartTime = Time.time;
        ChangeState(GameStateSO.E_GamePlayState.Playing);

        OnRunStartedEvent.Raise();
    }

    /// <summary>웨이브 번호 갱신(외부 WaveManager에서 호출)</summary>
    public void SetWave(int wave)
    {
        if (wave < 0) wave = 0;
        _gameStateData.CurrentWave = wave;
    }

    /// <summary>잔여 적수 강제 설정(스폰 직후 등)</summary>
    public void SetEnemiesAlive(int count)
    {
        if (count < 0) count = 0;
        _gameStateData.EnemiesAlive = count;
    }

    /// <summary>Perk 선택 화면 열기(웨이브 클리어 시 호출)</summary>
    public void OpenPerkSelect()
    {
        if (_gameStateData.CurrentState == GameStateSO.E_GamePlayState.GameOver) return; // 게임오버 보호

        ChangeState(GameStateSO.E_GamePlayState.PerkSelect);
        OnPerkSelectOpenedEvent.Raise();
    }

    /// <summary>Perk 선택 확정 → Playing 복귀</summary>
    public void HandlePerkSelectConfirmed()
    {
        ChangeState(GameStateSO.E_GamePlayState.Playing);
    }

    /// <summary>일시정지/해제</summary>
    public void SetPaused(bool pause)
    {
        if (_gameStateData.CurrentState == GameStateSO.E_GamePlayState.GameOver) return;
        ChangeState(pause ? GameStateSO.E_GamePlayState.Paused : GameStateSO.E_GamePlayState.Playing);
    }

    /// <summary>게임오버 처리</summary>
    public void TriggerGameOver(string reason = "PlayerDied")
    {
        if (_gameOverTriggered) return;
        if (_gameStateData.CurrentState == GameStateSO.E_GamePlayState.GameOver) return;

        _gameOverTriggered = true;

        float survived = Mathf.Max(0f, Time.time - _sessionStartTime);

        // Load bests
        int bestWave = PlayerPrefs.GetInt(_PP_BestWave, 0);
        float bestTime = PlayerPrefs.GetFloat(_PP_BestTime, 0);

        bool newBestWave = _gameStateData.CurrentWave > bestWave;
        bool newBestTime = survived > bestTime;

        if (newBestWave)
        {
            bestWave = _gameStateData.CurrentWave;
            PlayerPrefs.SetInt(_PP_BestWave, bestWave);
        }
        if (newBestTime)
        {
            bestTime = survived;
            PlayerPrefs.SetFloat(_PP_BestTime, bestTime);
        }
        PlayerPrefs.Save();

        var info = new GameOverInfo
        {
            wave = _gameStateData.CurrentWave,
            survivedSeconds = survived,
            reason = reason,
            bestWave = bestWave,
            bestSeconds = bestTime,
            isNewBestWave = newBestWave,
            isNewBestTime = newBestTime,
        };

        ChangeState(GameStateSO.E_GamePlayState.GameOver);
        OnGameOverEvent.Raise(info);   // 게임오버 전용 이벤트 방송

        _gameStateData.IsRunActive = false;
        OnRunEndedEvent.Raise();   // 런 종료 이벤트 방송
    }

    /// <summary>씬 리로드 기반 재시작. (필요 시 외부에서 UI 버튼으로 연결)</summary>
    public void RestartRun(bool reloadScene = true)
    {
        if (reloadScene)
        {
            var idx = SceneManager.GetActiveScene().buildIndex;
            SceneManager.LoadScene(idx);
            return;
        }

        // 씬 리로드 없이 런만 리셋하고 싶다면 외부 시스템(Wave/Enemies/Player) 초기화 후:
        StartRun(1);
    }

    public bool IsPlayable()
    {
        return _gameStateData.IsPlayable();
    }

    public bool IsInputLocked()
    {
        return _gameStateData.IsInputLocked;
    }

    public GameStateSO.E_GamePlayState CurrentState()
    {
        return _gameStateData.CurrentState;
    }

    public GameStateSO.E_GamePlayState PreviousState()
    {
        return _gameStateData.PreviousState;
    }

    // ===== Event Handler =====
    private void HandlePlayerDied()
    {
        TriggerGameOver();
    }

    private void HandleWaveStarted(int wave)
    {
        SetWave(wave);
    }

    private void HandleWaveCleared(int wave)
    {
        OpenPerkSelect();
    }

    private void HandleEnemySpawned(int alive)
    {
        SetEnemiesAlive(alive);
    }

    private void HandleEnemyDied(int alive)
    {
        SetEnemiesAlive(alive);
    }

    // ===== Internal Logic =====
    private void ChangeState(GameStateSO.E_GamePlayState next)
    {
        if (_gameStateData.CurrentState == next) return;

        _gameStateData.PreviousState = _gameStateData.CurrentState;
        _gameStateData.CurrentState = next;

        // 입력 잠금 규칙 업데이트
        _gameStateData.IsInputLocked = (_gameStateData.CurrentState == GameStateSO.E_GamePlayState.PerkSelect) ||
                        (_gameStateData.CurrentState == GameStateSO.E_GamePlayState.Paused) ||
                        (_gameStateData.CurrentState == GameStateSO.E_GamePlayState.GameOver);

        // TimeScale 적용
        ApplyTimeScaleForState(_gameStateData.CurrentState);

        // 상태 변경 이벤트 방송
        OnStateChangedEvent.Raise(_gameStateData.PreviousState, _gameStateData.CurrentState);

#if UNITY_EDITOR
        // 디버깅 로그
        Debug.Log($"[GameState] {_gameStateData.PreviousState}" +
            $" → {_gameStateData.CurrentState}" +
            $" | Wave:{_gameStateData.CurrentWave}" +
            $" | Alive:{_gameStateData.EnemiesAlive}");
#endif
    }

    private void ApplyTimeScaleForState(GameStateSO.E_GamePlayState s)
    {
        if (!_useTimeScalePause) return;

        switch (s)
        {
            case GameStateSO.E_GamePlayState.PerkSelect:
            case GameStateSO.E_GamePlayState.Paused:
            case GameStateSO.E_GamePlayState.GameOver:
                Time.timeScale = _pausedTimeScale;
                break;
            default:
                Time.timeScale = _playingTimeScale;
                break;
        }
    }

    // ===== Dev Hotkeys =====
    private void Update()
    {
        if (!_enableDevHotkeys) return;

        if (Input.GetKeyDown(_keyStartRun))
        {
            if (!_gameStateData.IsRunActive) StartRun(1);
        }
        if (Input.GetKeyDown(_keyOpenPerk))
        {
            OpenPerkSelect();
        }
        if (Input.GetKeyDown(_keyGameOver))
        {
            TriggerGameOver();
        }
        if (Input.GetKeyDown(_keyRestart))
        {
            if (_gameStateData.CurrentState == GameStateSO.E_GamePlayState.GameOver || Application.isEditor)
                RestartRun(true);
        }
    }
}
