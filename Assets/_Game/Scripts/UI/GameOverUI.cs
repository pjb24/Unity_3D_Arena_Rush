// GameOverUI.cs
// - Game Over 정보 표시 + 버튼 동작(재시작 등)

using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class GameOverUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GameObject _panelRoot;
    [SerializeField] private TextMeshProUGUI _txtTitle;
    [SerializeField] private TextMeshProUGUI _txtWave;
    [SerializeField] private TextMeshProUGUI _txtTime;
    [SerializeField] private TextMeshProUGUI _txtBest;
    [SerializeField] private Button _btnRetry;
    [SerializeField] private Button _btnQuit; // 선택

    private GameState _gs;

    private void Awake()
    {
        _gs = FindAnyObjectByType<GameState>();
    }

    private void OnEnable()
    {
        if (_panelRoot != null) _panelRoot.SetActive(false);
        _gs.OnGameOverEvent.AddListener(OnGameOver);
        if (_btnRetry != null) _btnRetry.onClick.AddListener(HandleRetry);
        if (_btnQuit != null) _btnQuit.onClick.AddListener(HandleQuit);
    }

    private void OnDisable()
    {
        _gs.OnGameOverEvent.RemoveListener(OnGameOver);
        if (_btnRetry != null) _btnRetry.onClick.RemoveListener(HandleRetry);
        if (_btnQuit != null) _btnQuit.onClick.RemoveListener(HandleQuit);
    }

    private void OnGameOver(GameOverInfo info)
    {
        if (_panelRoot != null) _panelRoot.SetActive(true);

        if (_txtTitle != null)
        {
            _txtTitle.text = (info.isNewBestWave || info.isNewBestTime)
                ? "GAME OVER\nNEW RECORD!"
                : "GAME OVER";
        }

        if (_txtWave != null) _txtWave.text = $"Wave: {info.wave}";
        if (_txtTime != null) _txtTime.text = $"Time: {info.survivedSeconds:0.0}s";

        if (_txtBest != null)
        {
            string best = $"Best Wave: {info.bestWave}  |  Best Time: {info.bestSeconds:0.0}s";
            if (info.isNewBestWave || info.isNewBestTime) best += "  (Updated)";
            _txtBest.text = best;
        }
    }

    private void HandleRetry()
    {
        _gs.RestartRun();
    }

    private void HandleQuit()
    {
        // 필요 시 메인 메뉴 씬 로드로 변경
        Application.Quit();
    }
}
