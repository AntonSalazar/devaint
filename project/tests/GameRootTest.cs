using System.Collections.Generic;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Net;

/// <summary>Тесты проверок подключения и приёма пакетов <see cref="GameRoot"/>.</summary>
public class GameRootTest : TestCase
{
    /// <summary>Подключение живо или ещё идёт в пределах таймаута - ошибки нет.</summary>
    public void TestNoProblemWhileConnectedOrConnecting()
    {
        CheckEqual(null, GameRoot.ConnectionProblem(connecting: false, connected: true, wasConnected: true, elapsed: 100), "connected");
        CheckEqual(null, GameRoot.ConnectionProblem(connecting: true, connected: false, wasConnected: false, elapsed: 1), "connecting");
    }

    /// <summary>Долгое подключение, отказ и потеря связи дают разные ошибки.</summary>
    public void TestProblems()
    {
        double late = GameRoot.ConnectTimeoutSeconds + 1;
        CheckEqual("cannot connect to the server: timed out", GameRoot.ConnectionProblem(true, false, false, late), "timeout");
        CheckEqual("cannot connect to the server: refused", GameRoot.ConnectionProblem(false, false, false, 1), "refused");
        CheckEqual("lost connection to the server", GameRoot.ConnectionProblem(false, false, true, 100), "lost");
    }

    /// <summary>Пакеты с теми же модами принимаются без ошибок.</summary>
    public void TestUpdateWithSameMods()
    {
        GameFixture game = new();
        ListLog log = new();
        game.Server.Tick();

        Check(GameRoot.TryUpdate(game.Client, log), "update succeeded");
        CheckEqual(0, log.Errors.Count, "errors");
    }

    /// <summary>Сервер с другими модами: приём даёт false и понятную ошибку в лог, исключение не уходит наружу.</summary>
    public void TestUpdateWithDifferentModsFails()
    {
        GameFixture game = new();
        LoopbackTransport transport = new();
        GameServer server = new(game.Sim, transport, new GodotLog());
        ModInfo stale = game.Sim.Mods[0] with { Hash = "stale" };
        GameClient client = new(transport.Connect(), game.Sim.StatTable, game.Sim.ItemTable, [stale]);
        ListLog log = new();
        server.Tick();

        Check(!GameRoot.TryUpdate(client, log), "update failed");
        CheckEqual("mods differ from the server: mod core 0.1.0 has different data", log.Errors.Count == 1 ? log.Errors[0] : "none", "error text");
    }

    /// <summary>Лог, который запоминает сообщения.</summary>
    private sealed class ListLog : ISimLog
    {
        /// <summary>Ошибки по порядку.</summary>
        public List<string> Errors { get; } = [];

        /// <inheritdoc/>
        public void Info(string message)
        {
        }

        /// <inheritdoc/>
        public void Error(string message) => Errors.Add(message);
    }
}
