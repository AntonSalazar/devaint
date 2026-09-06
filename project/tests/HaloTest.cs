using System.Collections.Generic;
using Godot;

/// <summary>
/// Тесты <see cref="Halo"/>: геометрия квада, параметры шейдера, монотонность по излучению,
/// плавное схождение радиуса к цели, продвижение фазы пульса.
/// Ореол инстанцируется из halo.tscn.
/// </summary>
public class HaloTest : CsTestCase
{
    /// <summary>Сцена ореола.</summary>
    private const string HaloScenePath = "res://core/halo/halo.tscn";

    /// <summary>Узлы текущего теста - освобождаются в AfterEach.</summary>
    private readonly List<Node> _nodes = [];

    /// <summary>Освобождение узлов после теста.</summary>
    public override void AfterEach()
    {
        foreach (Node node in _nodes)
        {
            node.Free();
        }
        _nodes.Clear();
    }

    /// <summary>Первый вызов применяется мгновенно: квад - эллипсоидный бокс 2:1.</summary>
    public void TestFirstEmissionSnapsAndBuildsQuad()
    {
        Halo halo = SpawnHalo();

        halo.SetEmission(1.0f);
        float radius = Halo.BaseRadius + Halo.RadiusPerEmission;
        CheckNear(halo.Radius, radius, "first call snaps the radius");
        Vector2[] polygon = halo.Polygon;
        CheckEq(polygon.Length, 4, "quad has four points");
        if (polygon.Length != 4)
        {
            return;
        }

        Rect2 bounds = new(polygon[0], Vector2.Zero);
        foreach (Vector2 point in polygon)
        {
            bounds = bounds.Expand(point);
        }
        CheckNear(bounds.Size.X, radius * 2.0, "quad width is the diameter");
        CheckNear(bounds.Size.Y, radius * 2.0 * Iso.ScaleY, "quad height is squashed by ScaleY");
        CheckNear(bounds.GetCenter().X, 0.0, "quad is centered on x");
        CheckNear(bounds.GetCenter().Y, 0.0, "quad is centered on y");
    }

    /// <summary>Параметры шейдера следуют из текущего состояния и Iso.</summary>
    public void TestShaderParameters()
    {
        Halo halo = SpawnHalo();
        ShaderMaterial shader = (ShaderMaterial)halo.Material;

        halo.SetEmission(1.0f);
        CheckNear(
            shader.GetShaderParameter("radius").AsSingle(),
            Halo.BaseRadius + Halo.RadiusPerEmission, "radius for emission 1.0");
        CheckNear(halo.PulseSpeed, Halo.BasePulse + Halo.PulsePerEmission, "pulse 1.0");
        CheckNear(shader.GetShaderParameter("iso_scale_y").AsSingle(), Iso.ScaleY, "iso scale from Iso");
    }

    /// <summary>Больше излучения - больше целевой радиус и быстрее пульс.</summary>
    public void TestMonotonicInEmission()
    {
        Halo halo = SpawnHalo();

        halo.SetEmission(1.0f);
        float radiusIdle = halo.TargetRadius;
        float pulseIdle = halo.PulseSpeed;
        halo.SetEmission(3.0f);
        halo._Process(1.0);
        CheckTrue(halo.TargetRadius > radiusIdle, "target radius grows");
        CheckTrue(halo.PulseSpeed > pulseIdle, "pulse speeds up");
    }

    /// <summary>Смена излучения сходится к цели плавно, а не скачком.</summary>
    public void TestRadiusConvergesSmoothly()
    {
        Halo halo = SpawnHalo();
        ShaderMaterial shader = (ShaderMaterial)halo.Material;

        halo.SetEmission(1.0f);
        float start = halo.Radius;
        halo.SetEmission(3.0f);
        CheckNear(halo.Radius, start, "no jump right after the change");
        halo._Process(0.1);
        CheckNear(halo.Radius, start + (Halo.RadiusSpeed * 0.1f), "moves at RadiusSpeed", 0.001);
        CheckTrue(halo.Radius < halo.TargetRadius, "still short of the target");
        halo._Process(1.0);
        CheckNear(halo.Radius, halo.TargetRadius, "reaches the target");
        CheckNear(
            shader.GetShaderParameter("radius").AsSingle(), halo.TargetRadius,
            "shader follows the current radius");
    }

    /// <summary>Фаза пульса копится в скрипте и заворачивается в [0, 1).</summary>
    public void TestPhaseAdvancesAndWraps()
    {
        Halo halo = SpawnHalo();
        ShaderMaterial shader = (ShaderMaterial)halo.Material;

        halo.SetEmission(1.0f);
        halo._Process(0.25);
        CheckNear(shader.GetShaderParameter("phase").AsSingle(), 0.25, "phase = pulse_speed * dt");
        halo._Process(1.0);
        float phase = shader.GetShaderParameter("phase").AsSingle();
        CheckTrue(phase is >= 0.0f and < 1.0f, "phase wraps into [0, 1)");
    }

    /// <summary>Создание ореола из сцены (без добавления в дерево).</summary>
    /// <returns>Ореол, освобождаемый в AfterEach.</returns>
    private Halo SpawnHalo()
    {
        Halo halo = GD.Load<PackedScene>(HaloScenePath).Instantiate<Halo>();
        _nodes.Add(halo);
        return halo;
    }
}
