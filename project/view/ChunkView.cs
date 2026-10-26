using System.Collections.Generic;
using DevAInt.Sim.Grid;
using Godot;

/// <summary>Отображение одного чанка: узел на каждый непустой этаж; перестраивается целиком при любом изменении.</summary>
public partial class ChunkView : Node3D
{
    /// <summary>Пустой срез стен.</summary>
    private static readonly HashSet<WallKey> _noWalls = [];

    /// <summary>Пустой срез объектов.</summary>
    private static readonly HashSet<long> _noObjects = [];

    /// <summary>Этажи чанка, в которых что-то есть.</summary>
    private readonly SortedDictionary<int, LevelView> _levels = [];

    /// <summary>Самый верхний видимый этаж, null - видны все.</summary>
    private int? _top;

    /// <summary>Чанк по x.</summary>
    public int ChunkX { get; private set; }

    /// <summary>Чанк по y.</summary>
    public int ChunkY { get; private set; }

    /// <summary>Непустые этажи чанка снизу вверх.</summary>
    public IEnumerable<LevelView> Levels => _levels.Values;

    /// <summary>Стены всех этажей, для тестов.</summary>
    public IReadOnlyList<WallInstance> Walls
    {
        get
        {
            List<WallInstance> walls = [];
            foreach (LevelView level in _levels.Values)
            {
                walls.AddRange(level.Walls);
            }

            return walls;
        }
    }

    /// <summary>Есть ли полы хоть на одном этаже.</summary>
    public bool HasFloors
    {
        get
        {
            foreach (LevelView level in _levels.Values)
            {
                if (level.HasFloors)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Свои данные экземпляра для шейдера среза: красный канал 1 - кусок опущен, 0 - целый.</summary>
    public static Color CutData(bool cut) =>
        // Не Colors.Transparent: в Godot это белый с нулевой прозрачностью, и красный канал у него 1
        cut ? new Color(1, 0, 0, 0) : new Color(0, 0, 0, 0);

    /// <summary>Настройка на чанк; вызывается сразу после создания.</summary>
    public void Init(int chunkX, int chunkY)
    {
        (ChunkX, ChunkY) = (chunkX, chunkY);
        Name = $"Chunk_{chunkX}_{chunkY}";
    }

    /// <summary>Этаж z или null, если на нём пусто.</summary>
    public LevelView? Level(int z) => _levels.GetValueOrDefault(z);

    /// <summary>Перестройка всех этажей по текущему миру реплики: пустые этажи узлов не держат.</summary>
    public void Rebuild(World world, WorldTable table)
    {
        for (int z = World.MinLevel; z <= World.MaxLevel; z++)
        {
            if (!world.HasLevel(ChunkX, ChunkY, z) && !_levels.ContainsKey(z))
            {
                continue;
            }

            LevelView level = _levels.GetValueOrDefault(z) ?? NewLevel(z);
            level.Rebuild(world, table, ChunkX, ChunkY);
            if (level.IsEmpty)
            {
                _levels.Remove(z);
                level.QueueFree();
            }
            else
            {
                _levels[z] = level;
                level.Visible = _top is not int top || z <= top;
            }
        }
    }

    /// <summary>Видны только этажи не выше top, как в изометрии Project Zomboid; null - видны все.</summary>
    public void ShowUpTo(int? top)
    {
        _top = top;
        foreach (LevelView level in _levels.Values)
        {
            level.Visible = top is not int limit || level.Z <= limit;
        }
    }

    /// <summary>Срез стен и объектов на этаже z, остальные этажи целые.</summary>
    public void ApplyCut(int z, IReadOnlySet<WallKey> cut, IReadOnlySet<long> cutObjects)
    {
        foreach (LevelView level in _levels.Values)
        {
            level.ApplyCut(level.Z == z ? cut : _noWalls, level.Z == z ? cutObjects : _noObjects);
        }
    }

    /// <summary>Новый узел этажа в дереве чанка.</summary>
    private LevelView NewLevel(int z)
    {
        LevelView level = new();
        level.Init(z);
        AddChild(level);
        return level;
    }
}
