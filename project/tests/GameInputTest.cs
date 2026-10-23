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
}
