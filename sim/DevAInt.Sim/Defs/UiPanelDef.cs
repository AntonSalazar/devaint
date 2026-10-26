using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Угол экрана, к которому прижата панель интерфейса.</summary>
public enum UiAnchor
{
    /// <summary>Левый верхний угол.</summary>
    TopLeft,

    /// <summary>Правый верхний угол.</summary>
    TopRight,

    /// <summary>Левый нижний угол.</summary>
    BottomLeft,

    /// <summary>Правый нижний угол.</summary>
    BottomRight,
}

/// <summary>Панель интерфейса мода: место на экране, заголовок и строки; клиент только рисует её по данным и значениям от сервера.</summary>
public sealed record UiPanelDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Угол экрана.</summary>
    public UiAnchor Anchor { get; init; } = UiAnchor.TopLeft;

    /// <summary>Ключ перевода заголовка, null - без заголовка.</summary>
    public string? Title { get; init; }

    /// <summary>Ключ значения от Lua: панель видна, только пока значение есть и не пустое, не 0 и не false; null - видна всегда.</summary>
    public string? VisibleWhen { get; init; }

    /// <summary>Строки сверху вниз.</summary>
    public IReadOnlyList<UiRowDef> Rows { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            foreach (string problem in Rows[i].Validate(defs))
            {
                yield return $"rows[{i}]: {problem}";
            }
        }
    }
}

/// <summary>Строка панели: ровно одно из полоса стата, мудлы, предметы, текст, кнопка или разделитель.</summary>
public sealed record UiRowDef
{
    /// <summary>Полоса этого стата своей сущности.</summary>
    public string? Stat { get; init; }

    /// <summary>Действующие мудлы своей сущности.</summary>
    public bool Moodles { get; init; }

    /// <summary>Свои предметы по видам.</summary>
    public bool Items { get; init; }

    /// <summary>Ключ перевода текста; {ключ} в тексте заменяется значением от Lua.</summary>
    public string? Text { get; init; }

    /// <summary>Ключ перевода подписи кнопки; нажатие вызывает хук ui_action с Action.</summary>
    public string? Button { get; init; }

    /// <summary>Id действия кнопки для хука ui_action.</summary>
    public string? Action { get; init; }

    /// <summary>Разделительная черта.</summary>
    public bool Separator { get; init; }

    /// <summary>Проблемы строки.</summary>
    internal IEnumerable<string> Validate(DefDatabase defs)
    {
        int kinds = (Stat is null ? 0 : 1) + (Moodles ? 1 : 0) + (Items ? 1 : 0) + (Text is null ? 0 : 1) + (Button is null ? 0 : 1) + (Separator ? 1 : 0);
        if (kinds != 1)
        {
            yield return "needs exactly one of stat, moodles, items, text, button, separator";
        }

        if (Stat is not null && !defs.TryGet<StatDef>(Stat, out _))
        {
            yield return $"unknown stat {Stat}";
        }

        if ((Button is null) != (Action is null))
        {
            yield return "action is required for button and only for button";
        }
    }
}
