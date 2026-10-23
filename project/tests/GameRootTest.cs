/// <summary>Тесты разбора аргументов <see cref="GameRoot"/>.</summary>
public class GameRootTest : TestCase
{
    /// <summary>Ускорение берётся из --time-scale N, неверное или отсутствующее значение - 1.</summary>
    public void TestTimeScale()
    {
        CheckEqual(200.0, GameRoot.TimeScale(["--time-scale", "200"]), "valid");
        CheckEqual(0.5, GameRoot.TimeScale(["--autowalk", "--time-scale", "0.5"]), "fraction after other args");
        CheckEqual(1.0, GameRoot.TimeScale([]), "missing");
        CheckEqual(1.0, GameRoot.TimeScale(["--time-scale"]), "no value");
        CheckEqual(1.0, GameRoot.TimeScale(["--time-scale", "fast"]), "not a number");
        CheckEqual(1.0, GameRoot.TimeScale(["--time-scale", "-3"]), "negative");
    }
}
