using Godot;

/// <summary>Камера, которую мышь крутит по горизонтали и вертикали: из-за плеча и от первого лица.</summary>
public abstract partial class MouseLookCamera : GameCamera
{
    /// <summary>Поворот в радианах на пиксель сдвига мыши.</summary>
    public const float Sensitivity = 0.003f;

    /// <summary>Поворот вокруг вертикали в радианах: 0 - взгляд на -Z.</summary>
    public float Yaw { get; private set; }

    /// <summary>Наклон в радианах: минус - вниз.</summary>
    public float Pitch { get; private set; }

    /// <summary>Самый низкий наклон.</summary>
    protected abstract float MinPitch { get; }

    /// <summary>Самый высокий наклон.</summary>
    protected abstract float MaxPitch { get; }

    /// <summary>Поворот вокруг вертикали так, чтобы камера смотрела по взгляду facing, без наклона.</summary>
    public void LookAlong(Vector3 facing)
    {
        if (facing.LengthSquared() > 1e-6f)
        {
            Yaw = Mathf.Atan2(-facing.X, -facing.Z);
        }

        Pitch = 0;
        Rotation = new Vector3(Pitch, Yaw, 0);
    }

    /// <summary>Мышь вправо - поворот направо, вверх - взгляд вверх; наклон в пределах MinPitch и MaxPitch.</summary>
    public override void Turn(Vector2 relative)
    {
        Yaw -= relative.X * Sensitivity;
        Pitch = Mathf.Clamp(Pitch - (relative.Y * Sensitivity), MinPitch, MaxPitch);
        Rotation = new Vector3(Pitch, Yaw, 0);
    }
}
