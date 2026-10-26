using System;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты очереди ввода на сервере и предсказания своего движения на клиенте.</summary>
public class PredictionTest
{
    /// <summary>Шаг ходьбы за тик без ускорения: 1.5 м/с за 0.1 с.</summary>
    private const double Step = 0.15;

    /// <summary>Допуск координат: позиции идут по сети во float.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>Время для подключений с задержкой.</summary>
    private double _time;

    /// <summary>Два ввода за один тик сервер тратит по одному за тик, номер подтверждения идёт следом.</summary>
    [Fact]
    public void ServerAppliesOneInputPerTick()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);

        client.SendInput(new MoveInput(1, 0));
        client.SendInput(new MoveInput(0, 1));
        run.Tick(1, client);

        Assert.Equal(1u, client.Replica.LastInput);
        Assert.Equal(6.5 + Step, run.Sim.Entity(client.Replica.OwnEntityId).Position.X, 6);

        run.Tick(1, client);

        Assert.Equal(2u, client.Replica.LastInput);
        Assert.Equal(6.5 + Step, run.Sim.Entity(client.Replica.OwnEntityId).Position.Y, 6);
    }

    /// <summary>Сверх трёх вводов в очереди старые выбрасываются, чтобы задержка не копилась.</summary>
    [Fact]
    public void InputQueueDropsOldest()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);

        for (int i = 0; i < 5; i++)
        {
            client.SendInput(new MoveInput(1, 0));
        }

        run.Tick(1, client);

        Assert.Equal(3u, client.Replica.LastInput);
    }

    /// <summary>Без нового ввода сервер повторяет последний: сущность продолжает идти.</summary>
    [Fact]
    public void ServerRepeatsLastInputWhenQueueIsEmpty()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        client.SendInput(new MoveInput(1, 0));

        run.Tick(2, client);

        Assert.Equal(6.5 + (2 * Step), run.Sim.Entity(client.Replica.OwnEntityId).Position.X, 6);
    }

    /// <summary>Ускорение времени сервера приходит в приветствии, предсказание шагает с ним.</summary>
    [Fact]
    public void PredictionUsesServerTimeScale()
    {
        NetRun run = new(timeScale: 2);
        GameClient client = run.Connect();
        run.Tick(1, client);

        client.SendInput(new MoveInput(1, 0));

        Assert.Equal(0.2, client.Replica.SecondsPerTick, 9);
        Assert.Equal(6.5 + (2 * Step), client.OwnPosition!.Value.X, Tolerance);
    }

    /// <summary>Итог этапа 10 в sim: при задержке 100 мс туда-обратно своя сущность трогается сразу, а не через ответ сервера.</summary>
    [Fact]
    public void OwnMovementIsImmediateWithLag()
    {
        NetRun run = new();
        GameClient client = Lagged(run, 0.05);
        TickWithLag(run, client, MoveInput.None, 3);
        ReplicaEntity own = client.Replica.Entity(client.Replica.OwnEntityId)!;

        TickWithLag(run, client, new MoveInput(1, 0), 1);

        Assert.Equal(6.5, own.X, Tolerance);
        Assert.Equal(6.5 + Step, client.OwnPosition!.Value.X, Tolerance);
    }

    /// <summary>Когда сервер считает так же, предсказание после подтверждений не поправляется и совпадает с сервером.</summary>
    [Fact]
    public void MatchingPredictionNeedsNoCorrection()
    {
        NetRun run = new();
        GameClient client = Lagged(run, 0.05);
        TickWithLag(run, client, MoveInput.None, 3);

        for (int i = 0; i < 20; i++)
        {
            WorldPos before = client.OwnPosition!.Value;
            client.SendInput(new MoveInput(1, 0.5));
            WorldPos predicted = client.OwnPosition!.Value;
            _time += 0.1;
            run.Server.Tick();
            client.Update();

            // После сверки с сервером позиция та же, что предсказана при отправке
            Assert.NotEqual(before, predicted);
            Assert.Equal(predicted.X, client.OwnPosition!.Value.X, Tolerance);
            Assert.Equal(predicted.Y, client.OwnPosition!.Value.Y, Tolerance);
        }

        TickWithLag(run, client, MoveInput.None, 3);
        AssertMatchesServer(run, client);
    }

    /// <summary>Сервер закрыл дверь, которую клиент ещё видит открытой: предсказание проходит сквозь неё, затем поправляется.</summary>
    [Fact]
    public void ClosedDoorCorrectsPrediction()
    {
        NetRun run = new();
        GameClient client = Lagged(run, 0.3);
        CellPos doorCell = new(6, 8, 0);
        World world = run.Sim.World;
        world.SetEdge(doorCell, Side.South, world.GetEdge(doorCell, Side.South) with { Open = true });
        TickWithLag(run, client, MoveInput.None, 8);
        Entity own = run.Sim.Entity(client.Replica.OwnEntityId);

        // Стена комнаты на y = 9, тело радиусом 0.3 упирается в закрытую дверь на y = 8.7
        double deepest = 0;
        for (int i = 0; i < 30; i++)
        {
            if (own.Position.Y >= 8 && world.GetEdge(doorCell, Side.South).Open)
            {
                world.SetEdge(doorCell, Side.South, world.GetEdge(doorCell, Side.South) with { Open = false });
            }

            TickWithLag(run, client, new MoveInput(0, 1), 1);
            deepest = Math.Max(deepest, client.OwnPosition!.Value.Y);
        }

        TickWithLag(run, client, MoveInput.None, 8);

        Assert.True(deepest > 8.7 + Step, $"prediction went through the door: {deepest}");
        Assert.Equal(8.7, own.Position.Y, 6);
        AssertMatchesServer(run, client);
    }

    /// <summary>Сервер сам переставил сущность: предсказание переходит на новую позицию.</summary>
    [Fact]
    public void ServerTeleportCorrectsPrediction()
    {
        NetRun run = new();
        GameClient client = Lagged(run, 0.05);
        TickWithLag(run, client, MoveInput.None, 3);

        run.Sim.Entity(client.Replica.OwnEntityId).Position = new WorldPos(3.5, 3.5, 0);
        TickWithLag(run, client, MoveInput.None, 3);

        AssertMatchesServer(run, client);
        Assert.Equal(3.5, client.OwnPosition!.Value.X, Tolerance);
    }

    /// <summary>Предсказание без своей позиции не двигается и не копит ввод.</summary>
    [Fact]
    public void PredictionWaitsForOwnPosition()
    {
        MovePrediction prediction = new();

        prediction.Add(1, new MoveInput(1, 0), new World(1, 1), 0.1, MoveParams.Default);

        Assert.Null(prediction.Position);
        Assert.Equal(0, prediction.PendingCount);
    }

    /// <summary>Сверка убирает подтверждённые вводы и прогоняет остальные от позиции сервера.</summary>
    [Fact]
    public void ReconcileReplaysUnconfirmedInputs()
    {
        // Пол стартовой комнаты: в мире без пола ходить нельзя
        World world = new NetRun().Sim.World;
        MovePrediction prediction = new();
        prediction.Reconcile(0, new WorldPos(1, 1, 0), world, 0.1, MoveParams.Default);
        prediction.Add(1, new MoveInput(1, 0), world, 0.1, MoveParams.Default);
        prediction.Add(2, new MoveInput(1, 0), world, 0.1, MoveParams.Default);
        prediction.Add(3, new MoveInput(0, 1), world, 0.1, MoveParams.Default);

        // Посреди комнаты, подальше от мебели
        prediction.Reconcile(1, new WorldPos(7, 6, 0), world, 0.1, MoveParams.Default);

        Assert.Equal(2, prediction.PendingCount);
        Assert.Equal(7 + Step, prediction.Position!.Value.X, 9);
        Assert.Equal(6 + Step, prediction.Position!.Value.Y, 9);
    }

    /// <summary>Своя позиция у клиента совпадает с сервером.</summary>
    private static void AssertMatchesServer(NetRun run, GameClient client)
    {
        WorldPos server = run.Sim.Entity(client.Replica.OwnEntityId).Position;
        Assert.Equal(server.X, client.OwnPosition!.Value.X, Tolerance);
        Assert.Equal(server.Y, client.OwnPosition!.Value.Y, Tolerance);
    }

    /// <summary>Клиент с задержкой oneWay секунд в каждую сторону.</summary>
    private GameClient Lagged(NetRun run, double oneWay)
    {
        LaggedConnection connection = new(run.ConnectRaw(), oneWay, () => _time);
        return run.Connect(connection, "lagged");
    }

    /// <summary>Тики как в игре: время идёт, клиент шлёт ввод, сервер тикает, клиент принимает.</summary>
    private void TickWithLag(NetRun run, GameClient client, MoveInput input, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _time += 0.1;
            client.SendInput(input);
            run.Server.Tick();
            client.Update();
        }
    }
}
