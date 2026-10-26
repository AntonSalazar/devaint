using System.Linq;
using DevAInt.Sim.Controls;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Net;
using Godot;

/// <summary>Действия управления из данных модов: регистрация в InputMap и отправка нажатий серверу.</summary>
public sealed class GameControls(ControlTable table, ISimLog log)
{
    /// <summary>Висит ли действие с этим номером на кнопке мыши; считается один раз, а не каждый кадр.</summary>
    private readonly bool[] _onMouse = [.. Enumerable.Range(0, table.Count).Select(i => table.Control(i).Keys.Any(key => key.StartsWith("mouse_", System.StringComparison.Ordinal)))];

    /// <summary>Событие ввода для клавиши из данных: mouse_left, mouse_right, mouse_middle или key_ и имя клавиши Godot; null - имя неизвестно.</summary>
    public static InputEvent? ParseKey(string key)
    {
        MouseButton? button = key switch
        {
            "mouse_left" => MouseButton.Left,
            "mouse_right" => MouseButton.Right,
            "mouse_middle" => MouseButton.Middle,
            _ => null,
        };
        if (button is MouseButton mouse)
        {
            return new InputEventMouseButton { ButtonIndex = mouse };
        }

        Key code = key.StartsWith("key_", System.StringComparison.Ordinal) ? OS.FindKeycodeFromString(key[4..]) : Key.None;
        return code == Key.None ? null : new InputEventKey { PhysicalKeycode = code };
    }

    /// <summary>Действия InputMap с id действий и их клавишами; неизвестная клавиша - ошибка в лог.</summary>
    public void Register()
    {
        for (int i = 0; i < table.Count; i++)
        {
            ControlDef control = table.Control(i);
            if (InputMap.HasAction(control.Id))
            {
                continue;
            }

            InputMap.AddAction(control.Id);
            foreach (string key in control.Keys)
            {
                if (ParseKey(key) is InputEvent input)
                {
                    InputMap.ActionAddEvent(control.Id, input);
                }
                else
                {
                    log.Error($"control {control.Id}: unknown key {key}");
                }
            }
        }
    }

    /// <summary>Нажатия и отпускания за кадр серверу; отпускание шлётся только у действий, которые держатся; mouseOverUi - нажатия действий на кнопках мыши не шлются.</summary>
    public void Poll(GameClient client, bool mouseOverUi = false)
    {
        for (int i = 0; i < table.Count; i++)
        {
            // Щелчок по окну тоже виден в Input, но это щелчок по окну, а не удар; отпускание шлётся всегда, иначе прицел залипнет
            ControlDef control = table.Control(i);
            bool blocked = mouseOverUi && _onMouse[i];
            if (!blocked && Input.IsActionJustPressed(control.Id))
            {
                client.SendControl(i, true);
            }
            else if (control.Hold && Input.IsActionJustReleased(control.Id))
            {
                client.SendControl(i, false);
            }
        }
    }

    /// <summary>Зажато ли какое-нибудь действие прицеливания: тогда своя капсула смотрит на курсор.</summary>
    public bool IsAiming() => Enumerable.Range(0, table.Count).Select(table.Control).Any(control => control.Does == ControlAction.Aim && Input.IsActionPressed(control.Id));
}
