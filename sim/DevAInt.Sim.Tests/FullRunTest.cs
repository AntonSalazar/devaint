using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Дымовые тесты целого забега на реальной карте: случайные легальные действия доигрывают
/// до конца без исключений, а сид + список действий воспроизводят забег бит в бит.
/// </summary>
public class FullRunTest
{
    /// <summary>Реальные правила.</summary>
    private static readonly Rules _rules = Rules.Load(DataDir.Path);

    /// <summary>Случайная политика доигрывает забег на нескольких сидах: исход есть, ход не выше рассвета, исключений нет.</summary>
    /// <param name="seed">Сид.</param>
    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(2026)]
    public void RandomPolicyFinishesTheRun(int seed)
    {
        RunState state = Sim.NewRun(_rules, seed, []);

        List<Action> played = PlayRandom(state, seed);

        Assert.NotEqual(Outcome.Running, state.Outcome);
        Assert.InRange(state.Turn, 1, _rules.Core.DawnTurn);
        Assert.True(played.Count > _rules.Core.DawnTurn / 4, "the run should take a while");
        Assert.Empty(Sim.Legal(_rules, state));
    }

    /// <summary>Реплей: тот же сид и те же действия дают то же состояние.</summary>
    [Fact]
    public void ReplayIsBitExact()
    {
        RunState first = Sim.NewRun(_rules, 42, ["miner"]);
        List<Action> played = PlayRandom(first, 42);

        RunState second = Sim.NewRun(_rules, 42, ["miner"]);
        foreach (Action action in played)
        {
            Sim.Apply(_rules, second, action);
        }

        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.Turn, second.Turn);
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Compute, second.Compute);
        Assert.Equal(first.RngState, second.RngState);
        Assert.Equal(first.Daemon.Pos, second.Daemon.Pos);
        Assert.Equal(first.World.Owner.Raw.ToArray(), second.World.Owner.Raw.ToArray());
        Assert.Equal(first.World.Patch.Raw.ToArray(), second.World.Patch.Raw.ToArray());
        Assert.Equal(first.World.Noise.Raw.ToArray(), second.World.Noise.Raw.ToArray());
    }

    /// <summary>
    /// Случайная политика: на каждом шаге - случайное легальное действие, кроме EndTurn,
    /// пока есть что делать; потом EndTurn. Возвращает сыгранные действия.
    /// </summary>
    /// <param name="state">Состояние; меняется.</param>
    /// <param name="seed">Сид политики (не забега).</param>
    /// <returns>Вернет список действий.</returns>
    private static List<Action> PlayRandom(RunState state, int seed)
    {
        Rng rng = new(seed);
        List<Action> played = [];
        while (state.Outcome == Outcome.Running)
        {
            List<Action> options = [.. Sim.Legal(_rules, state).Where(a => a is not EndTurn)];
            Action action = options.Count > 0 && rng.Chance(0.8f) ? rng.Pick(options) : new EndTurn();
            Sim.Apply(_rules, state, action);
            played.Add(action);
        }

        return played;
    }
}
