using DevAInt.Sim.Motion;
using Godot;

/// <summary>Ввод игрока: действия движения и пересчёт клавиш в направление относительно камеры.</summary>
public static class GameInput
{
    /// <summary>Действие вверх по экрану.</summary>
    public const string Up = "move_up";

    /// <summary>Действие вниз по экрану.</summary>
    public const string Down = "move_down";

    /// <summary>Действие влево.</summary>
    public const string Left = "move_left";

    /// <summary>Действие вправо.</summary>
    public const string Right = "move_right";

    /// <summary>Регистрация действий WASD и стрелок, если их ещё нет.</summary>
    public static void EnsureActions()
    {
        Add(Up, Key.W, Key.Up);
        Add(Down, Key.S, Key.Down);
        Add(Left, Key.A, Key.Left);
        Add(Right, Key.D, Key.Right);
    }

    /// <summary>Нажатые клавиши: x вправо, y вверх по экрану.</summary>
    public static Vector2 ReadKeys()
    {
        float x = Input.GetActionStrength(Right) - Input.GetActionStrength(Left);
        float y = Input.GetActionStrength(Up) - Input.GetActionStrength(Down);
        return new Vector2(x, y);
    }

    /// <summary>Клавиши в направление по миру: вверх по экрану - куда смотрит камера, по земле.</summary>
    public static MoveInput ToMoveInput(Vector2 keys, Basis camera)
    {
        // Ось y мира sim лежит вдоль оси Z Godot, поэтому берётся проекция камеры на землю XZ
        Vector3 forward = Flat(-camera.Z);
        Vector3 right = Flat(camera.X);
        Vector3 direction = (right * keys.X) + (forward * keys.Y);
        return new MoveInput(direction.X, direction.Z);
    }

    /// <summary>Вектор по земле единичной длины, ноль если он смотрит строго вверх или вниз.</summary>
    private static Vector3 Flat(Vector3 vector)
    {
        Vector3 flat = new(vector.X, 0, vector.Z);
        return flat.LengthSquared() > 1e-6f ? flat.Normalized() : Vector3.Zero;
    }

    /// <summary>Действие с клавишами, если его нет.</summary>
    private static void Add(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        foreach (Key key in keys)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }
}
