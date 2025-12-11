using System;
using System.Collections.Generic;
using UnityEngine;

public enum E_PerkStat
{
    Damage,         // 공격력
    FireRate,       // 발사 속도(초당 발사 수)
    MoveSpeed,      // 이동 속도
    MaxHP,          // 최대 체력
    DashCooldown,   // 대시 쿨다운 시간
}

public enum E_PerkOp
{
    AddFlat,        // 값에 +v
    AddPercent,     // 값에 +(base * v)
    Multiply,       // 값에 *v
}

[Serializable]
public struct PerkEffect
{
    [Tooltip("어떤 스탯에 영향을 줄지")]
    public E_PerkStat stat;

    [Tooltip("연산 방식: 가산, 퍼센트 가산, 승수")]
    public E_PerkOp op;

    [Tooltip("효과 값. 예) AddPercent=0.2f → +20%")]
    public float value;
}

[CreateAssetMenu(fileName = "Perk_", menuName = "ArenaRush SO/Perk")]
public class Perk : ScriptableObject
{
    [Header("Meta")]
    [SerializeField] private string _perkId = GuidEmpty;
    [SerializeField] private string _displayName;
    [TextArea(1, 3)][SerializeField] private string _description;
    [SerializeField] private Sprite _icon;

    [Header("Stacking")]
    [Tooltip("동일 퍼크가 중첩 가능한지")]
    [SerializeField] private bool _stackable = true;

    [Tooltip("최대 중첩 수(스택 가능 시)")]
    [SerializeField] private int _maxStacks = 99;

    [Header("Effects")]
    [SerializeField] private List<PerkEffect> _effects = new List<PerkEffect>();

    private const string GuidEmpty = "00000000-0000-0000-0000-000000000000";

    public string PerkId => string.IsNullOrWhiteSpace(_perkId) ? GuidEmpty : _perkId;
    public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;
    public string Description => _description;
    public Sprite Icon => _icon;
    public IReadOnlyList<PerkEffect> Effects => _effects;
    public bool Stackable => _stackable;
    public int MaxStacks => Mathf.Max(1, _maxStacks);

    /// <summary>
    /// UI용 간단 설명 생성. (예: "Damage +20%, FireRate +15%")
    /// </summary>
    public string BuildCompactSummary()
    {
        if (_effects == null || _effects.Count == 0) return Description;

        var parts = ListPool<string>.Get();
        try
        {
            foreach (var e in _effects)
            {
                switch (e.op)
                {
                    case E_PerkOp.AddFlat:
                        parts.Add($"{e.stat} +{TrimZero(e.value)}");
                        break;
                    case E_PerkOp.AddPercent:
                        parts.Add($"{e.stat} +{TrimZero(e.value * 100f)}%");
                        break;
                    case E_PerkOp.Multiply:
                        parts.Add($"{e.stat} ×{TrimZero(e.value)}");
                        break;
                }
            }
            return string.Join(", ", parts);
        }
        finally
        {
            ListPool<string>.Release(parts);
        }
    }

    private static string TrimZero(float v)
    {
        return v.ToString(Mathf.Approximately(v % 1f, 0f) ? "0" : "0.##");
    }

    /// <summary>
    /// 간이 리스트 풀(할당 최소화 목적). 프로토타입 용도.
    /// </summary>
    private static class ListPool<T>
    {
        static readonly Stack<List<T>> Pool = new Stack<List<T>>();
        public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>(4);
        public static void Release(List<T> list) { list.Clear(); Pool.Push(list); }
    }
}
