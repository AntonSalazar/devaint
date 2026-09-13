using System;
using System.Linq;

using DevAInt.Sim.Data;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты <see cref="Sim.NewRun(Rules, int, System.Collections.Generic.IReadOnlyList{string})"/>:
/// стартовое состояние, мутаторы, туман, копирование.
/// </summary>
public class NewRunTest
{
    /// <summary>Реальные правила.</summary>
    private static readonly Rules _rules = Rules.Load(DataDir.Path);

    /// <summary>Стартовое состояние на реальной карте: ход 1, Compute из правил, daemon на старте со стартовыми эксплойтами.</summary>
    [Fact]
    public void StartStateMatchesRules()
    {
        RunState state = Sim.NewRun(_rules, 42, []);

        Assert.Equal(1, state.Turn);
        Assert.Equal(42, state.Seed);
        Assert.Equal(_rules.Core.StartCompute, state.Compute);
        Assert.Equal(Outcome.Running, state.Outcome);
        Assert.Equal(_rules.Core.ActionPoints, state.Daemon.ActionPoints);
        Assert.Equal(Sim.Player, state.World.Owner[state.Daemon.Pos]);
        Assert.Equal(NodeType.Home, state.World.Type[state.Daemon.Pos]);
        Assert.Equal(NodeType.Server, state.World.Type[state.Target]);

        Assert.Equal(_rules.Core.StartingExploits, state.Daemon.Exploits.Select(s => s.DefId));
        Assert.All(state.Daemon.Exploits, slot => Assert.Equal(_rules.Exploits[slot.DefId].MaxCharges, slot.Charges));
    }

    /// <summary>Только стартовый узел свой, всё остальное нейтрально.</summary>
    [Fact]
    public void OnlyStartIsOwned()
    {
        RunState state = Sim.NewRun(_rules, 42, []);

        int owned = state.World.Cells().Count(h => state.World.Owner[h] == Sim.Player);

        Assert.Equal(1, owned);
    }

    /// <summary>Туман снят вокруг старта радиусом скана Home, цель далеко - не видна.</summary>
    [Fact]
    public void FogRevealedAroundStart()
    {
        RunState state = Sim.NewRun(_rules, 42, []);
        Layer<bool> known = state.World.KnownBy[Sim.Player];
        int radius = _rules.NodeTypes[NodeType.Home].ScanRadius;

        foreach (Hex hex in state.World.Cells())
        {
            Assert.Equal(state.Daemon.Pos.DistanceTo(hex) <= radius, known[hex]);
        }
        Assert.False(known[state.Target]);
    }

    /// <summary>Счётчики выгорания заведены для каждого эксплойта, порог - из описания.</summary>
    [Fact]
    public void ExposureCountersInitialised()
    {
        RunState state = Sim.NewRun(_rules, 42, []);

        foreach (ExploitDef def in _rules.Exploits.Values)
        {
            Assert.Equal(0, state.Exposure[def.Id]);
            Assert.Equal(def.ExposureLimit, state.ExposureLimit[def.Id]);
            Assert.Equal(0, state.PowerPenalty[def.Id]);
        }
    }

    /// <summary>Один сид - одно состояние: карта, старт, генератор случайностей.</summary>
    [Fact]
    public void SameSeedSameRun()
    {
        RunState a = Sim.NewRun(_rules, 42, ["miner"]);
        RunState b = Sim.NewRun(_rules, 42, ["miner"]);

        Assert.Equal(a.Daemon.Pos, b.Daemon.Pos);
        Assert.Equal(a.Target, b.Target);
        Assert.Equal(a.RngState, b.RngState);
        Assert.Equal(a.World.Type.Raw.ToArray(), b.World.Type.Raw.ToArray());
    }

    /// <summary>Мутаторы сворачиваются в коэффициенты: бонусы складываются, множители перемножаются.</summary>
    [Fact]
    public void StatsFoldMutators()
    {
        RunState state = Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["native_kestrel", "native_bastion", "optic"]);

        Assert.Equal(1, state.Stats.PowerBonus[Os.Kestrel]);
        Assert.Equal(1, state.Stats.PowerBonus[Os.Bastion]);
        Assert.Equal(0, state.Stats.PowerBonus[Os.Mote]);
        Assert.Equal(1, state.Stats.ScanRadiusBonus);
        Assert.Equal(1f, state.Stats.NoiseMult);
        Assert.Equal(_rules.Core.ActionPoints, state.Stats.ActionPoints);
    }

    /// <summary>Swift даёт +1 очко действия уже на первом ходу, hoarder - +1 заряд в каждом слоте.</summary>
    [Fact]
    public void SwiftAndHoarderApplyAtStart()
    {
        RunState state = Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["swift", "hoarder"]);

        Assert.Equal(_rules.Core.ActionPoints + 1, state.Daemon.ActionPoints);
        Assert.All(state.Daemon.Exploits, slot => Assert.Equal(_rules.Exploits[slot.DefId].MaxCharges + 1, slot.Charges));
    }

    /// <summary>Zero-day cache добавляет третий стартовый слот с зеро-деем.</summary>
    [Fact]
    public void ZeroDayCacheAddsSlot()
    {
        RunState state = Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["zero_day_cache"]);
        string zeroDay = _rules.Modifiers["zero_day_cache"].StartingExploit!;

        Assert.Equal(3, state.Daemon.Exploits.Count);
        Assert.Equal(zeroDay, state.Daemon.Exploits[^1].DefId);
        Assert.Equal(Rarity.ZeroDay, _rules.Exploits[zeroDay].Rarity);
    }

    /// <summary>Silent kernel: шум ×0.5; wide slots: лимит слотов +1.</summary>
    [Fact]
    public void SilentKernelAndWideSlots()
    {
        RunState state = Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["silent_kernel", "optic"]);
        RunState wide = Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["wide_slots"]);

        Assert.Equal(0.5f, state.Stats.NoiseMult);
        Assert.Equal(_rules.Core.SlotLimit + 1, wide.Stats.SlotLimit);
    }

    /// <summary>Набор дороже бюджета, повтор или неизвестный Id - ошибка вызывающего.</summary>
    [Fact]
    public void BadMutatorSetsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["silent_kernel", "swift"]));
        Assert.Throws<ArgumentException>(() => Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["miner", "miner"]));
        Assert.Throws<ArgumentException>(() => Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["ghost"]));
    }

    /// <summary>На ручной карте: старт свой, соседи видны, цель в дальнем углу не видна.</summary>
    [Fact]
    public void TinyMapWorks()
    {
        RunState state = Sim.NewRun(_rules, TestMaps.Tiny(), 1, []);

        Assert.Equal(TestMaps.Start, state.Daemon.Pos);
        Assert.Equal(TestMaps.Target, state.Target);
        Assert.Equal(Sim.Player, state.World.Owner[TestMaps.Start]);
        Assert.Equal(Sim.Neutral, state.World.Owner[TestMaps.Iot]);
        Assert.True(state.World.KnownBy[Sim.Player][TestMaps.Iot]);
        Assert.False(state.World.KnownBy[Sim.Player][TestMaps.Target]);
    }

    /// <summary>Clone - независимая копия: правка копии не трогает оригинал.</summary>
    [Fact]
    public void CloneIsIndependent()
    {
        RunState original = Sim.NewRun(_rules, TestMaps.Tiny(), 1, ["miner"]);
        RunState copy = original.Clone();

        copy.Turn = 9;
        copy.Compute = 0;
        copy.Daemon.Pos = TestMaps.Office;
        copy.Daemon.Exploits[0].Charges = 0;
        copy.World.Owner[TestMaps.Office] = Sim.Player;
        copy.World.KnownBy[Sim.Player][TestMaps.Target] = true;
        copy.Exposure["kestrel_t1"] = 5;
        copy.Mutators.Add("optic");

        Assert.Equal(1, original.Turn);
        Assert.Equal(_rules.Core.StartCompute, original.Compute);
        Assert.Equal(TestMaps.Start, original.Daemon.Pos);
        Assert.Equal(_rules.Exploits["kestrel_t1"].MaxCharges, original.Daemon.Exploits[0].Charges);
        Assert.Equal(Sim.Neutral, original.World.Owner[TestMaps.Office]);
        Assert.False(original.World.KnownBy[Sim.Player][TestMaps.Target]);
        Assert.Equal(0, original.Exposure["kestrel_t1"]);
        Assert.Single(original.Mutators);
    }
}
