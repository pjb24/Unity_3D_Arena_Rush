using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 웨이브 종료 시 퍼크 3종을 제시하고 선택을 브로드캐스트.
/// 실제 스탯 반영은 다른 시스템이 OnPerkApplied를 구독해 처리.
/// </summary>
public class PerkUI : MonoBehaviour
{
    [Header("Database")]
    [Tooltip("뽑기 풀(전체 Perk). 중복 방지 로직은 내부에서 처리.")]
    [SerializeField] private Perk[] _allPerks;

    [Header("UI References")]
    [SerializeField] private CanvasGroup _panel;
    [SerializeField] private Button[] _optionButtons = new Button[3];
    [SerializeField] private Image[] _optionIcons = new Image[3];
    [SerializeField] private TextMeshProUGUI[] _optionTitles = new TextMeshProUGUI[3];
    [SerializeField] private TextMeshProUGUI[] _optionDescs = new TextMeshProUGUI[3];

    [Header("Behavior")]
    [Tooltip("표시 중 게임 일시정지")]
    [SerializeField] private bool _pauseOnOpen = true;

    [Tooltip("같은 세션에서 동일 퍼크 완전 배제(스택 불허)")]
    [SerializeField] private bool _preventExactDuplicate = false;

    [Tooltip("웨이브 종료 이벤트 구독용")]
    [SerializeField] private WaveManager _waveManager;

    private event Action<Perk> _onPerkApplied;
    public void AddPerkAppliedListener(Action<Perk> listener) => _onPerkApplied += listener;
    public void RemovePerkAppliedListener(Action<Perk> listener) => _onPerkApplied -= listener;

    private readonly System.Random _rng = new System.Random();
    private readonly List<Perk> _sessionTaken = new List<Perk>(32);
    private readonly Perk[] _current = new Perk[3];
    private float _prevTimeScale = 1f;

    private void Awake()
    {
        if (_panel != null)
        {
            _panel.alpha = 0f;
            _panel.interactable = false;
            _panel.blocksRaycasts = false;
        }

        for (int i = 0; i < _optionButtons.Length; i++)
        {
            var idx = i;
            _optionButtons[i].onClick.AddListener(() => Select(idx));
        }

        _waveManager.AddWaveClearedListener(Open);
    }

    public void Open(int waveIndex = -1)
    {
        Open();
    }

    /// <summary>
    /// 외부에서 호출: 웨이브 종료 등 트리거에 바인딩.
    /// </summary>
    public void Open()
    {
        if (_allPerks == null || _allPerks.Length == 0)
        {
            Debug.LogWarning("[PerkUI] _allPerks 비어 있음.");
            return;
        }

        RollOptions();
        BindUI();

        if (_pauseOnOpen)
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        SetPanel(true);
    }

    /// <summary>
    /// 디버그 단축키(선택). 필요시 Update에 매핑.
    /// </summary>
    public void DebugSelect(int index) => Select(index);

    private void Select(int index)
    {
        if (index < 0 || index >= _current.Length) return;

        var picked = _current[index];
        if (picked == null) return;

        // 세션 중복 처리 정책
        if (_preventExactDuplicate)
        {
            _sessionTaken.Add(picked);
        }

        // 브로드캐스트: 실제 적용은 외부 시스템이 처리
        try
        {
            _onPerkApplied?.Invoke(picked);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }

        Close();

        _waveManager.SetSelectPerk();
    }

    void Close()
    {
        SetPanel(false);
        if (_pauseOnOpen)
        {
            Time.timeScale = _prevTimeScale;
        }
    }

    void SetPanel(bool show)
    {
        if (_panel == null) return;

        _panel.alpha = show ? 1f : 0f;
        _panel.interactable = show;
        _panel.blocksRaycasts = show;
    }

    void RollOptions()
    {
        var used = new HashSet<int>();
        int safety = 64;

        for (int i = 0; i < _current.Length; i++)
        {
            Perk pick = null;
            while (safety-- > 0)
            {
                int idx = _rng.Next(0, _allPerks.Length);
                if (used.Contains(idx)) continue;

                var candidate = _allPerks[idx];
                if (_preventExactDuplicate && _sessionTaken.Contains(candidate))
                    continue;

                pick = candidate;
                used.Add(idx);
                break;
            }
            _current[i] = pick ?? _allPerks[_rng.Next(0, _allPerks.Length)];
        }
    }

    void BindUI()
    {
        for (int i = 0; i < _current.Length; i++)
        {
            var p = _current[i];
            if (_optionTitles != null && i < _optionTitles.Length)
            {
                _optionTitles[i].text = p != null ? p.DisplayName : "-";
            }

            if (_optionDescs != null && i < _optionDescs.Length)
            {
                // 상세 설명 없으면 자동 요약
                var desc = !string.IsNullOrWhiteSpace(p.Description) ? p.Description : p.BuildCompactSummary();
                _optionDescs[i].text = desc;
            }

            if (_optionIcons != null && i < _optionIcons.Length)
            {
                _optionIcons[i].sprite = p != null ? p.Icon : null;
                _optionIcons[i].enabled = p != null && p.Icon != null;
            }

            if (_optionButtons != null && i < _optionButtons.Length)
            {
                _optionButtons[i].interactable = p != null;
            }
        }
    }
}
