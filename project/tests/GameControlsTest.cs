using DevAInt.Sim.Controls;
using DevAInt.Sim.Mods;
using Godot;

/// <summary>Тесты <see cref="GameControls"/>: клавиши из данных и действия InputMap.</summary>
public class GameControlsTest : TestCase
{
    /// <summary>Кнопки мыши и клавиши по именам Godot; неизвестное имя - null.</summary>
    public void TestParseKey()
    {
        CheckEqual(MouseButton.Right, (GameControls.ParseKey("mouse_right") as InputEventMouseButton)?.ButtonIndex, "right mouse");
        CheckEqual(Key.E, (GameControls.ParseKey("key_e") as InputEventKey)?.PhysicalKeycode, "e");
        CheckEqual(Key.Ctrl, (GameControls.ParseKey("key_ctrl") as InputEventKey)?.PhysicalKeycode, "ctrl");
        CheckEqual(Key.Space, (GameControls.ParseKey("key_space") as InputEventKey)?.PhysicalKeycode, "space");
        CheckEqual(null, GameControls.ParseKey("key_nosuchkey"), "unknown key");
        CheckEqual(null, GameControls.ParseKey("q"), "no prefix");
    }

    /// <summary>Действия core регистрируются с клавишами из данных: прицел на ПКМ и Ctrl, удар на ЛКМ, предмет на E.</summary>
    public void TestCoreControlsAreRegistered()
    {
        ModLoadResult loaded = ModLoader.Load(ModsPath.Find(), ["core"]);
        ListLog log = new();
        GameControls controls = new(new ControlTable(loaded.Defs), log);

        controls.Register();
        controls.Register();

        CheckEqual(2, InputMap.ActionGetEvents("core:aim").Count, "aim keys");
        Check(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }.IsAction("core:attack"), "left mouse attacks");
        Check(new InputEventKey { PhysicalKeycode = Key.E, Pressed = true }.IsAction("core:use_item"), "e uses item");
        Check(!controls.IsAiming(), "not aiming without keys");
        CheckEqual(0, log.Errors.Count, "no errors");
    }
}
