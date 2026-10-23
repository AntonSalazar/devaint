using Godot;

/// <summary>Общий материал стен со срезом вокруг своего персонажа.</summary>
public static class Cutaway
{
    /// <summary>Путь шейдера среза.</summary>
    public const string ShaderPath = "res://view/wall_cutaway.gdshader";

    /// <summary>Высота центра тела над полом, туда смотрит срез.</summary>
    public const float BodyCenter = 0.85f;

    /// <summary>Материал стен, один на все чанки, поэтому фокус задаётся один раз за кадр.</summary>
    public static ShaderMaterial Material { get; } = new() { Shader = GD.Load<Shader>(ShaderPath) };

    /// <summary>Фокус среза: точка на полу под персонажем.</summary>
    public static void SetFocus(Vector3 feet)
    {
        Material.SetShaderParameter("focus", feet + (Vector3.Up * BodyCenter));
        Material.SetShaderParameter("floor_y", feet.Y);
        Material.SetShaderParameter("enabled", true);
    }

    /// <summary>Срез выключен: своего персонажа нет.</summary>
    public static void Disable() => Material.SetShaderParameter("enabled", false);
}
