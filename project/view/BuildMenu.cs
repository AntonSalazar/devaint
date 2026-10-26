using System;
using System.Collections.Generic;
using Godot;

/// <summary>Меню постройки на B: пункты из данных модов и разбор, неготовые выключены; выбор включает режим постановки.</summary>
public partial class BuildMenu : CanvasLayer
{
    /// <summary>Ключ заголовка меню.</summary>
    public const string Title = "ui.build";

    /// <summary>Ключ подсказки управления постановкой.</summary>
    public const string Hint = "ui.build_hint";

    /// <summary>Колонка пунктов.</summary>
    private readonly VBoxContainer _column = new() { Name = "Options", CustomMinimumSize = new Vector2(320, 0) };

    /// <summary>Пункты последнего показа.</summary>
    private IReadOnlyList<BuildOption> _options = [];

    /// <summary>Пункт выбран.</summary>
    public event Action<BuildOption>? Chosen;

    /// <summary>Строки меню, для тестов.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            List<string> lines = [];
            foreach (Node child in _column.GetChildren())
            {
                lines.Add(child is Button button ? button.Text : ((Label)child).Text);
            }

            return lines;
        }
    }

    /// <summary>Текст пункта: название и состав.</summary>
    public static string Row(BuildOption option) => option.Needs.Length == 0 ? option.Title : $"{option.Title}  ({option.Needs})";

    /// <summary>Постройка меню; вызывается сразу после создания, меню скрыто.</summary>
    public void Init()
    {
        Name = "BuildMenu";
        Layer = 2;
        Visible = false;
        // Внизу слева: сверху слева HUD, который растёт вместе со списком предметов, справа окно инвентаря
        PanelContainer panel = new()
        {
            Name = "Panel",
            AnchorTop = 1,
            AnchorBottom = 1,
            GrowVertical = Control.GrowDirection.Begin,
            OffsetLeft = 16,
            OffsetTop = -16,
            OffsetBottom = -16,
        };
        panel.AddChild(_column);
        AddChild(panel);
    }

    /// <summary>Новые пункты меню.</summary>
    public void Show(IReadOnlyList<BuildOption> options)
    {
        _options = options;
        foreach (Node child in _column.GetChildren())
        {
            _column.RemoveChild(child);
            child.QueueFree();
        }

        _column.AddChild(new Label { Text = GameLocale.Text(Title, "Build"), AutoTranslateMode = AutoTranslateModeEnum.Disabled });
        for (int i = 0; i < options.Count; i++)
        {
            Button button = new()
            {
                Text = Row(options[i]),
                Disabled = !options[i].Ready,
                Alignment = HorizontalAlignment.Left,
                Flat = true,
                AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            };
            int index = i;
            button.Pressed += () => Choose(index);
            _column.AddChild(button);
        }

        _column.AddChild(new Label { Text = GameLocale.Text(Hint, "R - rotate, left click - build, right click - cancel"), AutoTranslateMode = AutoTranslateModeEnum.Disabled });
    }

    /// <summary>Выбор пункта номер index.</summary>
    public void Choose(int index) => Chosen?.Invoke(_options[index]);
}
