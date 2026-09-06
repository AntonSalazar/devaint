using System.Collections.Generic;
using Godot;

/// <summary>
/// Класс сетки сигнала роя.
/// Хранит покрытие роя в виде массива <see cref="_staticLayer"/> и отвечает на вопрос:
/// "Какая плотность покрытия в этой точке?".
/// Одна структура данных для симуляции и отрисовки.
/// </summary>
public class SignalGrid(Vector2I origin, Vector2I size)
{
    /// <summary>
    /// Гистерезис: уровень снимается только ниже порога минус это значение.
    /// </summary>
    public const float LevelHysteresis = 5.0f;

    /// <summary>
    /// Размер сектора в клетках.
    /// </summary>
    public const int SectorSize = 8;

    /// <summary>
    /// Максимальное значение тревожности сектора в процентах.
    /// </summary>
    public const float NoticeMax = 100.0f;

    /// <summary>
    /// Единица тревожности.
    /// </summary>
    public const float NoticeRate = 2.5f;

    /// <summary>
    /// Спад тревожности в игровых минутах.
    /// </summary>
    public const float DecayPerMinute = 1.5f;

    /// <summary>
    /// Спад тревожности в тиках игровых минутах там, где нет игрока.
    /// </summary>
    public const float AbsentDecayMultiplier = 2.0f;


    /// <summary>
    /// Пороги уровней в % заметности.
    /// </summary>
    private static readonly float[] _levelThresholds = [
        00.0f,
        25.0f,
        50.0f,
        75.0f,
        NoticeMax,
    ];


    /// <summary>
    /// Текущие сектора, у которых установлены уровни угроз.
    /// Если нет ключа - там <see cref="Level.None"/>.
    /// </summary>
    private readonly Dictionary<Vector2I, Level> _levels = [];

    /// <summary>
    /// Слой статики: суммарное покрытие работающих вышек, [0.0f, 1.0f] на клетку.
    /// </summary>
    private readonly float[] _staticLayer = new float[size.X * size.Y];

    /// <summary>
    /// Список вышек, где индекс в массиве - это id вышки.
    /// </summary>
    private readonly List<Tower> _towers = [];

    /// <summary>
    /// Таблица заметности игрока, где ключ - координата сектора,
    /// а значение - уровень тревоги в %.
    /// </summary>
    private readonly Dictionary<Vector2I, float> _notice = [];

    /// <summary>
    /// Буфер секторов для минутного тика.
    /// Переиспользуем, чтоб не аллоцировать.
    /// </summary>
    private readonly List<Vector2I> _sectorBuffer = [];

    /// <summary>
    /// Сектор, где источник был последним. В этом месте спад будет медленней.
    /// </summary>
    private Vector2I _activeSector = Vector2I.Zero;

    /// <summary>
    /// Экземпляр ссылки на закэшированную карту покрытия сети.
    /// </summary>
    private Image? _staticCache = null;


    /// <summary>
    /// Уровни тревоги.
    /// </summary>
    public enum Level
    {
        /// <summary>
        /// Ничего, ниже первого порога.
        /// </summary>
        None,
        /// <summary>
        /// 25% - любопытство.
        /// </summary>
        Curious,
        /// <summary>
        /// 50% - разведка: дрон в район.
        /// </summary>
        Scout,
        /// <summary>
        /// 75% - охота: охотник, перепрошивка вблизи.
        /// </summary>
        Hunt,
        /// <summary>
        /// 100% - тревога: внеочередная волна.
        /// </summary>
        Alarm
    }


    /// <summary>
    /// Клетка верхнего-левого угла грида.
    /// </summary>
    public Vector2I Origin { get; } = origin;

    /// <summary>
    /// Размер грида в клетках.
    /// </summary>
    public Vector2I Size { get; } = size;


    /// <summary>
    /// Статичный метод конвертации координат клетки грида в сектор.
    /// </summary>
    /// <param name="cell">Клетка грида.</param>
    /// <returns>Сектор.</returns>
    public static Vector2I CellToSector(Vector2I cell)
    {
        return new(
            Mathf.FloorToInt((float)cell.X / SectorSize),
            Mathf.FloorToInt((float)cell.Y / SectorSize)
        );
    }


    /// <summary>
    /// Метод возврата сектора по глобальной координате.
    /// </summary>
    /// <param name="position">Глобальная координата.</param>
    /// <returns>Вернет сектор.</returns>
    public Vector2I GetSectorAt(Vector2 position)
    {
        Vector2I cell = Iso.WorldToCell(position);
        return CellToSector(cell);
    }

    /// <summary>
    /// Метод возврата % тревоги в секторе.
    /// </summary>
    /// <param name="sector">Проверяемый сектор.</param>
    /// <returns>Уровень тревоги в %.</returns>
    public float GetNotice(Vector2I sector) => _notice.GetValueOrDefault(sector);


    /// <summary>
    /// Метод возврата % тревоги в секторе по глобальной позиции.
    /// </summary>
    /// <param name="position">Глобальная позиция в px.</param>
    /// <returns>Уровень тревоги в %.</returns>
    public float GetNoticeAt(Vector2 position) => GetNotice(GetSectorAt(position));


    /// <summary>
    /// Метод возврата снимка всех секторов.
    /// Пригодится при постройке карты.
    /// </summary>
    /// <returns>Снимок всех секторов.</returns>
    public Dictionary<Vector2I, float> GetNotices() => new(_notice);


    /// <summary>
    /// Метод возврата уровня тревоги у сектора.
    /// </summary>
    /// <param name="sector">Сектор, в котором проверяем уровень угрозы</param>
    /// <returns>Уровень тревоги.</returns>
    public Level GetLevel(Vector2I sector) => _levels.GetValueOrDefault(sector);


    /// <summary>
    /// Метод возврата уровня тревоги у сектора по глобальной позиции.
    /// </summary>
    /// <param name="position">Глобальная позиция в px.</param>
    /// <returns>Уровень тревоги.</returns>
    public Level GetLevelAt(Vector2 position) => GetLevel(GetSectorAt(position));


    /// <summary>
    /// Метод возврата плотности покрытия клетки в диапазоне [0.0f, 1.0f].
    /// </summary>
    /// <param name="cell">Проверяемая клетка.</param>
    /// <returns>Плотность покрытия.</returns>
    public float GetCoverage(Vector2I cell)
    {
        int idx = CellToIndex(cell);
        return idx >= 0 ? _staticLayer[idx] : 0.0f;
    }


    /// <summary>
    /// Метод возврата плотности покрытия клетки
    /// в мировых координатах px в диапазоне [0.0f, 1.0f].
    /// </summary>
    /// <param name="position">Глобальная позиция в px.</param>
    /// <returns>Плотность покрытия.</returns>
    public float GetCoverageAt(Vector2 position) => GetCoverage(Iso.WorldToCell(position));


    /// <summary>
    /// Метод выгрузки слоя статики в картинку <see cref="Image.Format.Rf"/>
    /// размером с грид, где R - покрытие клетки.
    /// Пригодится для шейдеров и карт.
    /// </summary>
    /// <returns>Картинка слоя статики.</returns>
    public Image GetStaticImage()
    {
        // Возьмем с кэша.
        if (_staticCache is not null)
        {
            return _staticCache;
        }

        // Создаем новую карту.
        Image image = Image.CreateEmpty(Size.X, Size.Y, false, Image.Format.Rf);
        for (int idx = 0; idx < _staticLayer.Length; idx++)
        {
            image.SetPixel(idx % Size.X, idx / Size.X, new Color(_staticLayer[idx], 0.0f, 0.0f));
        }

        // Закэшируем и вернем.
        _staticCache = image;
        return _staticCache;
    }


    /// <summary>
    /// Метод инициализации.
    /// </summary>
    public void Init() => EventBus.Subscribe<GameClock.OnMinutePassed>(OnMinutePassed);


    /// <summary>
    /// Метод деинициализации.
    /// </summary>
    public void Deinit() => EventBus.Unsubscribe<GameClock.OnMinutePassed>(OnMinutePassed);


    /// <summary>
    /// Метод добавления активной вышки.
    /// </summary>
    /// <param name="cell">В какую клетку добавляем.</param>
    /// <param name="radius">Какой радиус имеет вышка.</param>
    /// <returns>id вышки.</returns>
    public int AddTower(Vector2I cell, float radius)
    {
        if (CellToIndex(cell) < 0)
        {
            GD.PushWarning($"{this}.AddTower: `cell:{cell}` out of grid.");
        }
        _towers.Add(new(cell, radius));
        RebuildStatic();
        return _towers.Count - 1;
    }


    /// <summary>
    /// Метод установки флага работы вышки.
    /// </summary>
    /// <param name="idx">Индекс вышки.</param>
    /// <param name="active">Флаг работы.</param>
    public void SetTowerActive(int idx, bool active)
    {
        if (idx < 0 || idx >= _towers.Count)
        {
            GD.PushError($"{this}.SetTowerActive: unknown tower `id:{idx}`");
            return;
        }

        // Отсеем, если флаг не изменился.
        Tower tower = _towers[idx];
        if (tower.Active == active)
        {
            return;
        }

        // Ставим флаг и перестраиваем сеть.
        tower.Active = active;
        RebuildStatic();
    }


    /// <summary>
    /// Метод накопления заметности.
    /// </summary>
    /// <param name="position">Глобальная позиция в px.</param>
    /// <param name="emission">Излучение.</param>
    /// <param name="minutes">Сколько игровых минут прошло.</param>
    public void Accumulate(Vector2 position, float emission, float minutes)
    {
        Vector2I sector = GetSectorAt(position);
        float gain = emission * GetCoverageAt(position) * NoticeRate * minutes;
        _activeSector = sector;
        if (Mathf.IsZeroApprox(gain))
        {
            return;
        }

        // Запишим значение тревоги и обновим уровень.
        float value = Mathf.Min(GetNotice(sector) + gain, NoticeMax);
        _notice[sector] = value;
        UpdateLevel(sector, value);
    }


    /// <summary>
    /// Метод возврата индекса клетки в слоях.
    /// </summary>
    /// <param name="cell">Клетка слоя.</param>
    /// <returns>Индекс клетки.</returns>
    private int CellToIndex(Vector2I cell)
    {
        Vector2I local = cell - Origin;
        return local.X < 0 || local.Y < 0 || local.X >= Size.X || local.Y >= Size.Y ? -1 : (local.Y * Size.X) + local.X;
    }


    /// <summary>
    /// Метод возврата клетки по индексу в слоях.
    /// </summary>
    /// <param name="idx">Индекс клетки.</param>
    /// <returns>Клетка слоя</returns>
    private Vector2I IndexToCell(int idx) => Origin + new Vector2I(idx % Size.X, idx / Size.X);


    /// <summary>
    /// Метод пересчёта уровня тревоги сектора по значению с гистерезисом.
    /// При смене публикуется сообщение <see cref="OnNoticeLevelChanged"/>.
    /// </summary>
    /// <param name="sector">Обновляемый сектор.</param>
    /// <param name="value">Обновленное значение тревоги.</param>
    private void UpdateLevel(Vector2I sector, float value)
    {
        Level previous = GetLevel(sector);
        Level current = previous;

        for (int idx = 1; idx < _levelThresholds.Length; idx++)
        {
            if (value >= _levelThresholds[idx])
            {
                current = (Level)Mathf.Max((int)current, idx);
            }
        }

        while (current > Level.None && value < _levelThresholds[(int)current] - LevelHysteresis)
        {
            current = (Level)((int)current - 1);
        }

        if (current == previous)
        {
            return;
        }

        if (current == Level.None)
        {
            _levels.Remove(sector);
        }
        else
        {
            _levels[sector] = current;
        }

        // Оповестим окружение.
        new OnNoticeLevelChanged(sector, previous, current, value).Push();
    }


    /// <summary>
    /// Метод, вызываемый при получении сообщения
    /// о наступления новой игровой минуты.
    /// </summary>
    /// <param name="message">Сообщение новой игровой минуты.</param>
    private void OnMinutePassed(GameClock.OnMinutePassed message)
    {
        _sectorBuffer.Clear();
        _sectorBuffer.AddRange(_notice.Keys);
        foreach (Vector2I sector in _sectorBuffer)
        {
            float decay = AbsentDecayMultiplier * DecayPerMinute;
            if (sector == _activeSector)
            {
                decay = DecayPerMinute;
            }

            float value = _notice[sector];
            value = Mathf.Max(value - decay, 0.0f);
            _notice[sector] = value;

            if (Mathf.IsZeroApprox(value))
            {
                _notice.Remove(sector);
                UpdateLevel(sector, 0.0f);
            }
            else
            {
                _notice[sector] = value;
                UpdateLevel(sector, value);
            }
        }
    }


    /// <summary>
    /// Метод перестройки сети роя по активным вышкам.
    /// Поле вышки - линейный спад от 1.0f в центре до 0.0f на радиусе,
    /// расстояние считается по земле (по изометрии), сумма ограничивается до 1.0f.
    /// </summary>
    private void RebuildStatic()
    {
        // Заполняем нулями.
        for (int idx = 0; idx < _staticLayer.Length; idx++)
        {
            _staticLayer[idx] = 0.0f;
        }

        // Пройдемся по активным вышкам.
        foreach (Tower tower in _towers)
        {
            if (!tower.Active)
            {
                continue;
            }

            // Вычисляем покрытие.
            Vector2 center = Iso.CellToWorld(tower.Cell);
            for (int idx = 0; idx < _staticLayer.Length; idx++)
            {
                Vector2 point = Iso.CellToWorld(IndexToCell(idx));
                float value = 1.0f - (Iso.GroundDistance(center, point) / tower.Radius);
                _staticLayer[idx] = Mathf.Min(_staticLayer[idx] + Mathf.Max(value, 0.0f), 1.0f);
            }
        }

        // Вещаем, что сетка перестроена.
        _staticCache = null;
        new OnStaticChanged().Push();
    }


    /// <summary>
    /// Класс вышки роя - источник статического покрытия.
    /// </summary>
    public class Tower(Vector2I cell, float radius)
    {
        /// <summary>
        /// Клетка, на которой стоит вышка.
        /// </summary>
        public Vector2I Cell { get; } = cell;

        /// <summary>
        /// Радиус действия вышки.
        /// </summary>
        public float Radius { get; } = radius;

        /// <summary>
        /// Флаг работы вышки.
        /// </summary>
        public bool Active { get; set; } = true;
    }


    /// <summary>
    /// Сообщение об обновлении сетки роя.
    /// </summary>
    public class OnStaticChanged : EventBus.Message { };


    /// <summary>
    /// Сообщение об обновлении уровня угрозы.
    /// </summary>
    /// <param name="sector">Сектор, где обновилась угроза.</param>
    /// <param name="previous">Предыдущий уровень угрозы.</param>
    /// <param name="current">Текущий уровень угрозы.</param>
    /// <param name="value">Уровень угрозы в процентах.</param>
    public class OnNoticeLevelChanged(
            Vector2I sector, Level previous, Level current, float value)
        : EventBus.Message
    {
        /// <summary>
        /// Сектор, где обновилась угроза.
        /// </summary>
        public Vector2I Sector { get; } = sector;

        /// <summary>
        /// Предыдущий уровень угрозы.
        /// </summary>
        public Level Previous { get; } = previous;

        /// <summary>
        /// Текущий уровень угрозы.
        /// </summary>
        public Level Current { get; } = current;

        /// <summary>
        /// Уровень угрозы в процентах.
        /// </summary>
        public float Value { get; } = value;


        /// <summary>
        /// Метод возврата представления экземпляра класса в виде строки.
        /// </summary>
        /// <returns>Вернет строку.</returns>
        public override string ToString() =>
            $"{base.ToString()} {Sector} {Previous} -> {Current} ({Value:F0}%)";
    }


}
