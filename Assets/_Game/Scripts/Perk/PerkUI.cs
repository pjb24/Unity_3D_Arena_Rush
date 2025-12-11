/// <summary>
/// Arena Rush – Perk 선택 UI (GameState 연동)
/// 
/// 웨이브 종료 시 퍼크 3종을 제시하고 선택을 브로드캐스트.
/// 실제 스탯 반영은 다른 시스템이 OnPerkAppliedEvent를 구독해 처리.
/// 
/// - GameState.OnPerkSelectOpenedEvent 로 오픈
/// - GameState.OnStateChangedEvent 로 상태 이탈 시 자동 닫힘
/// - OnPerkSelectConfirmedEvent 브로드캐스트 → GameState.HandlePerkSelectConfirmed()
/// - Time.timeScale 제어는 GameState가 수행 (UI는 패널 표시만)
/// </summary>

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
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
    [Tooltip("같은 세션에서 동일 퍼크 완전 배제(스택 불허)")]
    [SerializeField] private bool _preventExactDuplicate = false;

    [Header("ScriptableObject Events")]
    public GameEventSO OnPerkSelectConfirmedEvent;    // Perk 선택 확정 이벤트
    public GameEventSO_Perk OnPerkAppliedEvent;    // Perk 선택 효과 적용 이벤트

    private readonly System.Random _rng = new System.Random();
    private readonly List<Perk> _sessionTaken = new List<Perk>(32);
    private readonly Perk[] _current = new Perk[3];

    private GameState _gs;

    private void Awake()
    {
        _gs = FindAnyObjectByType<GameState>();

        // 패널 비활성
        if (_panel != null)
        {
            _panel.alpha = 0f;
            _panel.interactable = false;
            _panel.blocksRaycasts = false;
        }

        // 버튼 리스너
        for (int i = 0; i < _optionButtons.Length; i++)
        {
            var idx = i;
            if (_optionButtons[i] != null)
            {
                _optionButtons[i].onClick.AddListener(() => Select(idx));
            }
        }
    }

    private void OnEnable()
    {
        if (_gs != null)
        {
            // PerkSelect 진입 시 열기
            _gs.OnPerkSelectOpenedEvent.AddListener(HandlePerkSelectOpened);
            // 상태 변화 감시: PerkSelect 벗어나면 닫기(Playing/GameOver/Paused 등)
            _gs.OnStateChangedEvent.AddListener(HandleStateChanged);
        }
    }

    private void OnDisable()
    {
        if (_gs != null)
        {
            // PerkSelect 진입 시 열기
            _gs.OnPerkSelectOpenedEvent.RemoveListener(HandlePerkSelectOpened);
            // 상태 변화 감시: PerkSelect 벗어나면 닫기(Playing/GameOver/Paused 등)
            _gs.OnStateChangedEvent.RemoveListener(HandleStateChanged);
        }
    }

    // === GameState Hooks ===
    private void HandlePerkSelectOpened()
    {
        Open();
    }

    private void HandleStateChanged(GameStateSO.E_GamePlayState prev, GameStateSO.E_GamePlayState current)
    {
        if (current != GameStateSO.E_GamePlayState.PerkSelect)
            SetPanel(false);
    }

    /// <summary>
    /// 외부에서 호출: 웨이브 종료 등 트리거에 바인딩.
    /// </summary>
    private void Open()
    {
        if (_allPerks == null || _allPerks.Length == 0)
        {
            Debug.LogWarning("[PerkUI] _allPerks 비어 있음.");
            return;
        }

        RollOptions();
        BindUI();

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
        if (OnPerkAppliedEvent != null)
        {
            OnPerkAppliedEvent.Raise(picked);
        }

        if (OnPerkSelectConfirmedEvent != null)
        {
            OnPerkSelectConfirmedEvent.Raise();
        }
        else
        {
            SetPanel(false); // 안전장치
        }
    }

    private void SetPanel(bool show)
    {
        if (_panel == null) return;

        _panel.alpha = show ? 1f : 0f;
        _panel.interactable = show;
        _panel.blocksRaycasts = show;
    }

    private void RollOptions()
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

    private void BindUI()
    {
        for (int i = 0; i < _current.Length; i++)
        {
            var p = _current[i];
            if (_optionTitles != null && i < _optionTitles.Length && _optionTitles[i] != null)
            {
                _optionTitles[i].text = p != null ? p.DisplayName : "-";
            }

            if (_optionDescs != null && i < _optionDescs.Length && _optionDescs[i] != null)
            {
                // 상세 설명 없으면 자동 요약
                var desc = (p != null && !string.IsNullOrWhiteSpace(p.Description))
                    ? p.Description
                    : (p != null ? p.BuildCompactSummary() : "");
                _optionDescs[i].text = desc;
            }

            if (_optionIcons != null && i < _optionIcons.Length && _optionIcons[i] != null)
            {
                _optionIcons[i].sprite = p != null ? p.Icon : null;
                _optionIcons[i].enabled = p != null && p.Icon != null;
            }

            if (_optionButtons != null && i < _optionButtons.Length && _optionButtons[i] != null)
            {
                _optionButtons[i].interactable = p != null;
            }
        }
    }
}
