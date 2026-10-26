using DevAInt.Sim.Motion;
using Godot;

/// <summary>Тесты <see cref="GameInput"/>: клавиши в направление относительно камеры.</summary>
public class GameInputTest : TestCase
{
    /// <summary>Допуск сравнения направлений.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>У изометрической камеры вверх по экрану - на северо-запад, вправо - на северо-восток.</summary>
    public void TestIsoCameraDirections()
    {
        IsoCamera camera = new();
        camera.Init(Vector3.Zero);
        double half = System.Math.Sqrt(0.5);

        MoveInput up = GameInput.ToMoveInput(new Vector2(0, 1), camera.Basis);
        MoveInput right = GameInput.ToMoveInput(new Vector2(1, 0), camera.Basis);

        CheckNear(-half, up.X, Tolerance, "up x");
        CheckNear(-half, up.Y, Tolerance, "up y");
        CheckNear(half, right.X, Tolerance, "right x");
        CheckNear(-half, right.Y, Tolerance, "right y");
        camera.Free();
    }

    /// <summary>Камера, смотрящая на север: вверх по экрану - на север, по y sim это минус.</summary>
    public void TestNorthCamera()
    {
        MoveInput up = GameInput.ToMoveInput(new Vector2(0, 1), Basis.Identity);

        CheckNear(0, up.X, Tolerance, "up x");
        CheckNear(-1, up.Y, Tolerance, "up y");
    }

    /// <summary>Без клавиш движения нет.</summary>
    public void TestNoKeysNoMove() => CheckEqual(MoveInput.None, GameInput.ToMoveInput(Vector2.Zero, Basis.Identity), "no keys");

    /// <summary>Колёсико вверх - щелчок приближения, вниз - отдаления, другое событие - не зум.</summary>
    public void TestWheelGivesZoomSteps()
    {
        GameInput.EnsureActions();

        CheckEqual(1, GameInput.ZoomSteps(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true }), "wheel up");
        CheckEqual(-1, GameInput.ZoomSteps(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true }), "wheel down");
        CheckEqual(0, GameInput.ZoomSteps(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }), "left button");
        CheckEqual(0, GameInput.ZoomSteps(new InputEventKey { PhysicalKeycode = Key.E, Pressed = true }), "key");
    }

    /// <summary>Esc - выход.</summary>
    public void TestEscapeQuits()
    {
        GameInput.EnsureActions();

        Check(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true }.IsActionPressed(GameInput.Quit), "escape is quit");
    }

    /// <summary>Зум меняет кадр шагом и держится в пределах, где тени ещё не пропускают свет сквозь стены.</summary>
    public void TestCameraZoomIsClamped()
    {
        IsoCamera camera = new();
        camera.Init(Vector3.Zero);

        camera.Zoom(1);
        CheckNear(IsoCamera.DefaultSize / IsoCamera.ZoomStep, camera.Size, 1e-4, "one step in");
        camera.Zoom(-2);
        CheckNear(IsoCamera.DefaultSize * IsoCamera.ZoomStep, camera.Size, 1e-4, "one step out from start");
        camera.Zoom(100);
        CheckEqual(IsoCamera.MinSize, camera.Size, "closest");
        camera.Zoom(-100);
        CheckEqual(IsoCamera.MaxSize, camera.Size, "farthest");
        camera.Free();
    }

    /// <summary>Луч из центра изометрической камеры попадает на землю в точку, на которую она смотрит; луч вдоль земли - null.</summary>
    public void TestGroundPoint()
    {
        IsoCamera camera = new();
        camera.Init(new Vector3(5, 0, 7));

        Vector3? point = GameInput.GroundPoint(camera.Position, -camera.Basis.Z, 0);
        Vector3? upper = GameInput.GroundPoint(camera.Position, -camera.Basis.Z, 3);

        CheckNear(5, point?.X ?? 0, 1e-3, "x");
        CheckNear(7, point?.Z ?? 0, 1e-3, "z");
        CheckNear(3, upper?.Y ?? 0, 1e-3, "upper floor height");
        CheckEqual(null, GameInput.GroundPoint(Vector3.Up, Vector3.Forward, 0), "ray along the ground");
        camera.Free();
    }

    /// <summary>Прицел: луч вниз - точка на полу; луч вверх, назад или на пол слишком далеко - точка в 10 м вперёд по взгляду.</summary>
    public void TestAimPoint()
    {
        Vector3 eye = new(0, 1.6f, 0);

        Vector3 down = GameInput.AimPoint(eye, new Vector3(0, -1.6f, 2).Normalized(), 0);
        Vector3 up = GameInput.AimPoint(eye, new Vector3(0, 1, 1).Normalized(), 0);
        Vector3 far = GameInput.AimPoint(eye, new Vector3(0, -0.01f, 1).Normalized(), 0);

        CheckEqual(new Vector3(0, 0, 2), Round(down), "floor point");
        CheckEqual(new Vector3(0, 0, GameInput.AimFallbackDistance), Round(up), "above the horizon");
        CheckEqual(new Vector3(0, 0, GameInput.AimFallbackDistance), Round(far), "floor too far");
    }

    /// <summary>Действия регистрируются один раз с клавишами WASD и стрелками.</summary>
    public void TestActionsAreRegistered()
    {
        GameInput.EnsureActions();
        GameInput.EnsureActions();

        foreach (string action in new[] { GameInput.Up, GameInput.Down, GameInput.Left, GameInput.Right })
        {
            Check(InputMap.HasAction(action), $"action {action} exists");
            CheckEqual(2, InputMap.ActionGetEvents(action).Count, $"keys of {action}");
        }

    }

    /// <summary>Вектор, округлённый до тысячных.</summary>
    private static Vector3 Round(Vector3 vector) => new(Mathf.Snapped(vector.X, 1e-3f), Mathf.Snapped(vector.Y, 1e-3f), Mathf.Snapped(vector.Z, 1e-3f));
}
