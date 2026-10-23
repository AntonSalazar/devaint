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

    /// <summary>Узел стен и дверей.</summary>
    private readonly MultiMeshInstance3D _walls = new() { Name = "Walls", MaterialOverride = _material };

    /// <summary>Чанк по x.</summary>
    public int ChunkX { get; private set; }

    /// <summary>Чанк по y.</summary>
    public int ChunkY { get; private set; }

    /// <summary>Стены и двери последней постройки.</summary>
    public IReadOnlyList<WallInstance> Walls { get; private set; } = [];

    /// <summary>Есть ли полы в последней постройке.</summary>
    public bool HasFloors => _floors.Mesh is not null;

    /// <summary>Настройка на чанк; вызывается сразу после создания.</summary>
    public void Init(int chunkX, int chunkY)
    {
        (ChunkX, ChunkY) = (chunkX, chunkY);
        Name = $"Chunk_{chunkX}_{chunkY}";
        AddChild(_floors);
        AddChild(_walls);
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
            Mesh = new BoxMesh { Size = Vector3.One },
            InstanceCount = Walls.Count,
        };
        for (int i = 0; i < Walls.Count; i++)
        {
            multi.SetInstanceTransform(i, Walls[i].Transform);
            multi.SetInstanceColor(i, Walls[i].Color);
        }

        _walls.Multimesh = multi;
    }
}
