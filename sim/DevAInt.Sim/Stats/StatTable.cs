using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Stats;

/// <summary>Эффект уровня мудла с номером стата вместо id.</summary>
public readonly record struct ResolvedEffect(int Stat, ModifierKind Kind, double Value);

/// <summary>Мудл с номером его стата, владельцем модификаторов и эффектами по уровням.</summary>
public sealed record MoodleInfo(int Stat, MoodleDef Def, string Owner, IReadOnlyList<IReadOnlyList<ResolvedEffect>> EffectsByLevel);

/// <summary>Все статы и мудлы по номерам, общие для всех сущностей; строится один раз после загрузки модов.</summary>
public sealed class StatTable
{
    /// <summary>Статы по номерам.</summary>
    private readonly IReadOnlyList<StatDef> _stats;

    /// <summary>Мудлы всех статов по номерам.</summary>
    private readonly List<MoodleInfo> _moodles = [];

    /// <summary>Номер стата по id.</summary>
    private readonly Dictionary<string, int> _statIndex = [];

    /// <summary>Номер мудла по id.</summary>
    private readonly Dictionary<string, int> _moodleIndex = [];

    /// <summary>Таблица по проверенным определениям.</summary>
    public StatTable(DefDatabase defs)
    {
        _stats = defs.All<StatDef>();
        for (int i = 0; i < _stats.Count; i++)
        {
            _statIndex[_stats[i].Id] = i;
        }

        // Эффекты переводятся в номера статов один раз, чтобы тик не искал строки
        for (int i = 0; i < _stats.Count; i++)
        {
            foreach (MoodleDef moodle in _stats[i].Moodles)
            {
                IReadOnlyList<ResolvedEffect>[] effects = [.. moodle.Levels.Select(level => Resolve(level.Effects))];
                _moodleIndex[moodle.Id] = _moodles.Count;
                _moodles.Add(new MoodleInfo(i, moodle, "moodle:" + moodle.Id, effects));
            }
        }
    }

    /// <summary>Число статов.</summary>
    public int StatCount => _stats.Count;

    /// <summary>Число мудлов.</summary>
    public int MoodleCount => _moodles.Count;

    /// <summary>Стат по номеру.</summary>
    public StatDef Stat(int index) => _stats[index];

    /// <summary>Мудл по номеру.</summary>
    public MoodleInfo Moodle(int index) => _moodles[index];

    /// <summary>Номер стата по id, исключение если его нет.</summary>
    public int StatIndex(string id) => _statIndex[id];

    /// <summary>Номер мудла по id, исключение если его нет.</summary>
    public int MoodleIndex(string id) => _moodleIndex[id];

    /// <summary>Эффекты уровня с номерами статов.</summary>
    private IReadOnlyList<ResolvedEffect> Resolve(IReadOnlyList<EffectDef> effects) => [.. effects.Select(ToResolved)];

    /// <summary>Эффект с номером стата вместо id.</summary>
    private ResolvedEffect ToResolved(EffectDef effect) => new(_statIndex[effect.Stat], effect.Kind, effect.Value);
}
