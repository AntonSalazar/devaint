using Godot;

/// <summary>
/// Класс маркера вышки сети роя.
/// </summary>
public partial class TowerMarker : Marker2D
{
    /// <summary>
    /// Радиус действия вышки по умолчанию, мировых px по земле.
    /// </summary>
    public const float DefaultRadius = 600.0f;

    /// <summary>
    /// Радиус действия вышки.
    /// </summary>
    [Export]
    public float Radius { get; set; } = DefaultRadius;
}
