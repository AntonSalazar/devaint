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

    /// <summary>Первое применение: первое яблоко и действие core:eat; без предметов - ничего.</summary>
    public void TestFirstUseIsEatingApple()
    {
        GameFixture game = new();
        CheckEqual<(long, int)?>(null, GameInput.FirstUse(game.Client.Replica, game.Sim.ItemTable), "nothing before inventory arrives");
        game.Tick();

        (long ItemId, int Action)? use = GameInput.FirstUse(game.Client.Replica, game.Sim.ItemTable);

        CheckEqual(game.Client.Replica.Inventory[0].Id, use?.ItemId ?? -1, "first apple");
        CheckEqual(game.Sim.ItemTable.ActionIndex("core:eat"), use?.Action ?? -1, "eat action");
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

        CheckEqual(1, InputMap.ActionGetEvents(GameInput.Use).Count, "use key");
    }

    /// <summary>Удар - на пробеле.</summary>
    public void TestAttackOnSpace()
    {
        GameInput.EnsureActions();
        InputEventKey space = new() { PhysicalKeycode = Key.Space, Pressed = true };

        Check(InputMap.HasAction(GameInput.Attack), "attack action exists");
        Check(space.IsAction(GameInput.Attack), "space is attack");
    }
}
