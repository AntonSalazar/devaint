using System;
using System.Collections.Generic;

using DevAInt.Sim.Data;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты действий daemon'а на ручной карте 5 × 5 (<see cref="TestMaps.Tiny"/>).
/// Схема одна на все действия: собрать состояние, спросить <see cref="Sim.Check"/>,
/// применить <see cref="Sim.Apply"/>, проверить, что изменилось и что нет.
/// </summary>
public class DaemonActionsTest
{
    /// <summary>Реальные правила.</summary>
    private static readonly Rules _rules = Rules.Load(DataDir.Path);

    /// <summary>Сосед старта справа (1,0) - это IoT из TestMaps.</summary>
    private static readonly Hex _right = TestMaps.Iot;

    /// <summary>Гекс через один от старта: (2,0), не сосед.</summary>
    private static readonly Hex _far = Hex.FromOffset(2, 0);

    // ---------------------------------------------------------------- Move

    /// <summary>Move на соседний свой узел: позиция сменилась, очко списано, событий нет.</summary>
    [Fact]
    public void MoveToOwnedNeighbor()
    {
        RunState state = Fresh();
        state.World.Owner[_right] = Sim.Player;

        Verdict verdict = Sim.Check(_rules, state, new Move(_right));
        IReadOnlyList<SimEvent> events = Sim.Apply(_rules, state, new Move(_right));

        Assert.True(verdict.Ok, verdict.Reason);
        Assert.Equal(_right, state.Daemon.Pos);
        Assert.Equal(_rules.Core.ActionPoints - _rules.Core.MoveCost, state.Daemon.ActionPoints);
        Assert.Empty(events);
    }

    /// <summary>Move на чужой узел нельзя: причина названа, состояние не тронуто.</summary>
    [Fact]
    public void MoveToNeutralIsIllegal()
    {
        RunState state = Fresh();

        Verdict verdict = Sim.Check(_rules, state, new Move(_right));

        Assert.False(verdict.Ok);
        Assert.Contains("not yours", verdict.Reason);
        Assert.Equal(TestMaps.Start, state.Daemon.Pos);
    }

    /// <summary>Move через гекс нельзя, даже если он свой.</summary>
    [Fact]
    public void MoveToNonAdjacentIsIllegal()
    {
        RunState state = Fresh();
        state.World.Owner[_far] = Sim.Player;

        Verdict verdict = Sim.Check(_rules, state, new Move(_far));

        Assert.False(verdict.Ok);
        Assert.Contains("adjacent", verdict.Reason);
    }

    /// <summary>Move без очков нельзя.</summary>
    [Fact]
    public void MoveWithoutActionPointsIsIllegal()
    {
        RunState state = Fresh();
        state.World.Owner[_right] = Sim.Player;
        state.Daemon.ActionPoints = 0;

        Verdict verdict = Sim.Check(_rules, state, new Move(_right));

        Assert.False(verdict.Ok);
        Assert.Contains("action points", verdict.Reason);
    }

    /// <summary>Move на своё же место или за край карты - нельзя.</summary>
    [Fact]
    public void MoveToSelfOrOffMapIsIllegal()
    {
        RunState state = Fresh();

        Assert.False(Sim.Check(_rules, state, new Move(TestMaps.Start)).Ok);
        Assert.False(Sim.Check(_rules, state, new Move(Hex.FromOffset(-1, 0))).Ok);
    }

    /// <summary>Линк делает дальний узел соседом; отключённый линк - нет.</summary>
    [Fact]
    public void MoveThroughLinkRespectsOutage()
    {
        RunState state = Fresh();
        state.World.Owner[_far] = Sim.Player;
        Link link = new(TestMaps.Start, _far, LinkKind.Sneakernet);
        state.World.Links.Add(link);

        Assert.True(Sim.Check(_rules, state, new Move(_far)).Ok);

        link.DisabledUntilTurn = state.Turn + 2;
        Assert.False(Sim.Check(_rules, state, new Move(_far)).Ok);
    }

    /// <summary>Apply нелегального действия - исключение, а не тихое "ничего".</summary>
    [Fact]
    public void ApplyIllegalThrows()
    {
        RunState state = Fresh();

        Assert.Throws<InvalidOperationException>(() => Sim.Apply(_rules, state, new Move(_right)));
    }

    /// <summary>После конца забега любое действие нелегально.</summary>
    [Fact]
    public void NothingIsLegalAfterRunEnds()
    {
        RunState state = Fresh();
        state.World.Owner[_right] = Sim.Player;
        state.Outcome = Outcome.LostDawn;

        Assert.False(Sim.Check(_rules, state, new Move(_right)).Ok);
    }

    // ---------------------------------------------------------------- Scan

    /// <summary>
    /// Scan с офисного сервера в середине карты: радиус Server = 2. Гекс на дистанции 2 раскрыт,
    /// цель на дистанции 3 - в тумане. Очко списано, шум на узле под daemon'ом = ScanNoise.
    /// </summary>
    [Fact]
    public void ScanRevealsRadiusAndMakesNoise()
    {
        // 1. Ситуация: daemon стоит на офисе (2,2). Ставим его туда руками - тест про скан, а не про ходьбу.
        RunState state = Fresh();
        state.World.Owner[TestMaps.Office] = Sim.Player;
        state.Daemon.Pos = TestMaps.Office;
        Layer<bool> known = state.World.KnownBy[Sim.Player];
        Hex atTwo = Hex.FromOffset(4, 2);            // дистанция 2 от офиса, 5 от старта
        Assert.False(known[atTwo]);                  // до скана - в тумане (проверка, что тест честный)

        // 2. Действие.
        Sim.Apply(_rules, state, new Scan());

        // 3. Что стало.
        Assert.True(known[atTwo]);                                           // раскрыт
        Assert.False(known[TestMaps.Target]);                                // цель на дистанции 3 - нет
        Assert.Equal(_rules.Core.ScanNoise, state.World.Noise[TestMaps.Office]);   // шум появился
        Assert.Equal(_rules.Core.ActionPoints - _rules.Core.ScanCost, state.Daemon.ActionPoints);
    }

    /// <summary>Scan без очков нельзя.</summary>
    [Fact]
    public void ScanWithoutActionPointsIsIllegal()
    {
        RunState state = Fresh();
        state.Daemon.ActionPoints = 0;

        Verdict verdict = Sim.CheckScan(_rules, state);
        Assert.False(verdict.Ok);
        Assert.Contains("action points", verdict.Reason);
    }

    // ---------------------------------------------------------------- Lurk

    /// <summary>Lurk обнуляет шум под daemon'ом и стоит очко.</summary>
    [Fact]
    public void LurkClearsNoise()
    {
        // 1. Ситуация: руками ставим шум на узел под daemon'ом.
        RunState state = Fresh();
        state.World.Noise[TestMaps.Start] = 0.7f;

        // 2. Действие.
        Sim.Apply(_rules, state, new Lurk());

        // 3. Что стало.
        Assert.Equal(0f, state.World.Noise[TestMaps.Start]);
        Assert.Equal(_rules.Core.ActionPoints - _rules.Core.LurkCost, state.Daemon.ActionPoints);
    }

    /// <summary>Lurk без очков нельзя.</summary>
    [Fact]
    public void LurkWithoutActionPointsIsIllegal()
    {
        RunState state = Fresh();
        state.Daemon.ActionPoints = 0;

        Verdict verdict = Sim.CheckLurk(_rules, state);
        Assert.False(verdict.Ok);
        Assert.Contains("action points", verdict.Reason);
    }

    // ---------------------------------------------------------------- Helpers

    /// <summary>
    /// Свежий забег на ручной карте без мутаторов.
    /// </summary>
    /// <returns>Вернет состояние.</returns>
    private static RunState Fresh() => Sim.NewRun(_rules, TestMaps.Tiny(), 1, []);
}
