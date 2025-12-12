// Scripts/UI/HUDController.cs
// HUD 갱신: HP Bar, Wave/Alive, FireRate/Reload
// 의존: UnityEngine.UI(Image), TextMeshPro(TMP_Text), Health, GunInfo, WaveInfo

using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class HUDController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _playerRoot;
    [SerializeField] private MonoBehaviour _waveInfoSource; // IWaveInfo

    [SerializeField] private Image _hpFill;                 // type: Filled, Fill Method: Horizontal
    [SerializeField] private TMP_Text _hpText;

    [SerializeField] private TMP_Text _waveText;            // "Wave 3 / Left 12"

    [SerializeField] private TMP_Text _gunText;             // "FR 6.0 / Reload 0.8s" or "FR 6.0 / Ready"

    [SerializeField] private TMP_Text _dashText;            // 텍스트 형태 사용
    [SerializeField] private Image _dashFill;               // 게이지 사용

    // runtime
    private IHealthInfo _health;
    private IGunInfo _gun;
    private IWaveInfo _wave;
    private IDashInfo _dash;

    private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(64);

    private void Awake()
    {
        if (_playerRoot == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            _playerRoot = player != null ? player.transform : null;
        }

        // Health
        if (_playerRoot != null)
        {
            _health = _playerRoot.GetComponent<IHealthInfo>();
            if (_health == null)
            {
                // Health 컴포넌트만 있을 경우 어댑터 부착
                var h = _playerRoot.GetComponent<Health>();
                if (h != null) _health = HUD_Adapter_Health.Attach(h);
            }

            _gun = _playerRoot.GetComponentInChildren<IGunInfo>();
            if (_gun == null)
            {
                var gun = _playerRoot.GetComponentInChildren<Gun>();
                if (gun != null) _gun = HUD_Adapter_Gun.Attach(gun);
            }
        }

        // Wave
        _wave = (_waveInfoSource as IWaveInfo);
        if (_wave == null && _waveInfoSource != null)
        {
            // 다른 컴포넌트에서 IWaveInfo 어댑트
            var wm = (_waveInfoSource as WaveManager);
            if (wm != null) _wave = HUD_Adapter_Wave.Attach(wm);
        }
        if (_wave == null)
        {
            var wm = FindAnyObjectByType<WaveManager>();
            if (wm != null) _wave = HUD_Adapter_Wave.Attach(wm);
        }

        // Dash
        _dash = _playerRoot.GetComponentInChildren<IDashInfo>();
        if (_dash == null)
        {
            var dash = _playerRoot.GetComponentInChildren<Dash>();
            if (dash != null) _dash = HUD_Adapter_Dash.Attach(dash);
        }
    }

    private void Update()
    {
        UpdateHP();
        UpdateWave();
        UpdateGun();
        UpdateDash();
    }

    private void UpdateHP()
    {
        if (_health == null || _hpFill == null) return;

        int max = Mathf.Max(1, _health.MaxHP);
        int cur = Mathf.Clamp(_health.CurrentHP, 0, max);
        _hpFill.fillAmount = (float)cur / (float)max;

        if (_hpText != null)
        {
            _sb.Clear();
            _sb.Append("HP ").Append(cur).Append(" / ").Append(max);
            _hpText.text = _sb.ToString();
        }
    }

    private void UpdateWave()
    {
        if (_wave == null || _waveText == null) return;

        _sb.Clear();
        _sb.Append("Wave ").Append(_wave.CurrentWave)
           .Append("  /  Left ").Append(Mathf.Max(0, _wave.AliveEnemies));
        _waveText.text = _sb.ToString();
    }

    private void UpdateGun()
    {
        if (_gunText == null || _gun == null) return;

        _sb.Clear();
        // FireRate: 초당 발사수(또는 연사속도)
        _sb.Append("FireRate ").Append(_gun.FireRate.ToString("0.0")).Append("  /  ");

        if (_gun.IsReloading)
        {
            _sb.Append("Reload ").Append(Mathf.Max(0f, _gun.ReloadRemaining).ToString("0.0")).Append("s");
        }
        else
        {
            _sb.Append("Ready");
        }

        _gunText.text = _sb.ToString();
    }

    private void UpdateDash()
    {
        if (_dash == null) return;

        // 텍스트형
        if (_dashText != null)
        {
            if (_dash.IsCooldown)
                _dashText.text = $"Dash {_dash.CooldownRemaining:0.0}s";
            else
                _dashText.text = "Dash Ready";
        }

        // 원형 게이지
        if (_dashFill != null)
        {
            _dashFill.fillAmount = _dash.IsCooldown ? _dash.CooldownPercent : 0f; // 남은 비율
            _dashFill.color = _dash.IsCooldown ? new Color(1f, 0.7f, 0.2f) : new Color(0.7f, 0.7f, 0.7f);
        }
    }
}
