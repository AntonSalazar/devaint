using Godot;

/// <summary>Тесты сцены выделенного сервера Server.tscn.</summary>
public class ServerSceneTest : TestCase
{
    /// <summary>Путь сцены сервера, её запускает just server.</summary>
    private const string ServerScenePath = "res://server/Server.tscn";

    /// <summary>Сцена загружается, её корень - ServerRoot без игровых узлов.</summary>
    public void TestServerSceneLoads()
    {
        PackedScene? scene = GD.Load<PackedScene>(ServerScenePath);
        Check(scene != null, "server scene is loaded");
        if (scene == null)
        {
            return;
        }

        Node root = scene.Instantiate();
        Check(root is ServerRoot, "root is ServerRoot");
        CheckEqual(0, root.GetChildCount(), "no child nodes");
        root.Free();
    }
}
