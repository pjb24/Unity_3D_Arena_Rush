/// <summary>
/// EnemySpawnManager (Pooler 호환)
/// - 스폰 포인트 셔플(Fisher–Yates)
/// - 플레이어 최소 거리 필터
/// </summary>

using UnityEngine;

[DisallowMultipleComponent]
public class EnemySpawnManager : MonoBehaviour
{
    [Header("Spawn Points")]
    [SerializeField] private Transform[] _spawnPoints;

    [Header("Player Filter")]
    [SerializeField] private Transform _player;
    [SerializeField, Min(0f)] private float _minDistanceFromPlayer = 6f;

    [Header("Wave Options")]
    [SerializeField] private bool _shuffleEachWave = true;

    [Header("Object Pool (Optional)")]
    [Tooltip("Pooler 컴포넌트(싱글톤/인스턴스). 없으면 Instantiate 사용")]
    [SerializeField] private Object _pooler; // 임의 Pooler 참조(타입 자유)

    private System.Random _rng;

    private void Awake()
    {
        _rng = new System.Random();
        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogWarning("[SpawnManager] 스폰 포인트가 없습니다.");
        }
    }

    /// <summary>웨이브 시작 시 호출(셔플 등)</summary>
    public void InitForWave()
    {
        if (!_shuffleEachWave || _spawnPoints == null || _spawnPoints.Length <= 1) return;

        // 간단한 Fisher–Yates
        for (int i = _spawnPoints.Length - 1; i > 0; --i)
        {
            int j = _rng.Next(i + 1);
            var tmp = _spawnPoints[i];
            _spawnPoints[i] = _spawnPoints[j];
            _spawnPoints[j] = tmp;
        }
    }

    /// <summary>플레이어 최소 거리 조건을 만족하는 포인트 중 랜덤 선택(선택 실패시 전체 중 랜덤)</summary>
    public Transform PickSpawnPoint()
    {
        if (_spawnPoints == null || _spawnPoints.Length == 0)
            return null;

        if (_player == null || _minDistanceFromPlayer <= 0f)
            return _spawnPoints[_rng.Next(_spawnPoints.Length)];

        // 플레이어와 최소 거리 조건 만족하는 포인트 중 랜덤

        // 1: 후보 개수 카운트
        float minSqr = _minDistanceFromPlayer * _minDistanceFromPlayer;
        int eligibleCount = 0;
        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            var p = _spawnPoints[i];
            if (p == null) continue;
            Vector3 d = p.position - _player.position;
            if (d.sqrMagnitude >= minSqr) eligibleCount++;
        }

        // 후보 없으면 전체 중 랜덤
        if (eligibleCount == 0)
            return _spawnPoints[_rng.Next(_spawnPoints.Length)];

        // 2: k번째 후보 선택
        int k = _rng.Next(eligibleCount);
        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            var p = _spawnPoints[i];
            if (p == null) continue;
            Vector3 d = p.position - _player.position;
            if (d.sqrMagnitude >= minSqr)
            {
                if (k == 0) return p;
                k--;
            }
        }

        // 논리상 도달 불가. 안전 폴백
        return _spawnPoints[_rng.Next(_spawnPoints.Length)];
    }
    
    /// <summary>스폰 실행. Pooler가 있으면 Pool Spawn, 없으면 Instantiate.</summary>
    public GameObject Spawn(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[SpawnManager] 프리팹이 null 입니다.");
            return null;
        }

        var point = PickSpawnPoint();
        if (point == null)
        {
            Debug.LogWarning("[SpawnManager] 유효한 스폰 포인트가 없습니다");
            return null;
        }

        // Pool 우선 시도 → 폴백 Instantiate
        GameObject go;
        //if (PoolerBridge.TrySpawn(_pooler, prefab, point.position, point.rotation, out go))
        //    return go;

        go = Instantiate(prefab, point.position, point.rotation);

        return go;
    }
}
