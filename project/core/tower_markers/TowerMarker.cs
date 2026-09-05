using Godot;

/// <summary>
/// Класс маркера вышки сети роя.
/// </summary>
public partial class TowerMarker : Marker2D
{
    /// <summary>
    /// Радиус действия вышки.
    /// </summary>
    [Export]
    public float Radius { get; set; } = 600.0f;
}
