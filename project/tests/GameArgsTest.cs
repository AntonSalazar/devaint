/// <summary>Тесты разбора аргументов <see cref="GameArgs"/>.</summary>
public class GameArgsTest : TestCase
{
    /// <summary>Ускорение берётся из --time-scale N, неверное или отсутствующее значение - 1.</summary>
    public void TestTimeScale()
    {
        CheckEqual(200.0, GameArgs.TimeScale(["--time-scale", "200"]), "valid");
        CheckEqual(0.5, GameArgs.TimeScale(["--autowalk", "--time-scale", "0.5"]), "fraction after other args");
        CheckEqual(1.0, GameArgs.TimeScale([]), "missing");
        CheckEqual(1.0, GameArgs.TimeScale(["--time-scale"]), "no value");
        CheckEqual(1.0, GameArgs.TimeScale(["--time-scale", "fast"]), "not a number");
        CheckEqual(1.0, GameArgs.TimeScale(["--time-scale", "-3"]), "negative");
        CheckEqual(1.0, GameArgs.TimeScale(["--time-scale", "Infinity"]), "infinite");
    }

    /// <summary>Порт из --port N в пределах 1..65535, иначе порт по умолчанию.</summary>
    public void TestPort()
    {
        CheckEqual(9000, GameArgs.Port(["--port", "9000"]), "valid");
        CheckEqual(GameArgs.DefaultPort, GameArgs.Port([]), "missing");
        CheckEqual(GameArgs.DefaultPort, GameArgs.Port(["--port", "0"]), "zero");
        CheckEqual(GameArgs.DefaultPort, GameArgs.Port(["--port", "70000"]), "too large");
    }

    /// <summary>Адрес из --connect: хост с портом, без порта или с неверным портом.</summary>
    public void TestConnect()
    {
        CheckEqual(("127.0.0.1", 9000), GameArgs.Connect(["--connect", "127.0.0.1:9000"]), "host and port");
        CheckEqual(("example.org", GameArgs.DefaultPort), GameArgs.Connect(["--connect", "example.org"]), "host only");
        CheckEqual(("::1", GameArgs.DefaultPort), GameArgs.Connect(["--connect", "::1"]), "ipv6 without port");
        CheckEqual(null, GameArgs.Connect([]), "missing");
        CheckEqual(null, GameArgs.Connect(["--connect"]), "no value");
    }

    /// <summary>Имя сохранения из --save, по умолчанию world; id игрока из --player или null.</summary>
    public void TestSaveAndPlayer()
    {
        CheckEqual("test-1", GameArgs.SaveName(["--save", "test-1"]), "save name");
        CheckEqual(GameArgs.DefaultSave, GameArgs.SaveName([]), "default save");
        CheckEqual("bob", GameArgs.Player(["--connect", "127.0.0.1", "--player", "bob"]), "player");
        CheckEqual(null, GameArgs.Player([]), "no player");
    }

    /// <summary>Задержка из --lag N в миллисекундах, неверная - 0.</summary>
    public void TestLag()
    {
        CheckEqual(100.0, GameArgs.LagMs(["--lag", "100"]), "valid");
        CheckEqual(0.0, GameArgs.LagMs([]), "missing");
        CheckEqual(0.0, GameArgs.LagMs(["--lag", "-5"]), "negative");
    }
}
