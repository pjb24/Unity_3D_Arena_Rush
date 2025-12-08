using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 웨이브 진행/스폰/클리어 관리.
/// - WaveConfig에 정의된 순서대로 웨이브 수행
/// - 적 사망 카운트 기반으로 웨이브 종료 판단
/// - 이벤트: WaveStarted / WaveCleared / AllWavesCleared
/// </summary>
[DisallowMultipleComponent]
public class WaveManager : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private WaveConfig _config;
    [SerializeField] private EnemySpawnManager _spawner;

    [Header("Runtime State (ReadOnly)")]
    [SerializeField, Min(0)] private int _currentWaveIndex = -1;
    [SerializeField, Min(0)] private int _alive = 0;
    [SerializeField] private bool _running;

    private bool _selectPerkFlag = false;

    public void SetSelectPerk()
    {
        _selectPerkFlag = true;
    }

    private event Action<int> _onWaveStarted;     // waveIndex
    private event Action<int> _onWaveCleared;     // waveIndex
    private event Action _onAllWavesCleared;

    // 외부 등록용 메서드
    public void AddWaveStartedListener(Action<int> listener) => _onWaveStarted += listener;
    public void RemoveWaveStartedListener(Action<int> listener) => _onWaveStarted -= listener;

    public void AddWaveClearedListener(Action<int> listener) => _onWaveCleared += listener;
    public void RemoveWaveClearedListener(Action<int> listener) => _onWaveCleared -= listener;

    public void AddAllWavesClearedListener(Action listener) => _onAllWavesCleared += listener;
    public void RemoveAllWavesClearedListener(Action listener) => _onAllWavesCleared -= listener;

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
        StartCoroutine(RunAllWaves());
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

            yield return new WaitForSeconds(wave.waveStartDelay);

            // 스폰 포인트 셔플 등 웨이브별 초기화
            _spawner.InitForWave();

            _onWaveStarted?.Invoke(i);

            // 스폰 시작
            yield return StartCoroutine(SpawnWave(wave));

            // 모두 사망할 때까지 대기
            yield return new WaitUntil(() => _alive <= 0);

            Debug.Log("Wave " + _config.waves[i].name + " Cleared");
            _onWaveCleared?.Invoke(i);

            if (_onWaveCleared.GetInvocationList().Length > 0)
            {
                yield return new WaitUntil(() => _selectPerkFlag == true);
                _selectPerkFlag = false;
            }
        }

        _running = false;
        _onAllWavesCleared?.Invoke();
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

        _alive++;

        // Health 이벤트 구독(권장)
        var health = go.GetComponent<Health>();
        if (health != null)
        {
            // Health가 C# event OnDied를 제공한다고 가정
            health.AddDeathListener(HandleEnemyDied);
        }
    }

    private void HandleEnemyDied(Health health)
    {
        // 자기 자신을 즉시 구독 해제
        health.RemoveDeathListener(HandleEnemyDied);

        // 카운트 감소
        _alive = Mathf.Max(0, _alive - 1);
    }
}
