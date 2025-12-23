using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

[DisallowMultipleComponent]
public sealed class WebGLPointerLock : MonoBehaviour
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

    [Header("Runtime")]
    [SerializeField] private E_Mode _mode;

    private void Awake()
    {
        if (_uiModule == null) _uiModule = FindAnyObjectByType<InputSystemUIInputModule>();
    }

    private void OnEnable()
    {
        _mode = _startMode;

        ApplyMode(_mode);
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

        }
        else // UiFree
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

        }
    }

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }
}
