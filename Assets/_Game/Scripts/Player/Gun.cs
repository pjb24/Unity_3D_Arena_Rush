// Gun.cs
// 히트스캔(레이캐스트) 기반 사격 시스템 통합 구현
// - GameState.IsPlayable()/IsInputLocked()에 따라 입력/발사/재장전 차단
//
// - Dash 동작 수행 중에는 Fire 불가(입력/외부 호출 모두 차단)
//
// - 자동/단발 모드, 연사/쿨다운, 산탄(스프레드), 탄환 수(옵션), 재장전(옵션)
// - 조준: "Muzzle → 마우스 월드 포인트(바닥 히트)" 방향
// - 인게임 표시: LineRenderer로 사격 거리만큼 붉은 라인 상시 업데이트(마우스 방향)
// - 히트 시 Health.TakeDamage() 호출, 이펙트/사운드 훅 제공
// 의존: Input System(선택), Health.cs, (선택) Pooler.cs

using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[DisallowMultipleComponent]
public class Gun : MonoBehaviour
{
    public enum E_FireMode { SemiAuto, FullAuto }

    [Header("Fire")]
    [SerializeField] private E_FireMode _fireMode = E_FireMode.FullAuto;
    [SerializeField, Range(1f, 30f)] private float _fireRate = 8f;        // 초당 발사 수(RPS)
    [SerializeField, Range(0f, 15f)] private float _spreadDegrees = 1.5f; // 산탄
    [SerializeField, Range(1, 20)] private int _shotsPerFire = 1;         // 샷건성 멀티 샷
    [SerializeField, Range(5f, 150f)] private float _range = 60f;
    [SerializeField] private int _damage = 10;
    [SerializeField] private LayerMask _hitMask = ~0;

    [Header("Ammo (Optional)")]
    [SerializeField] private int _magazineSize = 0;        // 0 이면 무한
    [SerializeField] private float _reloadTime = 1.2f;     // 재장전 시간
    [SerializeField] private bool _autoReload = true;

    [Header("Refs")]
    [SerializeField] private Transform _muzzle;            // 총구 위치(없으면 transform 사용)
    [SerializeField] private ParticleSystem _muzzleFlash;  // 총구 이펙트(옵션)
    [SerializeField] private GameObject _hitFxPrefab;      // 히트 스파크(옵션)
    [SerializeField] private AudioSource _audioSource;     // 사운드(옵션)
    [SerializeField] private AudioClip _fireSfx;           // 발사음(옵션)
    [SerializeField] private AudioClip _reloadSfx;         // 재장전(옵션)

    [Header("Aim Provider")]
    [SerializeField] private CrosshairAim _aim;

    [Header("Input (Optional)")]
    public InputActionReference _fireAction;    // Button
    public InputActionReference _reloadAction;  // Button

    [Header("Dash Lock")]
    [SerializeField] private bool _blockFireWhileDashing = true;
    [SerializeField] private Dash _dash; // 미지정 시 Awake에서 자동 탐색(권장: Player에 붙어있다고 가정)

    // runtime
    private bool _isFirePressed;
    public bool IsFirePressed => _isFirePressed;
    private float _nextFireTime;
    private bool _triggerReleased = true;     // 단발 트리거 제어
    private int _ammo;                        // 현재 탄 수
    private bool _reloading;
    private float _reloadEndTime;             // 재장전 종료 시각

    private GameState _gs;
    private PlayerController _playerController;

    // 상태 조회
    public bool IsReloading => _reloading;
    public int CurrentAmmo => _magazineSize > 0 ? _ammo : -1;
    public int MagazineSize => _magazineSize;
    public int Damage { get { return _damage; } set { _damage = value; } }
    public float FireRate { get { return _fireRate; } set { _fireRate = value; } }

    // 남은 재장전 시간(s)
    public float ReloadRemaining => _reloading ? Mathf.Max(0f, _reloadEndTime - Time.time) : 0f;

    private void Awake()
    {
        _gs = FindAnyObjectByType<GameState>();

        if (_magazineSize > 0) _ammo = _magazineSize;
        if (_muzzle == null) _muzzle = transform;

        if (_aim == null) _aim = FindAnyObjectByType<CrosshairAim>();
        if (_dash == null) _dash = GetComponentInParent<Dash>(); // Player 하위에 Gun이 붙어있는 케이스 대응
        if (_playerController == null) _playerController = GetComponentInParent<PlayerController>();
    }

    private void OnEnable()
    {
        if (_fireAction != null) _fireAction.action.Enable();
        if (_reloadAction != null) _reloadAction.action.Enable();

        if (_gs != null)
        {
            _gs.OnStateChangedEvent.AddListener(OnGameStateChanged);
            OnGameStateChanged(_gs.PreviousState(), _gs.CurrentState());
        }
    }

    private void OnDisable()
    {
        if (_fireAction != null) _fireAction.action.Disable();
        if (_reloadAction != null) _reloadAction.action.Disable();

        if (_gs != null)
        {
            _gs.OnStateChangedEvent.RemoveListener(OnGameStateChanged);
        }
    }

    private void Update()
    {
        bool playable = _gs == null
            || (_gs.IsPlayable() && !_gs.IsInputLocked());
        bool dashLock = _blockFireWhileDashing && _dash != null && _dash.IsDashing;

        // 입력 처리
        if (_fireAction != null)
        {
            // Dash 중에는 입력 자체를 "안 눌린 것"처럼 처리(연사/단발 상태 꼬임 방지)
            _isFirePressed = playable && !dashLock && _fireAction.action.IsPressed();

            if (_fireMode == E_FireMode.FullAuto)
            {
                if (_isFirePressed)
                {
                    if (_playerController)
                    {
                        _playerController.FaceCameraForwardImmediate();
                    }
                    TryFire();
                }
            }
            else // SemiAuto
            {
                if (_isFirePressed && _triggerReleased)
                {
                    if (_playerController)
                    {
                        _playerController.FaceCameraForwardImmediate();
                    }
                    TryFire();
                    _triggerReleased = false;
                }
                if (!_isFirePressed) _triggerReleased = true;
            }
        }

        if (_reloadAction != null && playable)
        {
            bool reloadPressed = _reloadAction.action.IsPressed();

            if (reloadPressed && _magazineSize != _ammo)
            {
                StartReload();
            }
        }
    }

    // 외부에서 발사 요청 시 사용
    public bool TryFire()
    {
        // Dash 중 Fire 차단(외부 호출도 포함)
        if (_blockFireWhileDashing && _dash != null && _dash.IsDashing)
            return false;

        // 상태/재장전/쿨다운/탄약 체크
        if (_gs != null
            && (!_gs.IsPlayable() || _gs.IsInputLocked()))
            return false;
        if (_reloading) return false;

        float t = Time.time;
        float interval = 1f / Mathf.Max(0.01f, _fireRate);
        if (t < _nextFireTime) return false;

        if (_magazineSize > 0 && _ammo <= 0)
        {
            if (_autoReload) StartReload();
            return false;
        }

        _nextFireTime = t + interval;

        if (_magazineSize > 0) _ammo = Mathf.Max(0, _ammo - 1);

        // 발사 처리
        FireBurst(_shotsPerFire);

        // 피드백
        PlayMuzzleFx();
        PlayAudio(_fireSfx);

        // 자동재장전 체크
        if (_magazineSize > 0 && _ammo == 0 && _autoReload) StartReload();

        return true;
    }

    public void StartReload()
    {
        if (_gs != null
            && (!_gs.IsPlayable() || _gs.IsInputLocked()))
            return;
        if (_magazineSize <= 0 || _reloading || _ammo == _magazineSize) return;
        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        _reloading = true;
        _reloadEndTime = Time.time + _reloadTime; // 종료 시각 기록
        PlayAudio(_reloadSfx);
        yield return new WaitForSeconds(_reloadTime);
        _ammo = _magazineSize;
        _reloading = false;
    }

    private void FireBurst(int count)
    {
        // 조준 벡터 확인, 항상 forward
        Vector3 baseDir = GetFireDirection_Crosshair();

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = ApplySpread(baseDir, _spreadDegrees);
            Ray ray = new Ray(_muzzle.position, dir);

            if (Physics.Raycast(ray, out RaycastHit hit, _range, _hitMask, QueryTriggerInteraction.Ignore))
            {
                OnHit(hit, dir);
            }
        }
    }

    // 공격 방향 = 크로스헤어(카메라 중앙)
    // Ray 방향 = Muzzle -> CrosshairWorldPoint
    private Vector3 GetFireDirection_Crosshair()
    {
        Vector3 start = _muzzle != null ? _muzzle.position : transform.position;

        if (_aim != null && _aim.TryGetCrosshairWorldPoint(start.y, out Vector3 worldPoint))
        {
            Vector3 dir = worldPoint - start;
            if (dir.sqrMagnitude > 1e-4f) return dir.normalized;
        }

        // fallback: muzzle forward
        if (_aim != null && _aim.Camera != null)
        {
            Vector3 f = _aim.Camera.transform.forward;
            if (f.sqrMagnitude > 1e-4f) return f.normalized;
        }

        return _muzzle.forward.normalized;
    }

    private Vector3 ApplySpread(Vector3 dir, float degrees)
    {
        if (degrees <= 0f) return dir;

        // dir을 중심으로 랜덤 원뿔 샘플
        Quaternion q = Random.rotationUniform;
        Vector3 rand = q * Vector3.forward; // 임의 단위 벡터

        // dir과 rand 사이 보간 각도를 스프레드 내 작은 값으로 제한
        float angle = Random.Range(0f, degrees);
        return Vector3.Slerp(dir, rand, angle / 180f).normalized;
    }

    private void OnHit(RaycastHit hit, Vector3 dir)
    {
        // 대미지 적용
        var h = hit.collider.GetComponentInParent<Health>();
        if (h != null)
        {
            DamageInfo info = new();
            info.amount = _damage;
            info.attacker = gameObject;
            info.type = E_DamageType.Bullet;
            info.knockback = 5;

            h.TakeDamage(info);
        }

        // 이펙트
        if (_hitFxPrefab != null)
        {
            SpawnFx(_hitFxPrefab, hit.point, Quaternion.LookRotation(hit.normal));
        }
    }

    private void PlayMuzzleFx()
    {
        if (_muzzleFlash == null) return;
        _muzzleFlash.transform.position = _muzzle.position;
        _muzzleFlash.transform.rotation = _muzzle.rotation;
        _muzzleFlash.Play(true);
    }

    private void PlayAudio(AudioClip clip)
    {
        if (_audioSource == null || clip == null) return;
        _audioSource.PlayOneShot(clip);
    }

    private void SpawnFx(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        var go = Pooler.Instance.Spawn(prefab, pos, rot);
        // fx가 자체 파괴 타이머를 가진다고 가정
    }

    // ===== GameState Hooks =====
    private void OnGameStateChanged(GameStateSO.E_GamePlayState prev, GameStateSO.E_GamePlayState current)
    {
        // 퍼크 선택/일시정지/게임오버 진입 시 트리거/연사 상태 초기화
        if (current != GameStateSO.E_GamePlayState.Playing)
        {
            _isFirePressed = false;
            _triggerReleased = true;
        }
    }
}
