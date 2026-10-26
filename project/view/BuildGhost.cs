using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using Godot;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Призрак постройки: полупрозрачный бокс цели под курсором, зелёный - можно, красный - нельзя.</summary>
public partial class BuildGhost : MeshInstance3D
{
    /// <summary>Цвет цели, которую можно построить.</summary>
    public static readonly Color Good = new(0.3f, 0.9f, 0.4f, 0.45f);

    /// <summary>Цвет цели, которую нельзя построить.</summary>
    public static readonly Color Bad = new(0.95f, 0.25f, 0.2f, 0.45f);

    /// <summary>Материал призрака, цвет меняется по проверке.</summary>
    private readonly StandardMaterial3D _material = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true,

        // Затемнение кадра тоже полупрозрачный проход и рисует поверх копию кадра: призрак идёт после него
        RenderPriority = VisibilityOverlay.MarksPriority,
    };

    /// <summary>Текущий цвет, для тестов.</summary>
    public Color Color => _material.AlbedoColor;

    /// <summary>Бокс цели: объект на своих клетках, стена на ребре; null - цели нет в мире, например разбирать нечего.</summary>
    public static Transform3D? Box(BuildTarget target, World world, WorldTable table)
    {
        switch (target.Kind)
        {
            case BuildKind.Object:
                ObjectDef def = table.Object(target.Def);
                return ObjectBox(WorldObject.Create(0, target.Def, def, target.Cell, target.Turn), (float)def.Height);
            case BuildKind.RemoveObject:
                return world.ObjectAt(target.Cell) is WorldObject obj ? ObjectBox(obj, (float)table.Object(obj.Def).Height) : null;
            default:
                return WallBox(target.Cell, target.Side);
        }
    }

    /// <summary>Настройка узла; вызывается сразу после создания, призрак скрыт.</summary>
    public void Init()
    {
        Name = "BuildGhost";
        Mesh = new BoxMesh { Size = Vector3.One };
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    /// <summary>Призрак на цели и цвет по проверке; без бокса цели призрак скрыт.</summary>
    public void Show(Transform3D? box, bool ok)
    {
        Visible = box is not null;
        if (box is Transform3D transform)
        {
            Transform = transform;
            _material.AlbedoColor = ok ? Good : Bad;
        }
    }

    /// <summary>Бокс объекта высотой height на всех его клетках.</summary>
    private static Transform3D ObjectBox(WorldObject obj, float height)
    {
        Vector3 size = new(obj.Width - ChunkMesh.ObjectGap, height, obj.Depth - ChunkMesh.ObjectGap);
        Vector3 center = ViewMath.ToGodot(obj.Cell.X + (obj.Width / 2f), obj.Cell.Y + (obj.Depth / 2f), obj.Cell.Z) + (Vector3.Up * height / 2);
        return new Transform3D(Basis.Identity.Scaled(size), center);
    }

    /// <summary>Бокс стены на ребре side клетки.</summary>
    private static Transform3D WallBox(CellPos cell, Side side)
    {
        // Северное и южное ребро идут вдоль X, западное и восточное - вдоль Z
        bool alongX = side is Side.North or Side.South;
        float x = cell.X + (side == Side.East ? 1 : alongX ? 0.5f : 0);
        float z = cell.Y + (side == Side.South ? 1 : alongX ? 0 : 0.5f);
        Vector3 size = alongX ? new Vector3(1, ChunkMesh.WallHeight, ChunkMesh.WallThickness * 2) : new Vector3(ChunkMesh.WallThickness * 2, ChunkMesh.WallHeight, 1);
        Vector3 center = ViewMath.ToGodot(x, z, cell.Z) + (Vector3.Up * ChunkMesh.WallHeight / 2);
        return new Transform3D(Basis.Identity.Scaled(size), center);
    }
}
