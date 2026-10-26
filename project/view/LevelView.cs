using System.Collections.Generic;
using DevAInt.Sim.Grid;
using Godot;

/// <summary>Один этаж чанка: меш полов, боксы стен и объектов, отдельно видимые со срезом и целые для теней.</summary>
public partial class LevelView : Node3D
{
    /// <summary>Общий материал с цветом из вершин или экземпляров.</summary>
    private static readonly StandardMaterial3D _material = new() { VertexColorUseAsAlbedo = true };

    /// <summary>Материал полов: виден с обеих сторон, снизу пол этажа выше - потолок.</summary>
    private static readonly StandardMaterial3D _floorMaterial = new() { VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

    /// <summary>Материал стекла окон: полупрозрачный и после затемнения кадра, иначе затемнение затирает его копией кадра.</summary>
    private static readonly StandardMaterial3D _glassMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(1, 1, 1, 0.35f),
        RenderPriority = VisibilityOverlay.MarksPriority,
    };

    /// <summary>Узел полов.</summary>
    private readonly MeshInstance3D _floors = new() { Name = "Floors", MaterialOverride = _floorMaterial };

    /// <summary>Опущенные куски по номерам последнего ApplyCut.</summary>
    private bool[] _cut = [];

    /// <summary>Опущенные объекты по номерам последнего ApplyCut.</summary>
    private bool[] _cutObjects = [];

    /// <summary>Спрятанные стёкла по номерам последнего ApplyCut.</summary>
    private bool[] _cutGlass = [];

    /// <summary>Этаж.</summary>
    public int Z { get; private set; }

    /// <summary>Куски стен и двери последней постройки, без стёкол.</summary>
    public IReadOnlyList<WallInstance> Walls { get; private set; } = [];

    /// <summary>Стёкла окон последней постройки.</summary>
    public IReadOnlyList<WallInstance> Glass { get; private set; } = [];

    /// <summary>Объекты последней постройки.</summary>
    public IReadOnlyList<ObjectInstance> Objects { get; private set; } = [];

    /// <summary>Есть ли полы в последней постройке.</summary>
    public bool HasFloors => _floors.Mesh is not null;

    /// <summary>Есть ли на этаже хоть что-то: пол, стена или объект.</summary>
    public bool IsEmpty => !HasFloors && Walls.Count == 0 && Glass.Count == 0 && Objects.Count == 0;

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

    /// <summary>Стёкла окон, для тестов.</summary>
    public MultiMeshInstance3D GlassPanes { get; } = new()
    {
        Name = "Glass",
        MaterialOverride = _glassMaterial,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
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

    /// <summary>Настройка на этаж z; вызывается сразу после создания.</summary>
    public void Init(int z)
    {
        Z = z;
        Name = $"Level_{z}";
        AddChild(_floors);
        AddChild(VisibleWalls);
        AddChild(ShadowWalls);
        AddChild(GlassPanes);
        AddChild(VisibleObjects);
        AddChild(ShadowObjects);
    }

    /// <summary>Перестройка полов, стен и объектов этажа чанка по текущему миру реплики.</summary>
    public void Rebuild(World world, WorldTable table, int chunkX, int chunkY)
    {
        _floors.Mesh = ChunkMesh.BuildFloors(world, table, chunkX, chunkY, Z);
        List<WallInstance> pieces = ChunkMesh.BuildWalls(world, table, chunkX, chunkY, Z);
        Walls = pieces.FindAll(piece => !piece.IsGlass);
        Glass = pieces.FindAll(piece => piece.IsGlass);
        Objects = ChunkMesh.BuildObjects(world, table, chunkX, chunkY, Z);
        MultiMesh walls = Boxes(Walls.Count);
        for (int i = 0; i < Walls.Count; i++)
        {
            walls.SetInstanceTransform(i, Walls[i].Transform);
            walls.SetInstanceColor(i, Walls[i].Color);
        }

        MultiMesh glass = Boxes(Glass.Count);
        for (int i = 0; i < Glass.Count; i++)
        {
            glass.SetInstanceTransform(i, Glass[i].Transform);
            glass.SetInstanceColor(i, Glass[i].Color);
        }

        GlassPanes.Multimesh = glass;
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
            VisibleWalls.Multimesh.SetInstanceCustomData(i, ChunkView.CutData(_cut[i]));
        }

        // Стекло без шейдера среза: у опущенной рамы оно прячется сжатием в точку, иначе висело бы в воздухе
        _cutGlass = new bool[Glass.Count];
        for (int i = 0; i < Glass.Count; i++)
        {
            _cutGlass[i] = cut.Contains(Glass[i].Key);
            Transform3D shown = Glass[i].Transform;
            GlassPanes.Multimesh.SetInstanceTransform(i, _cutGlass[i] ? new Transform3D(Basis.Identity.Scaled(Vector3.Zero), shown.Origin) : shown);
        }

        _cutObjects = new bool[Objects.Count];
        for (int i = 0; i < Objects.Count; i++)
        {
            _cutObjects[i] = cutObjects.Contains(Objects[i].Id);
            VisibleObjects.Multimesh.SetInstanceCustomData(i, ChunkView.CutData(_cutObjects[i]));
        }
    }

    /// <summary>Опущен ли кусок с номером index, для тестов; headless-отрисовка данные экземпляров не хранит.</summary>
    public bool IsCut(int index) => index < _cut.Length && _cut[index];

    /// <summary>Спрятано ли стекло с номером index, для тестов.</summary>
    public bool IsGlassCut(int index) => index < _cutGlass.Length && _cutGlass[index];

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
