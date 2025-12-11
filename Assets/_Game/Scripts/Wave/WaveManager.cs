using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 웨이브 진행/스폰/클리어 관리.
/// - WaveConfig에 정의된 순서대로 웨이브 수행
/// - 적 사망 카운트 기반으로 웨이브 종료 판단
/// - 이벤트: OnWaveStartedEvent / OnWaveClearedEvent
///
/// - GameState 연동 버전
///   · 웨이브 클리어 후 GameState.PerkSelect 진입, Playing 복귀까지 대기
/// </summary>

[DisallowMultipleComponent]
public class WaveManager : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private WaveConfig _config;
    [SerializeField] private EnemySpawnManager _spawner;

    // ===== ScriptableObject References =====
    [Header("ScriptableObject Events")]
    public GameEventSO_Int OnWaveStartedEvent;        // 웨이브 시작 이벤트 (int)
    public GameEventSO_Int OnWaveClearedEvent;        // 웨이브 클리어 이벤트 (int)
    public GameEventSO_Int OnEnemySpawnedEvent;       // Enemy Spawn 이벤트 (int)
    public GameEventSO_Int OnEnemyDiedEvent;       // Enemy Died 이벤트 (int)

    [Header("Runtime State (ReadOnly)")]
    [SerializeField, Min(0)] private int _currentWaveIndex = -1;
    public int CurrentWave => _currentWaveIndex + 1;
    [SerializeField, Min(0)] private int _alive = 0;
    public int AliveEnemies => _alive;
    [SerializeField] private bool _running;

    private GameState _gs;

    private void Awake()
    {
        _gs = FindAnyObjectByType<GameState>();
    }

    private void OnEnable()
    {
        if (_gs != null)
        {
            _gs.OnRunStartedEvent.AddListener(HandleRunStarted);
        }
    }

    private void OnDisable()
    {
        if (_gs != null)
        {
            _gs.OnRunStartedEvent.RemoveListener(HandleRunStarted);
        }
    }

    private void Start()
    {
        if (_config == null || _config.waves.Count == 0)
        {
            Debug.LogWarning("[WaveManager] WaveConfig 누락/비어있음");
            return;
        }
        if (_spawner == null)
        {
            _spawner = FindAnyObjectByType<EnemySpawnManager>();
            if (_spawner == null)
            {
                Debug.LogError("[WaveManager] EnemySpawnManager를 찾을 수 없음");
                return;
            }
        }
    }

    public void StopAll()
    {
        _running = false;
        StopAllCoroutines();
    }

    private IEnumerator RunAllWaves()
    {
        _running = true;

        for (int i = 0; _running && i < _config.waves.Count; i++)
        {
            _currentWaveIndex = i;
            var wave = _config.waves[i];

            if (wave.waveStartDelay > 0)
            {
                yield return new WaitForSeconds(wave.waveStartDelay);
            }

            // 스폰 포인트 셔플 등 웨이브별 초기화
            _spawner.InitForWave();

            OnWaveStartedEvent.Raise(i);

            // 스폰 시작
            yield return StartCoroutine(SpawnWave(wave));

            // 모두 사망할 때까지 대기
            yield return new WaitUntil(() => _alive <= 0);

            Debug.Log("Wave " + _config.waves[i].name + " Cleared");
            OnWaveClearedEvent.Raise(i);

            // GameState를 통한 PerkSelect → Playing 복귀까지 대기
            if (_gs != null)
            {
                // PerkSelect 진입 대기
                yield return new WaitUntil(() =>
                    _gs.CurrentState() == GameStateSO.E_GamePlayState.PerkSelect ||
                    _gs.CurrentState() == GameStateSO.E_GamePlayState.GameOver);

                // 선택/확정 후 Playing 복귀 대기 (GameOver 시 루프 종료)
                yield return new WaitUntil(() =>
                    _gs.CurrentState() == GameStateSO.E_GamePlayState.Playing ||
                    _gs.CurrentState() == GameStateSO.E_GamePlayState.GameOver);

                if (_gs.CurrentState() == GameStateSO.E_GamePlayState.GameOver)
                {
                    _running = false;
                    break;
                }
            }
        }

        _running = false;
    }

    private IEnumerator SpawnWave(WaveConfig.WaveEntry wave)
    {
        // 그룹 스폰(병렬처럼 보이도록 그룹별 코루틴 실행)
        var routines = new List<Coroutine>();

        foreach (var g in wave.groups)
        {
            if (g.prefab == null || g.count <= 0) continue;
            routines.Add(StartCoroutine(SpawnGroup(g)));
        }

        if (wave.isBossWave && wave.bossPrefab != null && wave.bossCount > 0)
        {
            routines.Add(StartCoroutine(SpawnBosses(wave.bossPrefab, wave.bossCount, wave.bossInterval)));
        }

        // 모든 그룹 스폰 완료까지 대기(= 생성만 완료, 처치는 별도로 대기)
        foreach (var r in routines)
            yield return r;
    }

    private IEnumerator SpawnGroup(WaveConfig.EnemySpawnGroup g)
    {
        if (g.startDelay > 0f) yield return new WaitForSeconds(g.startDelay);

        for (int i = 0; i < g.count; i++)
        {
            SpawnEnemy(g.prefab);
            if (g.interval > 0f) yield return new WaitForSeconds(g.interval);
            else yield return null; // 다음 프레임
        }
    }

    private IEnumerator SpawnBosses(GameObject bossPrefab, int count, float interval)
    {
        for (int i = 0; i < count; i++)
        {
            SpawnEnemy(bossPrefab);
            if (interval > 0f) yield return new WaitForSeconds(interval);
            else yield return null;
        }
    }

    private void SpawnEnemy(GameObject prefab)
    {
        var go = _spawner.Spawn(prefab);
        if (go == null) return;

        _alive = Mathf.Max(0, _alive + 1);

        // Health 이벤트 구독
        var health = go.GetComponent<Health>();
        if (health != null)
        {
            health.AddListenerOnDeathEvent(HandleEnemyDied);
        }

        if (OnEnemySpawnedEvent != null)
        {
            OnEnemySpawnedEvent.Raise(_alive);
        }
    }

    private void HandleEnemyDied(Health health)
    {
        // 자기 자신을 즉시 구독 해제
        health.RemoveListenerOnDeathEvent(HandleEnemyDied);

        // 카운트 감소
        _alive = Mathf.Max(0, _alive - 1);

        if (OnEnemyDiedEvent != null)
        {
            OnEnemyDiedEvent.Raise(_alive);
        }
    }

    private void HandleRunStarted()
    {
        StartCoroutine(RunAllWaves());
    }
}
