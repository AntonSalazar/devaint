using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты конца хода: upkeep, аудит, патч-волны, выгорание, отключение линков, рассвет.
/// Ручная карта 5 × 5, без случайностей там, где можно: шум и счётчики ставятся руками.
/// </summary>
public class TurnTest
{
    /// <summary>Реальные правила.</summary>
    private static readonly Rules _rules = Rules.Load(DataDir.Path);

    // ---------------------------------------------------------------- Upkeep

    /// <summary>EndTurn: ход +1, доход с узлов, очки сброшены, шум затух, событие TurnStarted.</summary>
    [Fact]
    public void EndTurnRunsUpkeep()
    {
        RunState state = Fresh();
        state.World.Owner[TestMaps.Iot] = Sim.Player;              // Home 1.0 + IoT 0.5 = 1.5 → 1
        state.Daemon.ActionPoints = 0;
        state.World.Noise[TestMaps.Start] = 0.5f;

        IReadOnlyList<SimEvent> events = Sim.Apply(_rules, state, new EndTurn());

        Assert.Equal(2, state.Turn);
        Assert.Equal(_rules.Core.StartCompute + 1, state.Compute);
        Assert.Equal(_rules.Core.ActionPoints, state.Daemon.ActionPoints);
        Assert.Equal(0.5f * _rules.Core.NoiseDecay, state.World.Noise[TestMaps.Start]);
        Assert.Equal(new TurnStarted(2, 1), Assert.Single(events));
    }

    /// <summary>Miner: доход ×1.25 - четыре Home дают 5 вместо 4.</summary>
    [Fact]
    public void MinerMultipliesIncome()
    {
        RunState state = Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["miner"]);
        foreach (Hex hex in new[] { Hex.FromOffset(0, 1), Hex.FromOffset(1, 1), Hex.FromOffset(0, 2) })
        {
            TestMaps.Set(state.World, hex, NodeType.Home, Os.Kestrel, 1);
            state.World.Owner[hex] = Sim.Player;
        }

        Assert.Equal(5, Sim.Income(_rules, state));
    }

    /// <summary>Заряды пополняются на своём сервере (+1 до максимума), в поле - нет.</summary>
    [Fact]
    public void ChargesRefillOnlyOnOwnServer()
    {
        RunState state = Fresh();
        state.Daemon.Exploits[0].Charges = 1;
        Sim.Apply(_rules, state, new EndTurn());
        Assert.Equal(1, state.Daemon.Exploits[0].Charges);          // в поле - не растут

        state.World.Owner[TestMaps.Office] = Sim.Player;
        state.Daemon.Pos = TestMaps.Office;
        state.Daemon.Exploits[1].Charges = _rules.Exploits["mote_t1"].MaxCharges;
        Sim.Apply(_rules, state, new EndTurn());
        Assert.Equal(2, state.Daemon.Exploits[0].Charges);          // +1
        Assert.Equal(_rules.Exploits["mote_t1"].MaxCharges, state.Daemon.Exploits[1].Charges);   // не выше максимума
    }

    // ---------------------------------------------------------------- Audit

    /// <summary>Аудит: шумный свой узел становится нейтральным, +1 Patch, шум 0, событие.</summary>
    [Fact]
    public void AuditWipesNoisyNode()
    {
        RunState state = Fresh();
        state.World.Owner[TestMaps.Iot] = Sim.Player;
        state.World.Noise[TestMaps.Iot] = _rules.Core.NoiseAudit;
        int patch = state.World.Patch[TestMaps.Iot];

        IReadOnlyList<SimEvent> events = Sim.Apply(_rules, state, new EndTurn());

        Assert.Equal(Sim.Neutral, state.World.Owner[TestMaps.Iot]);
        Assert.Equal(patch + 1, state.World.Patch[TestMaps.Iot]);
        Assert.Equal(0f, state.World.Noise[TestMaps.Iot]);
        Assert.Contains(new NodeAudited(1, TestMaps.Iot), events);
        Assert.Equal(Outcome.Running, state.Outcome);
    }

    /// <summary>Аудит узла под daemon'ом - поражение: исход, счёт только за узлы, RunEnded, ход не растёт.</summary>
    [Fact]
    public void AuditUnderDaemonLosesTheRun()
    {
        RunState state = Fresh();
        state.World.Noise[TestMaps.Start] = 0.9f;

        IReadOnlyList<SimEvent> events = Sim.Apply(_rules, state, new EndTurn());

        Assert.Equal(Outcome.LostAudit, state.Outcome);
        Assert.Equal(0, state.Score);                              // единственный узел отобран
        Assert.Equal(1, state.Turn);
        Assert.IsType<RunEnded>(events[^1]);
        Assert.Empty(Sim.Legal(_rules, state));
    }

    /// <summary>Шум ниже порога аудит не трогает.</summary>
    [Fact]
    public void QuietNodesSurviveAudit()
    {
        RunState state = Fresh();
        state.World.Noise[TestMaps.Start] = _rules.Core.NoiseAudit - 0.01f;

        Sim.Apply(_rules, state, new EndTurn());

        Assert.Equal(Sim.Player, state.World.Owner[TestMaps.Start]);
    }

    // ---------------------------------------------------------------- Patch wave

    /// <summary>Расписание патчей: фаза 1/8 даёт ходы 8 и 16, фаза 17/5 - 21 и 26, фаза 31/3 - 33.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(16, true)]
    [InlineData(17, false)]
    [InlineData(21, true)]
    [InlineData(26, true)]
    [InlineData(31, false)]
    [InlineData(33, true)]
    public void PatchWaveFollowsSchedule(int turn, bool fires)
    {
        RunState state = Fresh();
        state.Turn = turn;
        EventTrigger trigger = _rules.Events.Single(e => e.Id == "patch_wave").Trigger;

        Assert.Equal(fires, Sim.Triggers(trigger, state, new Rng(1)));
    }

    /// <summary>Патч-волна на ходу 8: ровно одна ОС получила +1 везде (до капа), остальные не тронуты, событие с именем ОС.</summary>
    [Fact]
    public void PatchWaveRaisesOneOs()
    {
        RunState state = Fresh();
        state.Turn = 8;
        state.World.Patch[TestMaps.Target] = _rules.Core.PatchMax;   // Bastion на капе - не растёт
        Dictionary<Hex, int> before = state.World.Nodes().ToDictionary(h => h, h => state.World.Patch[h]);

        IReadOnlyList<SimEvent> events = Sim.Apply(_rules, state, new EndTurn());

        WorldEventFired wave = Assert.Single(events.OfType<WorldEventFired>(), e => e.Id == "patch_wave");
        Os os = System.Enum.Parse<Os>(wave.Text.Split(' ')[2].TrimEnd(':'));
        foreach (Hex hex in state.World.Nodes())
        {
            int expected = state.World.Os[hex] == os ? System.Math.Min(before[hex] + 1, _rules.Core.PatchMax) : before[hex];
            Assert.Equal(expected, state.World.Patch[hex]);
        }
    }

    // ---------------------------------------------------------------- Burnout

    /// <summary>Выгорание: счётчик достиг порога - штраф +1, счётчик 0, порог ×2, событие с новой силой.</summary>
    [Fact]
    public void BurnoutAtExposureLimit()
    {
        RunState state = Fresh();
        int limit = _rules.Exploits["kestrel_t1"].ExposureLimit;
        state.Exposure["kestrel_t1"] = limit;

        IReadOnlyList<SimEvent> events = Sim.Apply(_rules, state, new EndTurn());

        Assert.Equal(1, state.PowerPenalty["kestrel_t1"]);
        Assert.Equal(0, state.Exposure["kestrel_t1"]);
        Assert.Equal(limit * 2, state.ExposureLimit["kestrel_t1"]);
        Assert.Contains(new ExploitBurned(1, "kestrel_t1", 0), events);
        Assert.Equal(0, Sim.Power(state, _rules.Exploits["kestrel_t1"], Os.Kestrel));
    }

    // ---------------------------------------------------------------- Link outage

    /// <summary>Отключённый линк перестаёт быть соседом и возвращается через Duration ходов.</summary>
    [Fact]
    public void LinkOutageExpires()
    {
        RunState state = Fresh();
        Hex far = Hex.FromOffset(3, 0);
        state.World.Owner[far] = Sim.Player;
        Link link = new(TestMaps.Start, far, LinkKind.Sneakernet);
        state.World.Links.Add(link);
        int duration = _rules.Events.Single(e => e.Id == "link_outage").Effect.Duration;

        link.DisabledUntilTurn = state.Turn + duration + 1;       // как ставит мировая фаза
        for (int i = 0; i < duration; i++)
        {
            Sim.Apply(_rules, state, new EndTurn());
            Assert.False(Sim.Check(_rules, state, new Move(far)).Ok, $"turn {state.Turn} still down");
        }
        Sim.Apply(_rules, state, new EndTurn());
        Assert.True(Sim.Check(_rules, state, new Move(far)).Ok);
    }

    // ---------------------------------------------------------------- Dawn

    /// <summary>Рассвет: EndTurn на последнем ходу ночи заканчивает забег поражением.</summary>
    [Fact]
    public void DawnEndsTheRun()
    {
        RunState state = Fresh();
        state.Turn = _rules.Core.DawnTurn - 1;

        IReadOnlyList<SimEvent> events = Sim.Apply(_rules, state, new EndTurn());

        Assert.Equal(Outcome.LostDawn, state.Outcome);
        Assert.Equal(_rules.Core.DawnTurn, state.Turn);
        Assert.Equal(1 * _rules.Core.ScorePerNode, state.Score);
        Assert.Equal(new RunEnded(_rules.Core.DawnTurn, Outcome.LostDawn, state.Score), events[^1]);
    }

    // ---------------------------------------------------------------- Legal

    /// <summary>Legal на старте: EndTurn, Scan, Lurk, захват IoT по Mote и Home по Kestrel; Move некуда, Compile негде.</summary>
    [Fact]
    public void LegalListsWhatCheckAllows()
    {
        RunState state = Fresh();

        IReadOnlyList<Action> legal = Sim.Legal(_rules, state);

        Assert.Contains(new EndTurn(), legal);
        Assert.Contains(new Scan(), legal);
        Assert.Contains(new Lurk(), legal);
        Assert.Contains(new Exploit(1, TestMaps.Iot), legal);
        Assert.Contains(new Exploit(0, Hex.FromOffset(0, 1)), legal);
        Assert.DoesNotContain(legal, a => a is Move);
        Assert.DoesNotContain(legal, a => a is Compile);
        Assert.All(legal, a => Assert.True(Sim.Check(_rules, state, a).Ok));
    }

    // ---------------------------------------------------------------- Helpers

    /// <summary>
    /// Свежий забег на ручной карте без мутаторов.
    /// </summary>
    /// <returns>Вернет состояние.</returns>
    private static RunState Fresh() => Sim.NewRun(_rules, TestMaps.Tiny(), 1, []);
}
