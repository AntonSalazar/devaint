using Godot;

/// <summary>Тесты главной сцены Main.tscn.</summary>
public class MainSceneTest : TestCase
{
    /// <summary>Путь главной сцены.</summary>
    private const string MainScenePath = "res://Main.tscn";

    /// <summary>Главная сцена загружается, её корень - Node3D с именем Main.</summary>
    public void TestMainSceneLoads()
    {
        PackedScene? scene = GD.Load<PackedScene>(MainScenePath);
        Check(scene != null, "main scene is loaded");
        if (scene == null)
        {
            return;
        }

        Node root = scene.Instantiate();
        Check(root is Node3D, "root is Node3D");
        CheckEqual("Main", root.Name.ToString(), "root name");
        root.Free();
    }

    /// <summary>Главная сцена указана в настройках проекта.</summary>
    public void TestMainSceneIsProjectMainScene()
    {
        string mainScene = ProjectSettings.GetSetting("application/run/main_scene").AsString();
        CheckEqual(MainScenePath, mainScene, "application/run/main_scene");
    }
}
