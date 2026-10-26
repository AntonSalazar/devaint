using System;
using Godot;

/// <summary>Три режима камеры и переключение между ними по кругу: изометрия, из-за плеча, от первого лица.</summary>
public partial class CameraRig : Node3D
{
    /// <summary>Имена режимов по порядку переключения, как в --camera.</summary>
    public static readonly string[] Modes = ["iso", "shoulder", "first"];

    /// <summary>Камеры в порядке Modes.</summary>
    private GameCamera[] _cameras = [];

    /// <summary>Номер текущего режима.</summary>
    private int _index;

    /// <summary>Текущая камера.</summary>
    public GameCamera Camera => _cameras[_index];

    /// <summary>Имя текущего режима.</summary>
    public string Mode => Modes[_index];

    /// <summary>Номер режима по имени или null, если такого нет.</summary>
    public static int? ModeIndex(string? mode) => Array.IndexOf(Modes, mode) is int index and >= 0 ? index : null;

    /// <summary>Создание камер; текущей становится режим mode, изометрия по умолчанию.</summary>
    public void Init(string? mode = null)
    {
        Name = "CameraRig";
        IsoCamera iso = new();
        iso.Init(Vector3.Zero);
        ShoulderCamera shoulder = new();
        shoulder.Init();
        FirstPersonCamera first = new();
        first.Init();
        _cameras = [iso, shoulder, first];
        foreach (GameCamera camera in _cameras)
        {
            AddChild(camera);
        }

        Select(ModeIndex(mode) ?? 0);
    }

    /// <summary>Следующий режим по кругу; новая камера сразу встаёт на ноги feet со взглядом facing.</summary>
    public void Next(Vector3 feet, Vector3 facing)
    {
        Select((_index + 1) % _cameras.Length);
        Camera.Place(feet, facing);
    }

    /// <summary>Режим с номером index становится текущим, остальные камеры выключаются.</summary>
    private void Select(int index)
    {
        _index = index;
        for (int i = 0; i < _cameras.Length; i++)
        {
            _cameras[i].Current = i == index;
        }
    }
}
