using Godot;

/// <summary>Тесты <see cref="Cutaway"/>: шейдер подключён к стенам и получает фокус.</summary>
public class CutawayTest : TestCase
{
    /// <summary>Материал стен использует шейдер среза.</summary>
    public void TestMaterialUsesShader()
    {
        Check(Cutaway.Material.Shader is not null, "shader is loaded");
        CheckEqual(Cutaway.ShaderPath, Cutaway.Material.Shader!.ResourcePath, "shader path");
    }

    /// <summary>Видимые стены со срезом не отбрасывают тени, тени дают те же стены целиком.</summary>
    public void TestShadowsComeFromWholeWalls()
    {
        GameFixture game = new();
        WorldView view = new();
        view.Init(game.Client.Replica, game.Sim.WorldTable);
        game.Tick();
        ChunkView chunk = view.Chunk(0, 0)!;

        CheckEqual(GeometryInstance3D.ShadowCastingSetting.Off, chunk.VisibleWalls.CastShadow, "visible walls cast no shadows");
        CheckEqual(GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, chunk.ShadowWalls.CastShadow, "shadow walls only cast shadows");
        Check(chunk.VisibleWalls.MaterialOverride == Cutaway.Material, "visible walls use the cutaway");
        Check(chunk.ShadowWalls.MaterialOverride != Cutaway.Material, "shadow walls do not use the cutaway");
        Check(chunk.VisibleWalls.Multimesh == chunk.ShadowWalls.Multimesh, "both share one multimesh");
        view.Free();
    }

    /// <summary>Фокус - центр тела над точкой на полу, срез включается и выключается.</summary>
    public void TestFocusAndToggle()
    {
        Cutaway.SetFocus(new Vector3(6.5f, 0, 6.5f));

        Vector3 focus = Cutaway.Material.GetShaderParameter("focus").AsVector3();
        CheckEqual(new Vector3(6.5f, Cutaway.BodyCenter, 6.5f), focus, "focus");
        CheckEqual(0f, Cutaway.Material.GetShaderParameter("floor_y").AsSingle(), "floor");
        Check(Cutaway.Material.GetShaderParameter("enabled").AsBool(), "enabled");

        Cutaway.Disable();
        Check(!Cutaway.Material.GetShaderParameter("enabled").AsBool(), "disabled");
    }
}
