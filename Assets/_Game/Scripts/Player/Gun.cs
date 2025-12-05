// Gun.cs
// 히트스캔(레이캐스트) 기반 사격 시스템 통합 구현
// - 자동/단발 모드, 연사/쿨다운, 산탄(스프레드), 탄환 수(옵션), 재장전(옵션)
// - 조준: 플레이어(총구) forward 고정
// - 인게임 표시: LineRenderer로 사격 거리만큼 붉은 라인 상시 업데이트
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

    [Header("Aim Line")]
    [SerializeField] private bool _showAimLine = true;
    [SerializeField, Range(0.05f, 0.5f)] private float _aimLineWidth = 0.03f;
    [SerializeField] private Color _aimLineColor = Color.red;

    [Header("Input (Optional)")]
    public InputActionReference _fireAction;       // Button

    // runtime
    private bool _isFirePressed;
    private float _nextFireTime;
    private bool _triggerReleased = true;     // 단발 트리거 제어
    private int _ammo;                        // 현재 탄 수
    private bool _reloading;
    
    private LineRenderer _line;
    private float _prevAimLineWidth;

    // 상태 조회
    public bool IsReloading => _reloading;
    public int CurrentAmmo => _magazineSize > 0 ? _ammo : -1;
    public int MagazineSize => _magazineSize;
    public int Damage => _damage;
    public float FireRate => _fireRate;
    
    private void Awake()
    {
        if (_magazineSize > 0) _ammo = _magazineSize;
        if (_muzzle == null) _muzzle = transform;

        if (_showAimLine) SetupLineRenderer();
    }

    private void OnEnable()
    {
        if (_fireAction != null) _fireAction.action.Enable();
    }

    private void OnDisable()
    {
        if (_fireAction != null) _fireAction.action.Disable();
    }

    private void Update()
    {
        // 입력 처리
        if (_fireAction != null)
        {
            _isFirePressed = _fireAction.action.IsPressed();

            if (_fireMode == E_FireMode.FullAuto)
            {
                if (_isFirePressed) TryFire();
            }
            else // SemiAuto
            {
                if (_isFirePressed && _triggerReleased)
                {
                    TryFire();
                    _triggerReleased = false;
                }
                if (!_isFirePressed) _triggerReleased = true;
            }
        }

        // 사격 라인 상시 갱신
        if (_showAimLine) UpdateAimLine();
    }

    // 외부에서 발사 요청 시 사용
    public bool TryFire()
    {
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
        if (_magazineSize <= 0 || _reloading || _ammo == _magazineSize) return;
        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        _reloading = true;
        PlayAudio(_reloadSfx);
        yield return new WaitForSeconds(_reloadTime);
        _ammo = _magazineSize;
        _reloading = false;
    }

    private void FireBurst(int count)
    {
        // 조준 벡터 확인, 항상 forward
        Vector3 baseDir = GetFireDirection();

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

    // === 조준 = 플레이어(총구) forward ===
    private Vector3 GetFireDirection()
    {
        Vector3 dir = _muzzle.forward;
        dir.y = 0f;                // 탑다운 기준 Y 고정(원하면 주석 처리)
        if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
        return dir.normalized;
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
        // 데미지 적용
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
        //// Pooler가 있으면 사용, 없으면 Instantiate
        //var pooler = FindAnyObjectByType<Pooler>();
        //if (pooler != null)
        //{
        //    var go = pooler.Spawn(prefab, pos, rot);
        //    // fx가 자체 파괴 타이머를 가진다고 가정
        //}
        //else
        //{
        //    var go = Instantiate(prefab, pos, rot);
        //    Destroy(go, 2f);
        //}
    }

    // ===== Aim Line =====
    void SetupLineRenderer()
    {
        _line = gameObject.GetComponent<LineRenderer>();
        if (_line == null) _line = gameObject.AddComponent<LineRenderer>();

        _line.positionCount = 2;
        _line.useWorldSpace = true;
        _line.startWidth = _aimLineWidth;
        _line.endWidth = _aimLineWidth;
        _prevAimLineWidth = _aimLineWidth;

        // 머티리얼(런타임 생성; 프로젝트 정책에 맞춰 교체 가능)
        var shader = Shader.Find("Sprites/Default");
        if (_line.sharedMaterial == null && shader != null)
        {
            var mat = new Material(shader);
            mat.name = "Gun_AimLine_Mat(Runtime)";
            _line.sharedMaterial = mat;
        }

        _line.startColor = _aimLineColor;
        _line.endColor = _aimLineColor;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.enabled = true;
    }

    void UpdateAimLine()
    {
        if (_line == null) return;

        if (_prevAimLineWidth !=  _aimLineWidth)
        {
            _line.startWidth = _aimLineWidth;
            _line.endWidth = _aimLineWidth;
            _prevAimLineWidth = _aimLineWidth;
        }

        Vector3 start = _muzzle != null ? _muzzle.position : transform.position;
        Vector3 dir = GetFireDirection();

        // 히트 여부에 따라 끝점 설정(히트 지점 또는 최대 사거리)
        Ray ray = new Ray(start, dir);
        Vector3 end = start + dir * _range;
        if (Physics.Raycast(ray, out RaycastHit hit, _range, _hitMask, QueryTriggerInteraction.Ignore))
            end = hit.point;

        _line.SetPosition(0, start);
        _line.SetPosition(1, end);
    }
}
