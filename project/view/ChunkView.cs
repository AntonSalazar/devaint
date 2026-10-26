using System.Collections.Generic;
using DevAInt.Sim.Grid;
using Godot;

/// <summary>Отображение одного чанка: меш полов, боксы стен и объектов; перестраивается целиком при любом изменении.</summary>
public partial class ChunkView : Node3D
{
    /// <summary>Общий материал с цветом из вершин или экземпляров.</summary>
    private static readonly StandardMaterial3D _material = new() { VertexColorUseAsAlbedo = true };

    /// <summary>Узел полов.</summary>
    private readonly MeshInstance3D _floors = new() { Name = "Floors", MaterialOverride = _material };

    /// <summary>Опущенные куски по номерам последнего ApplyCut.</summary>
    private bool[] _cut = [];

    /// <summary>Опущенные объекты по номерам последнего ApplyCut.</summary>
    private bool[] _cutObjects = [];

    /// <summary>Чанк по x.</summary>
    public int ChunkX { get; private set; }

    /// <summary>Чанк по y.</summary>
    public int ChunkY { get; private set; }

    /// <summary>Стены и двери последней постройки.</summary>
    public IReadOnlyList<WallInstance> Walls { get; private set; } = [];

    /// <summary>Объекты последней постройки.</summary>
    public IReadOnlyList<ObjectInstance> Objects { get; private set; } = [];

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

    /// <summary>Видимые объекты со срезом, для тестов.</summary>
    public MultiMeshInstance3D VisibleObjects { get; } = new()
    {
        Name = "Objects",
        MaterialOverride = Cutaway.Material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>Объекты для теней целиком, для тестов.</summary>
    public MultiMeshInstance3D ShadowObjects { get; } = new()
    {
        Name = "ObjectShadows",
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
        AddChild(VisibleObjects);
        AddChild(ShadowObjects);
    }

    /// <summary>Перестройка полов, стен и объектов по текущему миру реплики.</summary>
    public void Rebuild(World world, WorldTable table)
    {
        _floors.Mesh = ChunkMesh.BuildFloors(world, table, ChunkX, ChunkY);
        Walls = ChunkMesh.BuildWalls(world, table, ChunkX, ChunkY);
        Objects = ChunkMesh.BuildObjects(world, table, ChunkX, ChunkY);
        MultiMesh walls = Boxes(Walls.Count);
        for (int i = 0; i < Walls.Count; i++)
        {
            walls.SetInstanceTransform(i, Walls[i].Transform);
            walls.SetInstanceColor(i, Walls[i].Color);
        }

        MultiMesh objects = Boxes(Objects.Count);
        for (int i = 0; i < Objects.Count; i++)
        {
            objects.SetInstanceTransform(i, Objects[i].Transform);
            objects.SetInstanceColor(i, Objects[i].Color);
        }

        VisibleWalls.Multimesh = walls;
        ShadowWalls.Multimesh = walls;
        VisibleObjects.Multimesh = objects;
        ShadowObjects.Multimesh = objects;
    }

    /// <summary>Срез кусков стен на рёбрах из cut и объектов с id из cutObjects, остальные целые.</summary>
    public void ApplyCut(IReadOnlySet<WallKey> cut, IReadOnlySet<long> cutObjects)
    {
        _cut = new bool[Walls.Count];
        for (int i = 0; i < Walls.Count; i++)
        {
            _cut[i] = cut.Contains(Walls[i].Key);
            VisibleWalls.Multimesh.SetInstanceCustomData(i, CutData(_cut[i]));
        }

        _cutObjects = new bool[Objects.Count];
        for (int i = 0; i < Objects.Count; i++)
        {
            _cutObjects[i] = cutObjects.Contains(Objects[i].Id);
            VisibleObjects.Multimesh.SetInstanceCustomData(i, CutData(_cutObjects[i]));
        }
    }

    /// <summary>Опущен ли кусок с номером index, для тестов; headless-отрисовка данные экземпляров не хранит.</summary>
    public bool IsCut(int index) => index < _cut.Length && _cut[index];

    /// <summary>Опущен ли объект с номером index, для тестов.</summary>
    public bool IsObjectCut(int index) => index < _cutObjects.Length && _cutObjects[index];

    /// <summary>Пустой MultiMesh единичных боксов на count экземпляров с цветом и своими данными для среза.</summary>
    private static MultiMesh Boxes(int count)
    {
        return new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,

            // Красный канал своих данных - срезан ли бокс; его читает шейдер среза
            UseCustomData = true,
            Mesh = new BoxMesh { Size = Vector3.One },
            InstanceCount = count,
        };
    }
}
