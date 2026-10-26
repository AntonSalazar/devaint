using DevAInt.Sim.Grid;
using Godot;

/// <summary>Режим камеры: как она следует за своей капсулой и что из этого следует для тумана, среза стен и прицела.</summary>
public abstract partial class GameCamera : Camera3D
{
    /// <summary>Затемнение по видимости и туман войны: только изометрия.</summary>
    public virtual bool UsesFog => false;

    /// <summary>Срез стен, закрывающих персонажа: только изометрия.</summary>
    public virtual bool CutsWalls => false;

    /// <summary>Этажи выше своего не рисуются, как в изометрии Project Zomboid: иначе потолок закрыл бы персонажа.</summary>
    public virtual bool HidesUpperFloors => false;

    /// <summary>Мышь захвачена и крутит камеру.</summary>
    public virtual bool CapturesMouse => true;

    /// <summary>Своя капсула видна: от первого лица камера внутри неё.</summary>
    public virtual bool ShowsOwnBody => true;

    /// <summary>Персонаж всегда смотрит туда же, куда камера, без зажатого прицела.</summary>
    public virtual bool FacesAim => false;

    /// <summary>Мгновенная постановка на ноги feet своей капсулы со взглядом facing по земле.</summary>
    public abstract void Place(Vector3 feet, Vector3 facing);

    /// <summary>Следование за ногами feet за delta секунд; world - для стен, aiming - зажат ли прицел.</summary>
    public abstract void Follow(Vector3 feet, World? world, bool aiming, double delta);

    /// <summary>Зум на steps щелчков колёсика: плюс - ближе.</summary>
    public abstract void Zoom(int steps);

    /// <summary>Поворот камеры мышью на сдвиг relative в пикселях.</summary>
    public virtual void Turn(Vector2 relative)
    {
    }

    /// <summary>Точка экрана, через которую идёт прицел: в центре кадра, если мышь захвачена.</summary>
    public virtual Vector2 AimScreenPoint(Vector2 mouse, Vector2 screen) => screen / 2;
}
