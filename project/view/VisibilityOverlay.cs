using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using Godot;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Затемнение кадра по видимости своего персонажа и туману войны: полноэкранный проход, луч на каждый пиксель.</summary>
public partial class VisibilityOverlay : MeshInstance3D
{
    /// <summary>Сторона окна текстуры в клетках вокруг персонажа: с запасом больше обзора и видимого кадра.</summary>
    public const int WindowCells = 128;

    /// <summary>Путь шейдера затемнения.</summary>
    public const string ShaderPath = "res://view/visibility.gdshader";

    /// <summary>Сторона текстуры памяти в текселах: на клетку 4 на 4, как точки памяти.</summary>
    public const int MemoryTexels = WindowCells * SeenCells.PointsPerSide;

    /// <summary>Бит текстуры стен: северное ребро клетки загораживает взгляд.</summary>
    public const byte NorthWall = 1;

    /// <summary>Бит текстуры стен: западное ребро клетки загораживает взгляд.</summary>
    public const byte WestWall = 2;

    /// <summary>Бит текстуры стен: объект в клетке закрывает обзор, как шкаф.</summary>
    public const byte SightBlock = 4;

    /// <summary>Материал прохода.</summary>
    private readonly ShaderMaterial _material = new() { Shader = GD.Load<Shader>(ShaderPath) };

    /// <summary>Байты стен окна, переиспользуются между обновлениями.</summary>
    private readonly byte[] _wallTexels = new byte[WindowCells * WindowCells];

    /// <summary>Байты памяти окна, переиспользуются между обновлениями.</summary>
    private readonly byte[] _memoryTexels = new byte[MemoryTexels * MemoryTexels];

    /// <summary>Картинка стен: байт на клетку.</summary>
    private readonly Image _wallImage = Image.CreateEmpty(WindowCells, WindowCells, false, Image.Format.R8);

    /// <summary>Картинка памяти: байт на точку памяти.</summary>
    private readonly Image _memoryImage = Image.CreateEmpty(MemoryTexels, MemoryTexels, false, Image.Format.R8);

    /// <summary>Текстура стен.</summary>
    private ImageTexture? _walls;

    /// <summary>Текстура памяти.</summary>
    private ImageTexture? _memory;

    /// <summary>Угол окна текстуры в клетках.</summary>
    public CellPos Origin { get; private set; }

    /// <summary>Биты загораживающих рёбер и объектов клеток окна с углом origin в texels, байт на клетку.</summary>
    public static void FillWalls(byte[] texels, World world, CellPos origin)
    {
        for (int y = 0; y < WindowCells; y++)
        {
            for (int x = 0; x < WindowCells; x++)
            {
                CellPos cell = new(origin.X + x, origin.Y + y, origin.Z);

                // Каждая клетка хранит только северное и западное ребро: южное и восточное шейдер берёт у соседа
                int walls = 0;
                if (!world.GetEdge(cell, Side.North).IsPassable)
                {
                    walls |= NorthWall;
                }

                if (!world.GetEdge(cell, Side.West).IsPassable)
                {
                    walls |= WestWall;
                }

                if (world.BlocksSight(cell))
                {
                    walls |= SightBlock;
                }

                texels[(y * WindowCells) + x] = (byte)walls;
            }
        }
    }

    /// <summary>Память окна с углом origin в texels: 255 - точка увидена раньше, 0 - нет.</summary>
    public static void FillMemory(byte[] texels, SeenCells seen, CellPos origin)
    {
        const int side = SeenCells.PointsPerSide;
        for (int y = 0; y < WindowCells; y++)
        {
            for (int x = 0; x < WindowCells; x++)
            {
                // Биты клетки читаются один раз и раскладываются в её 16 текселов
                ushort points = seen.Points(new CellPos(origin.X + x, origin.Y + y, origin.Z));
                for (int py = 0; py < side; py++)
                {
                    int row = (((y * side) + py) * MemoryTexels) + (x * side);
                    for (int px = 0; px < side; px++)
                    {
                        texels[row + px] = (points & (1 << ((py * side) + px))) != 0 ? (byte)255 : (byte)0;
                    }
                }
            }
        }
    }

    /// <summary>Настройка узла: квад во весь экран, который никогда не отсекается.</summary>
    public void Init()
    {
        Name = "VisibilityOverlay";
        Mesh = new QuadMesh { Size = new Vector2(2, 2) };
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;
        _material.SetShaderParameter("unexplored", Lighting.Background);

        // Вершины ставит шейдер, поэтому границы узла не связаны с камерой: пусть он виден всегда
        CustomAabb = new Aabb(new Vector3(-1e5f, -1e5f, -1e5f), new Vector3(2e5f, 2e5f, 2e5f));
    }

    /// <summary>Новые текстуры стен мира world и памяти seen, окно вокруг клетки center.</summary>
    public void Refresh(World world, SeenCells seen, CellPos center)
    {
        Origin = new CellPos(center.X - (WindowCells / 2), center.Y - (WindowCells / 2), center.Z);
        FillWalls(_wallTexels, world, Origin);
        FillMemory(_memoryTexels, seen, Origin);
        _wallImage.SetData(WindowCells, WindowCells, false, Image.Format.R8, _wallTexels);
        _memoryImage.SetData(MemoryTexels, MemoryTexels, false, Image.Format.R8, _memoryTexels);
        _walls = Upload(_walls, _wallImage, "walls");
        _memory = Upload(_memory, _memoryImage, "memory");
        _material.SetShaderParameter("window_origin", new Vector2I(Origin.X, Origin.Y));
        _material.SetShaderParameter("window_cells", WindowCells);
    }

    /// <summary>Глаза и взгляд для шейдера каждый кадр: видимость следует за плавным движением, а не за тиками.</summary>
    public void Look(Vector3 eye, Vector3 facing, VisionDef vision)
    {
        // Мировые x и z - это клеточные x и y
        _material.SetShaderParameter("eye", new Vector2(eye.X, eye.Z));
        _material.SetShaderParameter("facing", new Vector2(facing.X, facing.Z).Normalized());
        _material.SetShaderParameter("half_angle", Mathf.DegToRad((float)vision.Angle / 2));
        _material.SetShaderParameter("range", (float)vision.Range);
        _material.SetShaderParameter("near", (float)vision.Near);
    }

    /// <summary>Значение параметра шейдера, для тестов.</summary>
    public Variant Parameter(string name) => _material.GetShaderParameter(name);

    /// <summary>Текстура из картинки image: первый раз создаётся и отдаётся шейдеру в parameter, потом обновляется.</summary>
    private ImageTexture Upload(ImageTexture? texture, Image image, string parameter)
    {
        if (texture is not null)
        {
            texture.Update(image);
            return texture;
        }

        texture = ImageTexture.CreateFromImage(image);
        _material.SetShaderParameter(parameter, texture);
        return texture;
    }
}
