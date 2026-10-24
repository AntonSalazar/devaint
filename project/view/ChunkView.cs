using System.Collections.Generic;
using DevAInt.Sim.Grid;
using Godot;

/// <summary>Отображение одного чанка: меш полов и боксы стен; перестраивается целиком при любом изменении.</summary>
public partial class ChunkView : Node3D
{
    /// <summary>Общий материал с цветом из вершин или экземпляров.</summary>
    private static readonly StandardMaterial3D _material = new() { VertexColorUseAsAlbedo = true };

    /// <summary>Узел полов.</summary>
    private readonly MeshInstance3D _floors = new() { Name = "Floors", MaterialOverride = _material };

    /// <summary>Опущенные куски по номерам последнего ApplyCut.</summary>
    private bool[] _cut = [];

    /// <summary>Чанк по x.</summary>
    public int ChunkX { get; private set; }

    /// <summary>Чанк по y.</summary>
    public int ChunkY { get; private set; }

    /// <summary>Стены и двери последней постройки.</summary>
    public IReadOnlyList<WallInstance> Walls { get; private set; } = [];

    /// <summary>Есть ли полы в последней постройке.</summary>
    public bool HasFloors => _floors.Mesh is not null;

    /// <summary>Видимые стены, для тестов.</summary>
    public MultiMeshInstance3D VisibleWalls { get; } = new()
    {
        Name = "Walls",
        MaterialOverride = Cutaway.Material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>Стены для теней, для тестов.</summary>
    public MultiMeshInstance3D ShadowWalls { get; } = new()
    {
        Name = "WallShadows",
        MaterialOverride = _material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
    };

    /// <summary>Свои данные экземпляра для шейдера среза: красный канал 1 - кусок опущен, 0 - целый.</summary>
    public static Color CutData(bool cut) =>
        // Не Colors.Transparent: в Godot это белый с нулевой прозрачностью, и красный канал у него 1
        cut ? new Color(1, 0, 0, 0) : new Color(0, 0, 0, 0);

    /// <summary>Настройка на чанк; вызывается сразу после создания.</summary>
    public void Init(int chunkX, int chunkY)
    {
        (ChunkX, ChunkY) = (chunkX, chunkY);
        Name = $"Chunk_{chunkX}_{chunkY}";
        AddChild(_floors);
        AddChild(VisibleWalls);
        AddChild(ShadowWalls);
    }

    /// <summary>Перестройка полов и стен по текущему миру реплики.</summary>
    public void Rebuild(World world, WorldTable table)
    {
        _floors.Mesh = ChunkMesh.BuildFloors(world, table, ChunkX, ChunkY);
        Walls = ChunkMesh.BuildWalls(world, table, ChunkX, ChunkY);
        MultiMesh multi = new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,

            // Красный канал своих данных - срезан ли кусок; его читает шейдер среза
            UseCustomData = true,
            Mesh = new BoxMesh { Size = Vector3.One },
            InstanceCount = Walls.Count,
        };
        for (int i = 0; i < Walls.Count; i++)
        {
            multi.SetInstanceTransform(i, Walls[i].Transform);
            multi.SetInstanceColor(i, Walls[i].Color);
        }

        VisibleWalls.Multimesh = multi;
        ShadowWalls.Multimesh = multi;
    }

    /// <summary>Срез кусков стен на рёбрах из cut, остальные целые.</summary>
    public void ApplyCut(IReadOnlySet<WallKey> cut)
    {
        MultiMesh multi = VisibleWalls.Multimesh;
        _cut = new bool[Walls.Count];
        for (int i = 0; i < Walls.Count; i++)
        {
            _cut[i] = cut.Contains(Walls[i].Key);
            multi.SetInstanceCustomData(i, CutData(_cut[i]));
        }
    }

    /// <summary>Опущен ли кусок с номером index, для тестов; headless-отрисовка данные экземпляров не хранит.</summary>
    public bool IsCut(int index) => index < _cut.Length && _cut[index];
}
