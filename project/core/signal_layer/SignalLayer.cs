using Godot;


/// <summary>
/// Класс визуализации сетки сигналов (поля покрытия роя).
/// Читает картинку статики из <see cref="SignalGrid"/> в текстуру шейдера.
/// Картинка обновляется по <see cref="SignalGrid.OnStaticChanged"/> сообщению.
/// </summary>
public partial class SignalLayer : Polygon2D
{
    /// <summary>
    /// Ссылка на экземпляр сетки сигнала роя.
    /// </summary>
    private SignalGrid? _grid = null;


    /// <summary>
    /// Метод инициализации.
    /// </summary>
    /// <param name="grid">Ссылка на экземпляр сетки роя.</param>
    public void Init(SignalGrid grid)
    {
        _grid = grid;
        FitToGrid(grid);
        Upload(grid);
        EventBus.Subscribe<SignalGrid.OnStaticChanged>(OnStaticChanged);
    }


    /// <summary>
    /// Метод деинициализации.
    /// </summary>
    public void Deinit()
    {
        EventBus.Unsubscribe<SignalGrid.OnStaticChanged>(OnStaticChanged);
        _grid = null;
    }


    /// <summary>
    /// Метод подгонки полигона под мировой прямоугольник грида.
    /// </summary>
    /// <param name="grid">Ссылка на экземпляр сетки роя.</param>
    private void FitToGrid(SignalGrid grid)
    {
        Vector2I origin = grid.Origin;
        Vector2I size = grid.Size;
        Vector2I[] corners = [
            origin, origin + new Vector2I(size.X, 0),
            origin + size, origin + new Vector2I(0, size.Y)
        ];
        Rect2 rect = new(Iso.CellToWorld(origin), Vector2.Zero);
        foreach (Vector2I corner in corners)
        {
            rect = rect.Expand(Iso.CellToWorld(corner));
        }
        rect = rect.Grow(Iso.TileWidth);
        Polygon = [
            rect.Position, new Vector2(rect.End.X, rect.Position.Y),
            rect.End, new Vector2(rect.Position.X, rect.End.Y)
        ];

        if (Material is ShaderMaterial shader)
        {
            shader.SetShaderParameter(ShaderParam.Origin, (Vector2)origin);
            shader.SetShaderParameter(ShaderParam.Size, (Vector2)size);
            shader.SetShaderParameter(ShaderParam.TileSize, new Vector2(Iso.TileWidth, Iso.TileHeight));
        }
    }


    /// <summary>
    /// Метод заливки картинки сетки роя в текстуру шейдера.
    /// </summary>
    /// <param name="grid">Ссылка на экземпляр сетки роя.</param>
    private void Upload(SignalGrid grid)
    {
        if (Material is ShaderMaterial shader)
        {
            shader.SetShaderParameter(
                ShaderParam.CoverageTex,
                ImageTexture.CreateFromImage(grid.GetStaticImage())
            );
        }
    }


    /// <summary>
    /// Метод, вызываемый при получении сообщения о перестройки сети роя.
    /// </summary>
    /// <param name="message">Сообщение об изменении сетки роя.</param>
    private void OnStaticChanged(SignalGrid.OnStaticChanged message)
    {
        if (_grid is null)
        {
            return;
        }
        Upload(_grid);
    }


    /// <summary>
    /// Набор параметров шейдера.
    /// </summary>
    private static class ShaderParam
    {
        /// <summary>
        /// Координата начала сетки.
        /// </summary>
        public static readonly StringName Origin = "grid_origin";

        /// <summary>
        /// Размер сетки.
        /// </summary>
        public static readonly StringName Size = "grid_size";

        /// <summary>
        /// Размер тайла.
        /// </summary>
        public static readonly StringName TileSize = "tile_size";

        /// <summary>
        /// Ссылка на текстуру карты покрытия.
        /// </summary>
        public static readonly StringName CoverageTex = "coverage_tex";
    }
}
