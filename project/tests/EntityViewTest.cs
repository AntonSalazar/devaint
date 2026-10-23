using Godot;

/// <summary>Тесты интерполяции <see cref="EntityView"/>.</summary>
public class EntityViewTest : TestCase
{
    /// <summary>Допуск координат.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>Капсула тоньше тела sim настолько, что не входит в стену: иначе она ловит свет снаружи.</summary>
    public void TestCapsuleFitsBesideWall()
    {
        double gap = DevAInt.Sim.Motion.MoveParams.Default.Radius - (ChunkMesh.WallThickness / 2) - EntityView.Radius;

        Check(gap > 0.01, $"capsule surface stays {gap} m away from the wall face");
    }

    /// <summary>За полтика капсула проходит половину пути, за тик и дальше стоит в цели.</summary>
    public void TestInterpolatesOverOneTick()
    {
        EntityView view = new();
        view.Init(1, own: true, Vector3.Zero);

        view.SetTarget(new Vector3(1, 0, 0));
        view.Advance(0.05);
        CheckNear(0.5, view.Position.X, Tolerance, "half way");

        view.Advance(1);
        CheckNear(1, view.Position.X, Tolerance, "at target");
        view.Free();
    }

    /// <summary>Новая цель посреди пути начинается с текущей позиции, без рывка назад.</summary>
    public void TestRetargetStartsFromCurrent()
    {
        EntityView view = new();
        view.Init(1, own: false, Vector3.Zero);
        view.SetTarget(new Vector3(1, 0, 0));
        view.Advance(0.05);

        view.SetTarget(new Vector3(1, 0, 1));
        CheckNear(0.5, view.Position.X, Tolerance, "no jump on retarget");
        view.Advance(0.05);

        CheckNear(0.75, view.Position.X, Tolerance, "x after retarget");
        CheckNear(0.5, view.Position.Z, Tolerance, "z after retarget");
        view.Free();
    }
}
