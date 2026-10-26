using System.Linq;
using DevAInt.Sim.Grid;
using Godot;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Тесты режимов камеры: переключение по кругу, камера из-за плеча и от первого лица, упор в стену.</summary>
public class CameraTest : TestCase
{
    /// <summary>Допуск сравнения координат.</summary>
    private const double Tolerance = 1e-3;

    /// <summary>Ноги для тестов.</summary>
    private static readonly Vector3 _feet = new(5.5f, 0, 5.5f);

    /// <summary>Режимы идут по кругу, текущая только одна камера; режим при запуске берётся по имени.</summary>
    public void TestRigCyclesModes()
    {
        CameraRig rig = new();
        rig.Init();
        string first = rig.Mode;
        Check(rig.Camera is IsoCamera && rig.Camera.Current, "iso is current at start");
        rig.Next(_feet, Vector3.Back);
        string second = rig.Mode;
        bool onlyOne = rig.GetChildren().OfType<Camera3D>().Count(camera => camera.Current) == 1;
        rig.Next(_feet, Vector3.Back);
        string third = rig.Mode;
        rig.Next(_feet, Vector3.Back);

        CheckEqual("iso shoulder first iso", $"{first} {second} {third} {rig.Mode}", "order");
        Check(onlyOne, "only one camera is current");
        CheckEqual(2, CameraRig.ModeIndex("first"), "mode by name");
        CheckEqual(null, CameraRig.ModeIndex("top"), "unknown mode");
        rig.Free();

        CameraRig shoulder = new();
        shoulder.Init("shoulder");
        Check(shoulder.Camera is ShoulderCamera, "start mode from the name");
        shoulder.Free();
    }

    /// <summary>Туман и срез стен только в изометрии, мышь захвачена в остальных, от первого лица капсулы не видно и персонаж смотрит за камерой.</summary>
    public void TestModeRules()
    {
        CameraRig rig = new();
        rig.Init();
        GameCamera iso = rig.Camera;
        rig.Next(_feet, Vector3.Back);
        GameCamera shoulder = rig.Camera;
        rig.Next(_feet, Vector3.Back);
        GameCamera first = rig.Camera;

        Check(iso.UsesFog && iso.CutsWalls && !iso.CapturesMouse, "iso has fog and cutaway, free mouse");
        Check(!shoulder.UsesFog && !shoulder.CutsWalls && shoulder.CapturesMouse && shoulder.ShowsOwnBody && !shoulder.FacesAim, "shoulder");
        Check(!first.UsesFog && first.CapturesMouse && !first.ShowsOwnBody && first.FacesAim, "first person");
        CheckEqual(new Vector2(3, 4), iso.AimScreenPoint(new Vector2(3, 4), new Vector2(100, 50)), "iso aims at the mouse");
        CheckEqual(new Vector2(50, 25), shoulder.AimScreenPoint(new Vector2(3, 4), new Vector2(100, 50)), "shoulder aims at the centre");
        rig.Free();
    }

    /// <summary>Камера из-за плеча встаёт сзади по взгляду, правее и на высоте точки вращения.</summary>
    public void TestShoulderStandsBehindRightShoulder()
    {
        ShoulderCamera camera = Shoulder();

        // Взгляд на юг, +Z: сзади - меньший z, справа от персонажа - меньший x
        CheckNear(1, (-camera.Basis.Z).Dot(Vector3.Back), Tolerance, "looks along the facing");
        CheckNear(_feet.Z - ShoulderCamera.DefaultDistance, camera.Position.Z, Tolerance, "behind");
        CheckNear(_feet.X - ShoulderCamera.ShoulderOffset, camera.Position.X, Tolerance, "right shoulder");
        CheckNear(ShoulderCamera.PivotHeight, camera.Position.Y, Tolerance, "pivot height");
        camera.Free();
    }

    /// <summary>Колёсико меняет расстояние в пределах, прицел плавно приближает и сужает угол обзора.</summary>
    public void TestShoulderZoomAndAim()
    {
        ShoulderCamera camera = Shoulder();

        camera.Zoom(1);
        float closer = camera.Distance;
        camera.Zoom(100);
        float closest = camera.Distance;
        camera.Zoom(-100);
        float farthest = camera.Distance;
        camera.Follow(_feet, null, true, 0.1);
        float halfFov = camera.Fov;
        camera.Follow(_feet, null, true, 1);
        Vector3 pivot = _feet + (Vector3.Up * ShoulderCamera.PivotHeight);

        CheckNear(ShoulderCamera.DefaultDistance - ShoulderCamera.ZoomStep, closer, Tolerance, "one step closer");
        CheckEqual(ShoulderCamera.MinDistance, closest, "closest");
        CheckEqual(ShoulderCamera.MaxDistance, farthest, "farthest");
        CheckNear((ShoulderCamera.NormalFov + ShoulderCamera.AimFov) / 2, halfFov, Tolerance, "halfway into aim");
        CheckEqual(ShoulderCamera.AimFov, camera.Fov, "aim field of view");
        CheckNear(new Vector2(ShoulderCamera.ShoulderOffset, ShoulderCamera.AimDistance).Length(), camera.Position.DistanceTo(pivot), Tolerance, "aim distance");
        camera.Free();
    }

    /// <summary>Мышь вправо поворачивает направо, вниз - наклоняет вниз, но не дальше предела.</summary>
    public void TestShoulderTurn()
    {
        ShoulderCamera camera = Shoulder();
        float yaw = camera.Yaw;

        camera.Turn(new Vector2(100, 0));
        float turned = camera.Yaw;
        camera.Turn(new Vector2(0, 100_000));

        CheckNear(yaw - (100 * MouseLookCamera.Sensitivity), turned, Tolerance, "yaw");
        CheckNear(Mathf.DegToRad(-60), camera.Pitch, Tolerance, "lowest pitch");
        camera.Free();
    }

    /// <summary>Стена за спиной: камера останавливается перед ней с зазором и не заходит за неё.</summary>
    public void TestShoulderStopsBeforeWall()
    {
        World world = new(16, 16);
        world.SetEdge(new CellPos(5, 5, 0), Side.North, new Edge(1, false));
        ShoulderCamera camera = Shoulder();

        camera.Follow(_feet, world, false, 0);

        // Стена на линии z = 5 между ногами на 5.5 и желаемой камерой на 3
        Vector3 pivot = _feet + (Vector3.Up * ShoulderCamera.PivotHeight);
        Vector3 desired = pivot + (camera.Basis.X * ShoulderCamera.ShoulderOffset) + (camera.Basis.Z * ShoulderCamera.DefaultDistance);
        float toWall = pivot.DistanceTo(desired) * (0.5f / ShoulderCamera.DefaultDistance);
        Check(camera.Position.Z > 5, $"camera z {camera.Position.Z} stays in front of the wall");
        CheckNear(toWall - ShoulderCamera.WallMargin, pivot.DistanceTo(camera.Position), Tolerance, "margin before the wall");
        CheckEqual(desired, ShoulderCamera.KeepOutOfWalls(null, pivot, desired, 0), "without a world nothing blocks");
        camera.Free();
    }

    /// <summary>От первого лица камера в глазах и смотрит по взгляду, прицел сужает угол обзора.</summary>
    public void TestFirstPersonInEyes()
    {
        FirstPersonCamera camera = new();
        camera.Init();

        camera.Place(_feet, Vector3.Right);
        Vector3 eyes = camera.Position;
        camera.Follow(_feet, null, true, 1);

        CheckEqual(_feet + (Vector3.Up * FirstPersonCamera.EyeHeight), eyes, "eyes");
        CheckNear(1, (-camera.Basis.Z).Dot(Vector3.Right), Tolerance, "looks along the facing");
        CheckEqual(FirstPersonCamera.AimFov, camera.Fov, "aim field of view");
        camera.Free();
    }

    /// <summary>Туман по глубине не трогает кадр изометрии и закрывает всё к дальности других камер.</summary>
    public void TestFogFitsCameras()
    {
        IsoCamera iso = new();
        iso.Init(Vector3.Zero);
        ShoulderCamera shoulder = Shoulder();
        WorldEnvironment environment = Lighting.CreateEnvironment();

        Check(environment.Environment.FogEnabled, "fog is on");
        Check(environment.Environment.FogDepthBegin >= iso.Far, "iso frame is closer than the fog");
        CheckEqual(shoulder.Far, environment.Environment.FogDepthEnd, "fog covers everything at the far plane");
        CheckEqual(Lighting.Background, environment.Environment.FogLightColor, "fog has the background colour");
        iso.Free();
        shoulder.Free();
        environment.Free();
    }

    /// <summary>Камера из-за плеча на ногах _feet со взглядом на юг.</summary>
    private static ShoulderCamera Shoulder()
    {
        ShoulderCamera camera = new();
        camera.Init();
        camera.Place(_feet, Vector3.Back);
        return camera;
    }
}
