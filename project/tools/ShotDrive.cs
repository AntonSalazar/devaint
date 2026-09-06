using System.Collections.Generic;
using Godot;

/// <summary>
/// Управляемый скриншот. Запуск: `just shot-drive`.
/// Поднимает main.tscn, разгоняет время и ведет робота input-действиями к вышке,
/// пока Movie Maker пишет кадры: на последних кадрах видны заполненная шкала
/// заметности, уровень и лог. Движение - через Input.ActionPress,
/// время - через InputEventAction в очередь ввода.
/// </summary>
public partial class ShotDrive : SceneTree
{
    /// <summary>Кадр, на котором разгоняем время.</summary>
    private const int SpeedUpFrame = 5;

    /// <summary>Сколько раз нажать ускорение (x2 -> x4 -> x8 -> x16).</summary>
    private const int SpeedUpPresses = 4;

    /// <summary>Кадр, с которого ведем робота.</summary>
    private const int DriveFromFrame = 10;

    /// <summary>Дистанция остановки у цели, px.</summary>
    private const float ArriveDistance = 24.0f;

    /// <summary>Запасная клетка-цель, если в мире нет маркеров вышек.</summary>
    private static readonly Vector2I _fallbackCell = new(4, 12);

    /// <summary>Действия движения по осям экрана.</summary>
    private static readonly (StringName Action, Vector2 Axis)[] _moveActions =
    [
        ("move_right", Vector2.Right),
        ("move_left", Vector2.Left),
        ("move_down", Vector2.Down),
        ("move_up", Vector2.Up),
    ];

    /// <summary>Действие ускорения времени.</summary>
    private static readonly StringName _timeSpeedUp = "time_speed_up";

    /// <summary>Корень игры.</summary>
    private Main _main = null!;

    /// <summary>Счетчик кадров.</summary>
    private int _frame;

    /// <summary>Подъем главной сцены.</summary>
    public override void _Initialize()
    {
        _main = GD.Load<PackedScene>("res://core/main/main.tscn").Instantiate<Main>();
        Root.AddChild(_main);
    }

    /// <summary>Кадровый сценарий: разгон времени, затем вождение робота.</summary>
    /// <param name="delta">Время между кадрами.</param>
    /// <returns>false - продолжать цикл.</returns>
    public override bool _Process(double delta)
    {
        _frame++;
        if (_frame == SpeedUpFrame)
        {
            for (int idx = 0; idx < SpeedUpPresses; idx++)
            {
                Press(_timeSpeedUp);
            }
        }
        if (_frame >= DriveFromFrame)
        {
            Drive();
        }
        return false;
    }

    /// <summary>Нажатие действия через очередь ввода (доходит до _UnhandledInput).</summary>
    /// <param name="action">Имя действия.</param>
    private static void Press(StringName action) =>
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });

    /// <summary>Ведение робота к цели зажатием действий движения.</summary>
    private void Drive()
    {
        Robot? robot = _main.Robot;
        if (robot is null)
        {
            return;
        }

        Vector2 delta = TargetPosition() - robot.GlobalPosition;
        foreach ((StringName action, Vector2 axis) in _moveActions)
        {
            bool wants = delta.Length() > ArriveDistance && delta.Dot(axis) > ArriveDistance * 0.5f;
            if (wants)
            {
                Input.ActionPress(action);
            }
            else
            {
                Input.ActionRelease(action);
            }
        }
    }

    /// <summary>Цель: первый маркер вышки мира, иначе запасная клетка.</summary>
    /// <returns>Мировая позиция цели.</returns>
    private Vector2 TargetPosition()
    {
        List<TowerMarker>? markers = _main.World?.GetTowerMarkers();
        return markers is { Count: > 0 } ? markers[0].GlobalPosition : Iso.CellToWorld(_fallbackCell);
    }
}
