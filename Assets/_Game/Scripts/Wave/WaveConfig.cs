using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WaveConfig", menuName = "ArenaRush SO/WaveConfig", order = 0)]
public class WaveConfig : ScriptableObject
{
    [Serializable]
    public class EnemySpawnGroup
    {
        [Tooltip("스폰할 적 프리팹")]
        public GameObject prefab;

        [Min(1), Tooltip("이 그룹이 스폰할 총 마릿수")]
        public int count = 5;

        [Min(0f), Tooltip("개체 간 스폰 간격(초)")]
        public float interval = 0.2f;

        [Min(0f), Tooltip("그룹 시작 지연(초)")]
        public float startDelay = 0f;
    }

    [Serializable]
    public class WaveEntry
    {
        [Tooltip("에디터 식별용")]
        public string name = "Wave";

        [Tooltip("웨이브 시작 전 지연(초)")]
        [Min(0f)] public float waveStartDelay = 0.5f;

        [Tooltip("이 웨이브에서 스폰될 그룹들")]
        public List<EnemySpawnGroup> groups = new List<EnemySpawnGroup>();

        [Header("보스(옵션)")]
        public bool isBossWave = false;
        public GameObject bossPrefab;
        [Min(0)] public int bossCount = 1;
        [Min(0f)] public float bossInterval = 0.5f;
    }

    [Tooltip("진행 순서대로 배치된 웨이브 데이터")]
    public List<WaveEntry> waves = new List<WaveEntry>();
}
