/// <summary>Тесты проверки подключения <see cref="GameRoot"/>.</summary>
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
}
