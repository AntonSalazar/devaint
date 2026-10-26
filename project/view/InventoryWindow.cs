using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Items;
using Godot;

/// <summary>Окно инвентаря, как в Project Zomboid: слева свой инвентарь, справа ближние контейнеры; щелчок перекладывает, правая кнопка - применить.</summary>
public partial class InventoryWindow : CanvasLayer
{
    /// <summary>Ключ заголовка колонки ближних контейнеров.</summary>
    public const string NearbyTitle = "ui.nearby";

    /// <summary>Ключ надписи, когда рядом нет контейнеров.</summary>
    public const string NothingNearby = "ui.nothing_nearby";

    /// <summary>Ключ заголовка колонки рецептов.</summary>
    public const string CraftTitle = "ui.crafting";

    /// <summary>Колонка своего инвентаря.</summary>
    private readonly VBoxContainer _own = new() { Name = "Own", CustomMinimumSize = new Vector2(260, 0) };

    /// <summary>Колонка ближних контейнеров.</summary>
    private readonly VBoxContainer _nearby = new() { Name = "Nearby", CustomMinimumSize = new Vector2(260, 0) };

    /// <summary>Колонка рецептов.</summary>
    private readonly VBoxContainer _craft = new() { Name = "Craft", CustomMinimumSize = new Vector2(260, 0) };

    /// <summary>Меню применений предмета по правой кнопке.</summary>
    private readonly PopupMenu _menu = new() { Name = "UseMenu" };

    /// <summary>Свой инвентарь последнего показа.</summary>
    private InventoryPanel? _ownPanel;

    /// <summary>Ближние контейнеры последнего показа.</summary>
    private IReadOnlyList<InventoryPanel> _nearbyPanels = [];

    /// <summary>Предметы по номерам, для применений.</summary>
    private ItemTable? _items;

    /// <summary>Стопка, для которой открыто меню, и номера действий пунктов.</summary>
    private (long Place, int Stack, List<int> Actions)? _menuFor;

    /// <summary>Предмет надо переложить: id, откуда и куда, 0 - свой инвентарь.</summary>
    public event Action<long, long, long>? MoveRequested;

    /// <summary>Предмет надо применить: id и номер действия.</summary>
    public event Action<long, int>? UseRequested;

    /// <summary>Надо начать крафт: номер рецепта.</summary>
    public event Action<int>? CraftRequested;

    /// <summary>Контейнер, куда кладутся предметы из своего инвентаря, 0 - рядом ничего нет.</summary>
    public long Selected { get; private set; }

    /// <summary>Строки своей колонки, для тестов.</summary>
    public IReadOnlyList<string> OwnLines => Lines(_own);

    /// <summary>Строки колонки ближних контейнеров, для тестов.</summary>
    public IReadOnlyList<string> NearbyLines => Lines(_nearby);

    /// <summary>Строки колонки рецептов, для тестов.</summary>
    public IReadOnlyList<string> CraftLines => Lines(_craft);

    /// <summary>Текст заголовка панели: название и вес, с вместимостью, если она есть.</summary>
    public static string Header(InventoryPanel panel)
    {
        string weight = panel.Weight.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        if (double.IsPositiveInfinity(panel.Capacity))
        {
            return $"{panel.Title}  {weight}";
        }

        return $"{panel.Title}  {weight} / {panel.Capacity.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>Текст строки стопки: название, число и вес.</summary>
    public static string Row(ItemStack stack) => $"{stack.Name} x{stack.Count}  {stack.Weight.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>Постройка окна; вызывается сразу после создания, окно скрыто.</summary>
    public void Init(ItemTable items)
    {
        Name = "InventoryWindow";
        Layer = 2;
        Visible = false;
        _items = items;
        PanelContainer panel = new()
        {
            Name = "Panel",
            AnchorLeft = 1,
            AnchorRight = 1,
            OffsetLeft = -840,
            OffsetRight = -16,
            OffsetTop = 16,
        };
        HBoxContainer columns = new() { Name = "Columns" };
        columns.AddChild(_own);
        columns.AddChild(new VSeparator());
        columns.AddChild(_nearby);
        columns.AddChild(new VSeparator());
        columns.AddChild(_craft);
        panel.AddChild(columns);
        AddChild(panel);
        AddChild(_menu);
        _menu.IdPressed += OnMenuPressed;
    }

    /// <summary>Новое содержимое окна; выбранный контейнер сохраняется, если он ещё рядом, иначе берётся ближний.</summary>
    public void Show(InventoryPanel own, IReadOnlyList<InventoryPanel> nearby)
    {
        _ownPanel = own;
        _nearbyPanels = nearby;
        bool stillNear = false;
        foreach (InventoryPanel panel in nearby)
        {
            stillNear |= panel.Place == Selected;
        }

        Selected = stillNear ? Selected : nearby.Count > 0 ? nearby[0].Place : 0;
        Clear(_own);
        Clear(_nearby);
        _own.AddChild(Label(Header(own)));
        AddRows(_own, own);
        _nearby.AddChild(Label(GameLocale.Text(NearbyTitle, "Nearby")));
        if (nearby.Count == 0)
        {
            _nearby.AddChild(Label(GameLocale.Text(NothingNearby, "Nothing nearby")));
        }

        foreach (InventoryPanel panel in nearby)
        {
            // Заголовок контейнера - кнопка выбора: туда кладутся предметы из своего инвентаря
            Button header = Button((panel.Place == Selected ? "> " : string.Empty) + Header(panel));
            long place = panel.Place;
            header.Pressed += () => Select(place);
            _nearby.AddChild(header);
            AddRows(_nearby, panel);
        }
    }

    /// <summary>Рецепты в колонке крафта: кнопка на рецепт, неготовые выключены, состав под названием.</summary>
    public void ShowRecipes(IReadOnlyList<RecipeRow> recipes)
    {
        Clear(_craft);
        _craft.AddChild(Label(GameLocale.Text(CraftTitle, "Crafting")));
        foreach (RecipeRow recipe in recipes)
        {
            Button button = Button(recipe.Title);
            button.Disabled = !recipe.Ready;
            button.TooltipText = recipe.Needs;
            int index = recipe.Index;
            button.Pressed += () => Craft(index);
            _craft.AddChild(button);
            _craft.AddChild(Label("  " + recipe.Needs));
        }
    }

    /// <summary>Просьба начать крафт по рецепту index.</summary>
    public void Craft(int index) => CraftRequested?.Invoke(index);

    /// <summary>Щелчок по стопке номер index места place: один предмет или вся стопка в другую колонку.</summary>
    public void PressStack(long place, int index, bool all)
    {
        if (Stack(place, index) is not ItemStack stack)
        {
            return;
        }

        // Из своего инвентаря - в выбранный контейнер, из контейнера - к себе
        long to = place == 0 ? Selected : 0;
        if (place == to)
        {
            return;
        }

        int count = all ? stack.Ids.Count : 1;
        for (int i = 0; i < count; i++)
        {
            MoveRequested?.Invoke(stack.Ids[i], place, to);
        }
    }

    /// <summary>Применения стопки своего инвентаря номер index: подпись и номер действия; у контейнеров применений нет.</summary>
    public List<(string Text, int Action)> Uses(long place, int index)
    {
        List<(string Text, int Action)> uses = [];
        if (place != 0 || _items is null || Stack(0, index) is not ItemStack stack)
        {
            return uses;
        }

        foreach (ItemUseDef use in _items.Item(stack.Item).Uses)
        {
            uses.Add((HudModel.Name(use.Action), _items.ActionIndex(use.Action)));
        }

        return uses;
    }

    /// <summary>Применение первого предмета стопки index своего инвентаря действием action.</summary>
    public void Use(int index, int action)
    {
        if (Stack(0, index) is ItemStack stack)
        {
            UseRequested?.Invoke(stack.Ids[0], action);
        }
    }

    /// <summary>Выбор контейнера, куда кладутся предметы из своего инвентаря.</summary>
    public void Select(long place)
    {
        Selected = place;
        if (_ownPanel is InventoryPanel own)
        {
            Show(own, _nearbyPanels);
        }
    }

    /// <summary>Надпись без автоматического перевода: текст уже на нужном языке.</summary>
    private static Label Label(string text) => new() { Text = text, AutoTranslateMode = AutoTranslateModeEnum.Disabled };

    /// <summary>Кнопка без автоматического перевода, текст слева.</summary>
    private static Button Button(string text) => new()
    {
        Text = text,
        AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        Alignment = HorizontalAlignment.Left,
        Flat = true,
    };

    /// <summary>Тексты надписей и кнопок колонки.</summary>
    private static List<string> Lines(VBoxContainer column)
    {
        List<string> lines = [];
        foreach (Node child in column.GetChildren())
        {
            if (child is Label label)
            {
                lines.Add(label.Text);
            }
            else if (child is Button button)
            {
                lines.Add(button.Text);
            }
        }

        return lines;
    }

    /// <summary>Удаление всех строк колонки.</summary>
    private static void Clear(VBoxContainer column)
    {
        foreach (Node child in column.GetChildren())
        {
            column.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>Панель места place из последнего показа или null.</summary>
    private InventoryPanel? Panel(long place)
    {
        if (place == 0)
        {
            return _ownPanel;
        }

        foreach (InventoryPanel panel in _nearbyPanels)
        {
            if (panel.Place == place)
            {
                return panel;
            }
        }

        return null;
    }

    /// <summary>Стопка номер index места place или null, если её уже нет: окно могло обновиться между щелчком и вызовом.</summary>
    private ItemStack? Stack(long place, int index)
    {
        InventoryPanel? panel = Panel(place);
        return panel is not null && index >= 0 && index < panel.Stacks.Count ? panel.Stacks[index] : null;
    }

    /// <summary>Кнопки стопок панели: левая кнопка перекладывает, с Shift - всю стопку, правая открывает меню применений.</summary>
    private void AddRows(VBoxContainer column, InventoryPanel panel)
    {
        for (int i = 0; i < panel.Stacks.Count; i++)
        {
            Button row = Button(Row(panel.Stacks[i]));
            long place = panel.Place;
            int index = i;
            row.GuiInput += input => OnRowInput(input, place, index);
            column.AddChild(row);
        }
    }

    /// <summary>Нажатие мыши на строку стопки.</summary>
    private void OnRowInput(InputEvent input, long place, int index)
    {
        if (input is not InputEventMouseButton { Pressed: true } click)
        {
            return;
        }

        if (click.ButtonIndex == MouseButton.Left)
        {
            PressStack(place, index, click.ShiftPressed);
        }
        else if (click.ButtonIndex == MouseButton.Right)
        {
            OpenMenu(place, index, click.GlobalPosition);
        }
    }

    /// <summary>Меню применений стопки у курсора; без применений не открывается.</summary>
    private void OpenMenu(long place, int index, Vector2 at)
    {
        List<(string Text, int Action)> uses = Uses(place, index);
        if (uses.Count == 0)
        {
            return;
        }

        _menu.Clear();
        for (int i = 0; i < uses.Count; i++)
        {
            _menu.AddItem(uses[i].Text, i);
        }

        _menuFor = (place, index, uses.ConvertAll(use => use.Action));
        _menu.Position = (Vector2I)at;
        _menu.Popup();
    }

    /// <summary>Пункт меню выбран: применение стопки, для которой меню открыто.</summary>
    private void OnMenuPressed(long id)
    {
        if (_menuFor is (_, int stack, List<int> actions))
        {
            Use(stack, actions[(int)id]);
        }
    }
}
