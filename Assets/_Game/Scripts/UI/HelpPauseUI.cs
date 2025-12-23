using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class HelpPauseUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Canvas _canvas;
    [SerializeField] private GameObject _root;      // Dim+Window 루트
    [SerializeField] private Button _startButton;
    [SerializeField] private Button _resumeButton;

    [Header("Input (Optional)")]
    [SerializeField] private InputActionReference _toggleHelpAction; // 예: "UI/Help" (H or Esc)

    private GameState _gs;

    private void Reset()
    {
        _canvas = GetComponentInChildren<Canvas>(true);
    }

    private void Awake()
    {
        if (_gs == null)
        {
            _gs = FindAnyObjectByType<GameState>();
        }

        if (_canvas == null) _canvas = GetComponentInChildren<Canvas>(true);
        if (_root == null && _canvas != null) _root = _canvas.gameObject;

        if (_startButton != null) _startButton.onClick.AddListener(OnClickStart);
        if (_resumeButton != null) _resumeButton.onClick.AddListener(OnClickResume);

        HideImmediate();
    }

    private void OnEnable()
    {
        if (_gs != null)
            _gs.AddListenerStateChanged(OnGameStateChanged);

        if (_toggleHelpAction != null)
        {
            _toggleHelpAction.action.performed += OnToggleHelpPerformed;
            _toggleHelpAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (_gs != null)
            _gs.RemoveListenerStateChanged(OnGameStateChanged);

        if (_toggleHelpAction != null)
        {
            _toggleHelpAction.action.performed -= OnToggleHelpPerformed;
            _toggleHelpAction.action.Disable();
        }
    }

    // ===== Public API (필요 시 다른 UI에서 호출) =====
    public void ShowStart()
    {
        _startButton?.gameObject.SetActive(true);
        _resumeButton?.gameObject.SetActive(false);
        Show();
    }

    public void ShowPause()
    {
        _startButton?.gameObject.SetActive(false);
        _resumeButton?.gameObject.SetActive(true);
        Show();
    }

    // ===== Internals =====
    private void OnGameStateChanged(GameStateSO.E_GamePlayState prevState, GameStateSO.E_GamePlayState curState)
    {
        if (curState == GameStateSO.E_GamePlayState.Boot)
        {
            ShowStart();
            return;
        }

        if (curState == GameStateSO.E_GamePlayState.Playing)
        {
            Hide();
            return;
        }

        if (curState == GameStateSO.E_GamePlayState.Paused)
        {
            ShowPause();
            return;
        }
    }

    private void OnToggleHelpPerformed(InputAction.CallbackContext _)
    {
        // 진행 중이면 Pause/Resume 토글
        if (_gs == null) return;

        if (_gs.CurrentState() == GameStateSO.E_GamePlayState.Playing)
            _gs.PauseGame();
        else if (_gs.CurrentState() == GameStateSO.E_GamePlayState.Paused)
            _gs.ResumeGame();
    }

    private void OnClickStart()
    {
        if (_gs == null) return;
        _gs.StartGame();
    }

    private void OnClickResume()
    {
        if (_gs == null) return;
        _gs.ResumeGame();
    }

    private void Show()
    {
        if (_canvas != null) _canvas.enabled = true;
        if (_root != null) _root.SetActive(true);

        // 마우스 커서 필요 시
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void Hide()
    {
        if (_root != null) _root.SetActive(false);
        if (_canvas != null) _canvas.enabled = false;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.None;
    }

    private void HideImmediate()
    {
        if (_root != null) _root.SetActive(false);
        if (_canvas != null) _canvas.enabled = false;
    }
}
