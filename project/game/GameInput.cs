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

    /// <summary>Приблизить камеру.</summary>
    public const string ZoomIn = "zoom_in";

    /// <summary>Отдалить камеру.</summary>
    public const string ZoomOut = "zoom_out";

    /// <summary>Выйти из игры.</summary>
    public const string Quit = "quit";

    /// <summary>Следующий режим камеры.</summary>
    public const string CameraMode = "camera_mode";

    /// <summary>Куда смотрит прицел, если луч не попал в пол рядом: столько метров вперёд по взгляду.</summary>
    public const float AimFallbackDistance = 10f;

    /// <summary>Дальше этого по земле точка на полу для прицела не берётся.</summary>
    public const float AimMaxDistance = 30f;

    /// <summary>Регистрация движения WASD и стрелками, зума колёсиком, смены камеры на V и выхода по Esc, если их ещё нет; остальное управление задают моды.</summary>
    public static void EnsureActions()
    {
        Add(Up, Key.W, Key.Up);
        Add(Down, Key.S, Key.Down);
        Add(Left, Key.A, Key.Left);
        Add(Right, Key.D, Key.Right);
        Add(Quit, Key.Escape);
        Add(CameraMode, Key.V);
        AddMouse(ZoomIn, MouseButton.WheelUp);
        AddMouse(ZoomOut, MouseButton.WheelDown);
    }

    /// <summary>Щелчки зума в событии: плюс - приближение, минус - отдаление, 0 - событие не про зум.</summary>
    public static int ZoomSteps(InputEvent input)
    {
        if (input.IsActionPressed(ZoomIn))
        {
            return 1;
        }

        return input.IsActionPressed(ZoomOut) ? -1 : 0;
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

    /// <summary>Точка, где луч камеры пересекает горизонтальную плоскость высоты height; null, если луч идёт вдоль неё.</summary>
    public static Vector3? GroundPoint(Vector3 origin, Vector3 direction, float height)
    {
        if (Mathf.Abs(direction.Y) < 1e-6f)
        {
            return null;
        }

        float distance = (height - origin.Y) / direction.Y;
        return origin + (direction * distance);
    }

    /// <summary>Точка прицела на полу высоты height по лучу камеры; если пол дальше AimMaxDistance или луч смотрит выше горизонта - точка в AimFallbackDistance вперёд по взгляду.</summary>
    public static Vector3 AimPoint(Vector3 origin, Vector3 direction, float height)
    {
        // Луч назад от камеры попадает в пол за спиной: такая точка тоже не годится
        if (direction.Y < 0 && GroundPoint(origin, direction, height) is Vector3 ground)
        {
            Vector3 along = ground - origin;
            if (new Vector2(along.X, along.Z).Length() <= AimMaxDistance)
            {
                return ground;
            }
        }

        Vector3 ahead = origin + (Flat(direction) * AimFallbackDistance);
        return new Vector3(ahead.X, height, ahead.Z);
    }

    /// <summary>Вектор по земле единичной длины, ноль если он смотрит строго вверх или вниз.</summary>
    private static Vector3 Flat(Vector3 vector)
    {
        Vector3 flat = new(vector.X, 0, vector.Z);
        return flat.LengthSquared() > 1e-6f ? flat.Normalized() : Vector3.Zero;
    }

    /// <summary>Действие с кнопкой мыши, если его нет.</summary>
    private static void AddMouse(string action, MouseButton button)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
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
