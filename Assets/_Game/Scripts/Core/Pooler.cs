// Pooler.cs
// 통합 오브젝트 풀 시스템 (프리팹 단위 풀 관리)
// - 프리셋 없이 런타임에서 즉시 사용 가능
// - API: Spawn / Despawn / Prewarm / Configure / GetStats
// - IPoolable 훅 제공(선택): OnSpawned() / OnDespawned()

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPoolable
{
    public void OnSpawned();
    public void OnDespawned();
}

[DisallowMultipleComponent]
public class Pooler : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Singleton
    // ─────────────────────────────────────────────────────────────────────────────
    private static Pooler _instance;
    public static Pooler Instance
    {
        get
        {
#if UNITY_EDITOR
            // 에디터에서 플레이 중이 아니면 자동 생성 금지(유령 오브젝트 방지)
            if (!Application.isPlaying) return _instance;
#endif
            if (_isShuttingDown) return null;

            // 이미 존재하면 반환
            if (_instance != null) return _instance;

            // 씬 배치된 인스턴스 우선 탐색
            _instance = FindFirstObjectByType<Pooler>();
            if (_instance != null) return _instance;

            // 최후의 수단: 안전 생성(중복 가드 포함)
            var go = new GameObject("[Pooler]");
            _instance = go.AddComponent<Pooler>();
            _instance.hideFlags = HideFlags.DontSaveInEditor;
            DontDestroyOnLoad(go);
            return _instance;
        }
    }

    private static bool _isShuttingDown = false;

    private void Awake()
    {
        _isShuttingDown = false;

        // 중복 방지(씬에 배치한 경우 포함)
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            // 후발 인스턴스 제거
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
            return;
        }
    }

    private void OnApplicationQuit()
    {
        _isShuttingDown = true;
    }

    private void OnDestroy()
    {
        // 종료 과정이 아니고 자기 자신일 때만 해제
        if (!_isShuttingDown && _instance == this)
            _instance = null;
    }

    [Header("Debug")]
    [SerializeField] private bool _logWarnings = true;
    [SerializeField] private bool _drawHierarchy = true;

    // 프리팹→풀, 인스턴스→풀 매핑
    private readonly Dictionary<GameObject, Pool> _poolsByPrefab = new Dictionary<GameObject, Pool>(64);
    private readonly Dictionary<GameObject, Pool> _poolsByInstance = new Dictionary<GameObject, Pool>(512);

    // ─────────────────────────────────────────────────────────────────────────────
    // Public API (정적 래퍼)
    // ─────────────────────────────────────────────────────────────────────────────
    /// <summary>프리팹 풀 설정/생성(이미 있으면 갱신).</summary>
    public void Configure(GameObject prefab, int prewarm = 0, int maxSize = 128, bool autoExpand = true, int reserve = 0)
        => Instance.ConfigureInternal(prefab, prewarm, maxSize, autoExpand, reserve);

    /// <summary>지정 개수 만큼 미리 생성.</summary>
    public void Prewarm(GameObject prefab, int count) => Instance.PrewarmInternal(prefab, count);

    /// <summary>스폰(게임오브젝트 단위).</summary>
    public GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent = null)
        => Instance.SpawnInternal(prefab, pos, rot, parent);

    /// <summary>스폰(컴포넌트 단위).</summary>
    public T Spawn<T>(T prefab, Vector3 pos, Quaternion rot, Transform parent = null) where T : Component
    {
        var go = Instance.SpawnInternal(prefab.gameObject, pos, rot, parent);
        return go.GetComponent<T>();
    }

    /// <summary>디스폰(즉시).</summary>
    public void Despawn(GameObject instance) => Instance.DespawnInternal(instance);

    /// <summary>디스폰(지연).</summary>
    public void Despawn(GameObject instance, float delay) => Instance.StartCoroutine(Instance.DespawnDelayed(instance, delay));

    /// <summary>풀 전체 간단 통계.</summary>
    public string GetStats() => Instance.GetStatsInternal();

    // ─────────────────────────────────────────────────────────────────────────────
    // Pool 구현
    // ─────────────────────────────────────────────────────────────────────────────
    private class Pool
    {
        public readonly GameObject prefab;
        public readonly Transform root;           // 계층 정리용 폴더
        public int maxSize;
        public bool autoExpand;
        public int reserve;                       // 최소 유지 비활성 수(초과분은 cull 대상)

        // 비활성: 스택, 활성: 집합
        private readonly Stack<GameObject> _inactive = new Stack<GameObject>(64);
        private readonly HashSet<GameObject> _active = new HashSet<GameObject>(64);

        // 진단
        public int TotalCount => _active.Count + _inactive.Count;
        public int ActiveCount => _active.Count;
        public int InactiveCount => _inactive.Count;

        public Pool(GameObject prefab, Transform parent, int maxSize, bool autoExpand, int reserve)
        {
            this.prefab = prefab;
            this.maxSize = Mathf.Max(1, maxSize);
            this.autoExpand = autoExpand;
            this.reserve = Mathf.Max(0, reserve);
            root = new GameObject($"[Pool] {prefab.name}").transform;
            root.SetParent(parent, false);
        }

        public void Prewarm(int count, Pooler owner)
        {
            count = Mathf.Max(0, count);
            for (int i = 0; i < count; i++)
            {
                var go = owner.InstantiateSilent(prefab, root);
                _inactive.Push(go);
            }
        }

        public GameObject Get(Pooler owner, Vector3 pos, Quaternion rot, Transform parent)
        {
            GameObject go = null;

            // 1) 비활성 존재 시 재사용
            if (_inactive.Count > 0)
            {
                go = _inactive.Pop();
            }
            else
            {
                // 2) 더 생성할 수 있나?
                if (TotalCount < maxSize || autoExpand)
                {
                    go = owner.InstantiateSilent(prefab, root);
                }
            }

            if (go == null)
            {
                if (owner._logWarnings)
                    Debug.LogWarning($"[Pooler] Pool '{prefab.name}' is exhausted (Total={TotalCount}, Max={maxSize}).");
                return null;
            }

            _active.Add(go);

            // 활성화 전 위치/부모 설정
            var tr = go.transform;
            if (parent != null) tr.SetParent(parent, false);
            tr.SetPositionAndRotation(pos, rot);

            // 활성화
            go.SetActive(true);

            // 훅
            var poolable = go.GetComponent<IPoolable>();
            poolable?.OnSpawned();

            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null) return;
            if (!_active.Contains(go)) return; // 중복 반납 방지

            // 훅
            var poolable = go.GetComponent<IPoolable>();
            poolable?.OnDespawned();

            // 비활성화 + 루트 재부착
            go.SetActive(false);
            go.transform.SetParent(root, false);

            _active.Remove(go);

            // 용량 초과 시 바로 파기(최소 reserve 유지)
            if (_inactive.Count + 1 > maxSize || (_inactive.Count + 1 > reserve && !autoExpand))
            {
                Object.Destroy(go);
            }
            else
            {
                _inactive.Push(go);
            }
        }

        public void CullExcess()
        {
            // 비활성 개수가 reserve를 초과하면 제거
            while (_inactive.Count > reserve)
            {
                var go = _inactive.Pop();
                Object.Destroy(go);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 내부 로직
    // ─────────────────────────────────────────────────────────────────────────────
    private void ConfigureInternal(GameObject prefab, int prewarm, int maxSize, bool autoExpand, int reserve)
    {
        if (prefab == null) { Debug.LogError("[Pooler] Configure: prefab is null."); return; }

        if (!_poolsByPrefab.TryGetValue(prefab, out var pool))
        {
            pool = new Pool(prefab, transform, maxSize, autoExpand, reserve);
            _poolsByPrefab.Add(prefab, pool);
        }
        else
        {
            pool.maxSize = Mathf.Max(1, maxSize);
            pool.autoExpand = autoExpand;
            pool.reserve = Mathf.Max(0, reserve);
        }

        if (prewarm > 0) pool.Prewarm(prewarm, this);
    }

    private void PrewarmInternal(GameObject prefab, int count)
    {
        EnsurePool(prefab);
        var pool = _poolsByPrefab[prefab];
        pool.Prewarm(count, this);
    }

    private GameObject SpawnInternal(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
    {
        EnsurePool(prefab);
        var pool = _poolsByPrefab[prefab];
        var go = pool.Get(this, pos, rot, parent);
        if (go != null)
        {
            _poolsByInstance[go] = pool;
        }
        return go;
    }

    private void DespawnInternal(GameObject instance)
    {
        if (instance == null) return;

        if (_poolsByInstance.TryGetValue(instance, out var pool))
        {
            pool.Release(instance);
            _poolsByInstance.Remove(instance);
        }
        else
        {
            // 풀 소속이 아니면 일반 파괴(개발 편의)
            if (_logWarnings) Debug.LogWarning($"[Pooler] Despawn: instance '{instance.name}' has no pool. Destroying.");
            Destroy(instance);
        }
    }

    private IEnumerator DespawnDelayed(GameObject instance, float delay)
    {
        yield return new WaitForSeconds(delay);
        DespawnInternal(instance);
    }

    private void EnsurePool(GameObject prefab)
    {
        if (prefab == null) { Debug.LogError("[Pooler] Prefab is null."); return; }

        if (!_poolsByPrefab.ContainsKey(prefab))
        {
            ConfigureInternal(prefab, prewarm: 0, maxSize: 128, autoExpand: true, reserve: 0);
        }
    }

    internal GameObject InstantiateSilent(GameObject prefab, Transform parent)
    {
        var go = Instantiate(prefab, parent);
        go.name = prefab.name; // (Clone) 제거로 계층 가독성
        go.SetActive(false);   // 비활성으로 생성
        return go;
    }

    private string GetStatsInternal()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
        sb.AppendLine("=== Pooler Stats ===");
        foreach (var kv in _poolsByPrefab)
        {
            var p = kv.Value;
            sb.AppendLine($"{p.prefab.name} | Active: {p.ActiveCount}, Inactive: {p.InactiveCount}, Total: {p.TotalCount}, Max: {p.maxSize}, AutoExpand: {p.autoExpand}, Reserve: {p.reserve}");
        }
        return sb.ToString();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!_drawHierarchy) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.2f);
        foreach (Transform child in transform)
        {
            if (child == null) continue;
            Gizmos.DrawWireCube(child.position, Vector3.one * 0.5f);
        }
    }
#endif

    // 주기적 컷(cull) 옵션이 필요하면 코루틴 활성화
    private Coroutine _cullRoutine;
    [SerializeField] private bool _autoCull = false;
    [SerializeField] private float _cullInterval = 10f;

    private void OnEnable()
    {
        if (_autoCull && _cullRoutine == null)
        {
            _cullRoutine = StartCoroutine(CullLoop());
        }
    }

    private void OnDisable()
    {
        if (_cullRoutine != null)
        {
            StopCoroutine(_cullRoutine);
            _cullRoutine = null;
        }
    }

    private IEnumerator CullLoop()
    {
        var wait = new WaitForSeconds(_cullInterval);

        while (true)
        {
            yield return wait;

            foreach (var pool in _poolsByPrefab.Values)
            {
                pool.CullExcess();
            }
        }
    }
}
