using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;

[DisallowMultipleComponent]
public sealed class WebGLPointerLockAndFakeCursor : MonoBehaviour
{
    public enum E_Mode
    {
        AimLocked = 0,   // 조준/회전: Pointer Lock, OS 커서 숨김
        UiFree = 1       // UI 조작: Unlock, Fake 커서 표시 + VirtualMouse로 UI 포인팅
    }

    [Header("Refs")]
    [SerializeField] private InputSystemUIInputModule _uiModule; // 씬의 EventSystem에 붙어있음

    [Header("Mode")]
    [SerializeField] private E_Mode _startMode = E_Mode.AimLocked;
    [SerializeField] private bool _lockOnLeftClick = true;
    [SerializeField] private bool _dontLockWhenPointerOverUI = true;

    [Header("Keys")]
    [SerializeField] private KeyCode _keyUnlock = KeyCode.Escape; // ESC로 무조건 UI 모드
    [SerializeField] private bool _allowRelockByClick = true;     // UI 모드에서 클릭으로 재잠금

    [Header("Fake Cursor")]
    [SerializeField] private RectTransform _fakeCursor;          // Screen Space Overlay Canvas 내
    [SerializeField] private Canvas _fakeCursorCanvas;           // (선택) 커서만 별도 캔버스면 연결
    [SerializeField] private bool _hideSystemCursorInUiMode = true;
    [SerializeField] private float _uiSensitivity = 1.0f;

    [Header("Runtime")]
    [SerializeField] private E_Mode _mode;

    private Mouse _virtualMouse;
    private bool _virtualActive = false;

    // Virtual mouse state cache
    private Vector2 _virtualPos;
    private bool _virtualLeftDown;

    private void Awake()
    {
        if (_uiModule == null) _uiModule = FindAnyObjectByType<InputSystemUIInputModule>();
    }

    private void OnEnable()
    {
        _mode = _startMode;

        EnsureVirtualMouse();
        ApplyMode(_mode);
    }

    private void OnDisable()
    {
        if (_virtualMouse != null)
        {
            InputSystem.RemoveDevice(_virtualMouse);
            _virtualMouse = null;
        }
    }

    private void Update()
    {
        // ESC: 언제든 UI 모드(잠금 해제 + Fake 커서)
        if (Input.GetKeyDown(_keyUnlock))
            EnterUiFree();

        // UI 모드에서 Fake 커서 이동 + 가상 마우스 입력 주입
        if (_mode == E_Mode.UiFree)
        {
            // 클릭으로 재잠금(유저 제스처 필요)
            if (_allowRelockByClick && _lockOnLeftClick && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (_dontLockWhenPointerOverUI && IsPointerOverUI())
                    return;

                EnterAimLocked();
            }
        }

        UpdateFakeCursorAndVirtualMouse();
    }

    // ===== Public API (GameState/PerkUI에서 호출) =====
    public E_Mode CurrentMode => _mode;

    public void EnterAimLocked() => SetMode(E_Mode.AimLocked);
    public void EnterUiFree() => SetMode(E_Mode.UiFree);

    public void SetMode(E_Mode next)
    {
        if (_mode == next) return;
        _mode = next;
        ApplyMode(_mode);
    }

    // ===== Internal =====
    private void ApplyMode(E_Mode m)
    {
        if (m == E_Mode.AimLocked)
        {
            // WebGL에서만 진짜 Pointer Lock 의미가 있음
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            SetFakeCursorVisible(false);
            _virtualActive = false;

            _virtualLeftDown = false;
            QueueVirtualMouseState(_virtualPos, _virtualLeftDown);
        }
        else // UiFree
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = !_hideSystemCursorInUiMode;

            SetFakeCursorVisible(true);
            _virtualActive = true;

            // 시작 위치 동기화
            if (_fakeCursor != null)
            {
                _virtualPos = ClampToScreen(_fakeCursor.position);
                _fakeCursor.position = _virtualPos;
            }
            else
            {
                _virtualPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }

            _virtualLeftDown = false;
            QueueVirtualMouseState(_virtualPos, _virtualLeftDown);
        }
    }

    private void EnsureVirtualMouse()
    {
        if (_virtualMouse != null) return;

        _virtualMouse = (Mouse)InputSystem.AddDevice("VirtualMouse");

        _virtualPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        _virtualLeftDown = false;

        QueueVirtualMouseState(_virtualPos, _virtualLeftDown);
    }

    private void UpdateFakeCursorAndVirtualMouse()
    {
        if (!_virtualActive) return;
        if (_fakeCursor == null) return;
        if (Mouse.current == null) return;

        // Fake 커서 이동 (실마우스 델타로 이동)
        Vector2 delta = Mouse.current.delta.ReadValue() * _uiSensitivity;

        _virtualPos += delta;
        _virtualPos.x = Mathf.Clamp(_virtualPos.x, 0f, Screen.width);
        _virtualPos.y = Mathf.Clamp(_virtualPos.y, 0f, Screen.height);

        _fakeCursor.position = _virtualPos;

        // 버튼 상태는 실제 마우스 입력을 따라가게
        _virtualLeftDown = Mouse.current.leftButton.isPressed;

        QueueVirtualMouseState(_virtualPos, _virtualLeftDown);
    }

    private void QueueVirtualMouseState(Vector2 pos, bool leftDown)
    {
        if (_virtualMouse == null) return;

        var state = new MouseState
        {
            position = pos,
            delta = Vector2.zero,
            buttons = leftDown ? (ushort)(1 << (int)MouseButton.Left) : (ushort)0 // 1 = left button bit
        };

        InputSystem.QueueStateEvent(_virtualMouse, state);
        InputSystem.Update(); // UI 즉시 반영(프레임 지연 방지)
    }

    private Vector3 ClampToScreen(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, 0f, Screen.width);
        p.y = Mathf.Clamp(p.y, 0f, Screen.height);
        return p;
    }

    private void SetFakeCursorVisible(bool visible)
    {
        if (_fakeCursor == null) return;

        if (_fakeCursorCanvas != null)
            _fakeCursorCanvas.enabled = visible;

        _fakeCursor.gameObject.SetActive(visible);
    }

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }
}
