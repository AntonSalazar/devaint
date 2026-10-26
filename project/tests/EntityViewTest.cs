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

    /// <summary>Полоса прогресса видна во время действия и прячется без него.</summary>
    public void TestActionBarShowsProgress()
    {
        EntityView view = new();
        view.Init(1, own: true, Vector3.Zero, Colors.White);
        Check(!view.ActionBarVisible, "hidden at start");

        view.SetActionProgress(0.4f);
        Check(view.ActionBarVisible, "visible during action");
        CheckEqual(0.4f, view.ActionProgress, "progress");

        view.SetActionProgress(null);
        Check(!view.ActionBarVisible, "hidden after action");
        view.Free();
    }

    /// <summary>За полтика капсула проходит половину пути, за тик и дальше стоит в цели.</summary>
    public void TestInterpolatesOverOneTick()
    {
        EntityView view = new();
        view.Init(1, own: true, Vector3.Zero, Colors.White);

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
        view.Init(1, own: false, Vector3.Zero, Colors.White);
        view.SetTarget(new Vector3(1, 0, 0));
        view.Advance(0.05);

        view.SetTarget(new Vector3(1, 0, 1));
        CheckNear(0.5, view.Position.X, Tolerance, "no jump on retarget");
        view.Advance(0.05);

        CheckNear(0.75, view.Position.X, Tolerance, "x after retarget");
        CheckNear(0.5, view.Position.Z, Tolerance, "z after retarget");
        view.Free();
    }

    /// <summary>Кольцо только под своей капсулой, цвет тела из вида.</summary>
    public void TestRingAndColor()
    {
        EntityView own = new();
        own.Init(1, own: true, Vector3.Zero, Colors.Blue);
        EntityView other = new();
        other.Init(2, own: false, Vector3.Zero, Colors.Green);

        Check(own.HasRing, "own has a ring");
        Check(!other.HasRing, "other has no ring");
        CheckEqual(Colors.Green, other.BodyColor, "body color");
        own.Free();
        other.Free();
    }

    /// <summary>От первого лица своя капсула только отбрасывает тень и теряет кольцо; обратно - всё как было.</summary>
    public void TestFirstPerson()
    {
        EntityView own = new();
        own.Init(1, true, Vector3.Zero, Colors.Blue);
        GeometryInstance3D body = own.GetNode<GeometryInstance3D>("Visual/Body");

        own.SetFirstPerson(true);
        GeometryInstance3D.ShadowCastingSetting hidden = body.CastShadow;
        bool ringHidden = !own.GetNode<Node3D>("Ring").Visible;
        own.SetFirstPerson(false);

        CheckEqual(GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, hidden, "body only casts a shadow");
        Check(ringHidden, "ring is hidden");
        CheckEqual(GeometryInstance3D.ShadowCastingSetting.On, body.CastShadow, "body is back");
        Check(own.GetNode<Node3D>("Ring").Visible, "ring is back");
        own.Free();
    }

    /// <summary>Чужая капсула смотрит по движению, своя - только по SetFacing, короткое направление взгляд не меняет.</summary>
    public void TestFacing()
    {
        EntityView own = new();
        own.Init(1, own: true, Vector3.Zero, Colors.Blue);
        EntityView other = new();
        other.Init(2, own: false, Vector3.Zero, Colors.Green);

        other.SetTarget(new Vector3(1, 0, 0));
        own.SetTarget(new Vector3(1, 0, 0));
        CheckNear(1, other.Facing.X, Tolerance, "other faces east");
        CheckNear(1, own.Facing.Z, Tolerance, "own keeps facing south");

        own.SetFacing(new Vector3(0, 0, -1));
        own.SetFacing(new Vector3(0.0001f, 0, 0));
        CheckNear(-1, own.Facing.Z, Tolerance, "own faces north after input");
        own.Free();
        other.Free();
    }

    /// <summary>Выпад сдвигает тело вперёд и возвращает, вспышка красит тело и гаснет.</summary>
    public void TestLungeAndFlash()
    {
        EntityView view = new();
        view.Init(1, own: false, Vector3.Zero, Colors.Green);

        view.PlayAttack();
        view.PlayHit();
        view.Advance(EntityView.LungeSeconds / 2);
        float middle = view.LungeOffset;
        bool flashing = view.IsFlashing && view.BodyColor != Colors.Green;
        view.Advance(1);

        Check(middle > 0.2f, $"body moved forward by {middle}");
        Check(flashing, "flashes red");
        CheckNear(0, view.LungeOffset, Tolerance, "body is back");
        Check(!view.IsFlashing && view.BodyColor == Colors.Green, "flash is over");
        view.Free();
    }
}
