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
    [SerializeField] private Transform _spawnPointsContainer;     // (선택) 자식들을 자동 수집하고 싶을 때
    [SerializeField] private Transform[] _spawnPoints;

    [Header("Player Filter")]
    [SerializeField] private Transform _player;
    [SerializeField, Min(0f)] private float _minDistanceFromPlayer = 6f;

    [Header("Wave Options")]
    [SerializeField] private bool _shuffleEachWave = true;

    // ===== ScriptableObject References =====
    [Header("Subscribe Events")]
    [SerializeField] private GameEventSO _onRunStartedEvent;

    [Header("GameState Integration")]
    [SerializeField] private bool _respectGameState = true;          // GameState를 존중
    [SerializeField] private bool _blockWhenNotPlayable = true;      // 비플레이 상태에서 스폰 차단
    [SerializeField] private string _playerTag = "Player";           // 런 시작 시 재확인
    [SerializeField] private bool _resolvePlayerOnRunStarted = true; // RunStarted에서 Player 참조 갱신

    private System.Random _rng;

    private GameState _gs;

    private void Awake()
    {
        _rng = new System.Random();

        // 컨테이너 기반 자동 수집(옵션)
        if (_spawnPointsContainer != null && (_spawnPoints == null || _spawnPoints.Length == 0))
        {
            var list = _spawnPointsContainer.GetComponentsInChildren<Transform>(true);
            // 자기 자신 제외
            var tmp = new System.Collections.Generic.List<Transform>(list.Length);
            foreach (var t in list)
                if (t != _spawnPointsContainer) tmp.Add(t);
            _spawnPoints = tmp.ToArray();
        }

        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogWarning("[EnemySpawnManager] 스폰 포인트가 없습니다.");
        }

        _gs = FindAnyObjectByType<GameState>();
    }

    private void OnEnable()
    {
        if (_onRunStartedEvent != null)
        {
            _onRunStartedEvent.AddListener(OnRunStarted);
        }

        // 초기 Player 없으면 한 번 시도
        if (_player == null)
        {
            var p = GameObject.FindGameObjectWithTag(_playerTag);
            if (p != null) _player = p.transform;
        }
    }

    private void OnDisable()
    {
        if (_onRunStartedEvent != null)
        {
            _onRunStartedEvent.RemoveListener(OnRunStarted);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GameState Hooks
    // ─────────────────────────────────────────────────────────────────────────────
    private void OnRunStarted()
    {
        if (_resolvePlayerOnRunStarted || _player == null)
        {
            var p = GameObject.FindGameObjectWithTag(_playerTag);
            if (p != null) _player = p.transform;
        }
    }

    private bool CanSpawnByGameState()
    {
        if (!_respectGameState || _gs == null) return true;
        if (!_blockWhenNotPlayable) return true;
        bool result = false;
        result = _gs.IsPlayable()
            && !_gs.IsInputLocked();

        return result;
    }
    
    // ─────────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────────
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

    /// <summary>
    /// 스폰 실행. Pooler가 있으면 Pool Spawn, 없으면 Instantiate.
    /// GameState가 비플레이면 실패(null).
    /// </summary>
    public GameObject Spawn(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[EnemySpawnManager] 프리팹이 null 입니다.");
            return null;
        }

        // 상태 체크
        if (!CanSpawnByGameState())
        {
#if UNITY_EDITOR
            // 디버깅용 로그(필요시 비활성화 가능)
            Debug.Log($"[EnemySpawnManager] 비플레이 상태에서 스폰 차단: {_gs.CurrentState()}");
#endif
            return null;
        }

        var point = PickSpawnPoint();
        if (point == null)
        {
            Debug.LogWarning("[EnemySpawnManager] 유효한 스폰 포인트가 없습니다");
            return null;
        }

        GameObject go;
        // Pool 우선 시도 → 폴백 Instantiate
        if (Pooler.Instance != null)
        {
            go = Pooler.Instance.Spawn(prefab, point.position, point.rotation);
        }
        else
        {
            Debug.Log("[EnemySpawnManager] Spawn Enemy With Instantiate Call");
            go = Instantiate(prefab, point.position, point.rotation);
        }

        return go;
    }
}
