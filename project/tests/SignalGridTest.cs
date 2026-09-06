using System.Collections.Generic;
using Godot;

/// <summary>
/// Тесты <see cref="SignalGrid"/>: пустой грид, поле одной вышки, нахлест и кламп,
/// включение/выключение, границы грида, согласованность с Iso, картинка,
/// событие перестройки; память секторов: накопление, кламп, спад, подписка, уровни.
/// </summary>
public class SignalGridTest : CsTestCase
{
    /// <summary>Радиус тестовой вышки в мировых пикселях.</summary>
    private const float Radius = 300.0f;

    /// <summary>
    /// Расстояние по земле между центрами соседних по оси клеток:
    /// (64, 32) на экране -> (64, 64) по земле.
    /// </summary>
    private const double NeighborDistance = 90.50966799187809;

    /// <summary>Допуск для покрытия: линейный спад считается во float.</summary>
    private const double CoverageTolerance = 0.0001;

    /// <summary>Угол тестового грида.</summary>
    private static readonly Vector2I _origin = new(-10, -10);

    /// <summary>Размер тестового грида.</summary>
    private static readonly Vector2I _size = new(21, 21);

    /// <summary>Полный сброс шины перед каждым тестом.</summary>
    public override void BeforeEach() => EventBus.Reset();

    /// <summary>Сброс шины после теста.</summary>
    public override void AfterEach() => EventBus.Reset();

    /// <summary>Пустой грид - нули везде, включая точки вне грида.</summary>
    public void TestEmptyGridIsZero()
    {
        SignalGrid grid = new(_origin, _size);
        CheckNear(grid.GetCoverage(Vector2I.Zero), 0.0, "origin cell is zero");
        CheckNear(grid.GetCoverage(_origin), 0.0, "corner cell is zero");
        CheckNear(grid.GetCoverage(new Vector2I(100, 100)), 0.0, "outside cell is zero");
        CheckEq(grid.Size, _size, "size is stored");
        CheckEq(grid.Origin, _origin, "origin is stored");
    }

    /// <summary>Одна вышка: 1.0 в центре, линейный спад, 0.0 за радиусом.</summary>
    public void TestSingleTowerField()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);

        CheckNear(grid.GetCoverage(Vector2I.Zero), 1.0, "tower cell is fully covered");
        CheckNear(
            grid.GetCoverage(new Vector2I(1, 0)), 1.0 - (NeighborDistance / Radius),
            "neighbor cell follows the linear falloff", CoverageTolerance);
        CheckTrue(
            grid.GetCoverage(new Vector2I(1, 0)) > grid.GetCoverage(new Vector2I(2, 0))
                && grid.GetCoverage(new Vector2I(2, 0)) > grid.GetCoverage(new Vector2I(3, 0)),
            "coverage decreases with distance");
        CheckNear(grid.GetCoverage(new Vector2I(5, 0)), 0.0, "cell beyond the radius is zero");
    }

    /// <summary>Поле симметрично по земле: клетки на равном наземном расстоянии равны.</summary>
    public void TestFieldIsGroundSymmetric()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);

        float diagonal = grid.GetCoverage(new Vector2I(1, 1));
        CheckNear(grid.GetCoverage(new Vector2I(1, -1)), diagonal, "(1,-1) equals (1,1)");
        CheckNear(grid.GetCoverage(new Vector2I(-1, -1)), diagonal, "(-1,-1) equals (1,1)");
        CheckNear(grid.GetCoverage(new Vector2I(-1, 1)), diagonal, "(-1,1) equals (1,1)");
    }

    /// <summary>Две вышки внахлест - сумма клампится в 1.0.</summary>
    public void TestOverlapClampsToOne()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);
        grid.AddTower(new Vector2I(1, 0), Radius);

        CheckNear(grid.GetCoverage(Vector2I.Zero), 1.0, "overlap does not exceed 1.0");
        CheckNear(grid.GetCoverage(new Vector2I(1, 0)), 1.0, "second tower cell is also 1.0");
    }

    /// <summary>Выключение вышки обнуляет поле, включение - возвращает.</summary>
    public void TestTowerToggle()
    {
        SignalGrid grid = new(_origin, _size);
        int id = grid.AddTower(Vector2I.Zero, Radius);

        grid.SetTowerActive(id, false);
        CheckNear(grid.GetCoverage(Vector2I.Zero), 0.0, "disabled tower gives no coverage");
        grid.SetTowerActive(id, true);
        CheckNear(grid.GetCoverage(Vector2I.Zero), 1.0, "re-enabled tower restores coverage");
    }

    /// <summary>Неизвестный id вышки - ошибка без падения и без изменений поля.</summary>
    public void TestUnknownTowerIdIsSafe()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);

        GD.Print("        (an ERROR from SignalGrid is expected below - part of the test)");
        grid.SetTowerActive(99, false);
        CheckNear(grid.GetCoverage(Vector2I.Zero), 1.0, "field is unchanged after a bad id");
    }

    /// <summary>Вышка за пределами грида не ломает его, поле обрезается границей.</summary>
    public void TestTowerOutsideGrid()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(new Vector2I(12, 0), Radius);

        CheckNear(grid.GetCoverage(new Vector2I(12, 0)), 0.0, "outside cell reads zero");
        CheckTrue(grid.GetCoverage(new Vector2I(10, 0)) > 0.0f, "edge cell inside the grid is covered");
    }

    /// <summary>Запрос по мировой точке согласован с запросом по клетке.</summary>
    public void TestCoverageAtMatchesCell()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);

        int mismatches = 0;
        for (int x = -3; x <= 3; x++)
        {
            for (int y = -3; y <= 3; y++)
            {
                Vector2I cell = new(x, y);
                float byPoint = grid.GetCoverageAt(Iso.CellToWorld(cell));
                if (!Mathf.IsEqualApprox(byPoint, grid.GetCoverage(cell)))
                {
                    mismatches++;
                }
            }
        }
        CheckEq(mismatches, 0, "GetCoverageAt agrees with GetCoverage");
    }

    /// <summary>Картинка статики: размер грида, R-канал = покрытие клетки.</summary>
    public void TestStaticImage()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);
        Image image = grid.GetStaticImage();

        CheckEq(image.GetSize(), _size, "image size equals grid size");
        CheckEq(image.GetFormat(), Image.Format.Rf, "image format is RF");
        Vector2I local = new Vector2I(1, 0) - _origin;
        CheckNear(
            image.GetPixel(local.X, local.Y).R, grid.GetCoverage(new Vector2I(1, 0)),
            "pixel R equals cell coverage");
        CheckTrue(ReferenceEquals(grid.GetStaticImage(), image), "image is cached");
    }

    /// <summary>OnStaticChanged публикуется на каждой перестройке.</summary>
    public void TestStaticChangedMessage()
    {
        int count = 0;
        EventBus.Subscribe<SignalGrid.OnStaticChanged>(message => count++);
        SignalGrid grid = new(_origin, _size);

        int id = grid.AddTower(Vector2I.Zero, Radius);
        CheckEq(count, 1, "AddTower publishes OnStaticChanged");
        grid.SetTowerActive(id, false);
        CheckEq(count, 2, "toggle publishes OnStaticChanged");
        grid.SetTowerActive(id, false);
        CheckEq(count, 2, "no-op toggle does not publish");
    }

    /// <summary>Сектор клетки: квадраты SectorSize, отрицательные клетки не схлопываются.</summary>
    public void TestCellToSector()
    {
        int size = SignalGrid.SectorSize;
        CheckEq(SignalGrid.CellToSector(Vector2I.Zero), Vector2I.Zero, "(0,0) -> (0,0)");
        CheckEq(
            SignalGrid.CellToSector(new Vector2I(size - 1, size - 1)), Vector2I.Zero,
            "last cell of sector 0");
        CheckEq(SignalGrid.CellToSector(new Vector2I(size, 0)), new Vector2I(1, 0), "first cell of sector 1");
        CheckEq(SignalGrid.CellToSector(new Vector2I(-1, -1)), new Vector2I(-1, -1), "(-1,-1) -> (-1,-1)");
        CheckEq(SignalGrid.CellToSector(new Vector2I(-size, 3)), new Vector2I(-1, 0), "(-size,3) -> (-1,0)");
        CheckEq(
            SignalGrid.CellToSector(new Vector2I(-size - 1, 0)), new Vector2I(-2, 0),
            "(-size-1,0) -> (-2,0)");
    }

    /// <summary>Накопление: излучение x покрытие x NoticeRate x минуты в сектор позиции.</summary>
    public void TestAccumulateFullCoverage()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);
        Vector2 center = Iso.CellToWorld(Vector2I.Zero);

        grid.Accumulate(center, 3.0f, 1.0f);
        CheckNear(
            grid.GetNotice(Vector2I.Zero), 3.0 * SignalGrid.NoticeRate,
            "walk emission for one minute at full coverage");
        CheckNear(grid.GetNoticeAt(center), grid.GetNotice(Vector2I.Zero), "GetNoticeAt agrees");
        grid.Accumulate(center, 1.0f, 0.5f);
        CheckNear(grid.GetNotice(Vector2I.Zero), 3.5 * SignalGrid.NoticeRate, "accumulation adds up");
    }

    /// <summary>Без покрытия след не пишется и запись сектора не создается.</summary>
    public void TestAccumulateWithoutCoverage()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);

        grid.Accumulate(Iso.CellToWorld(new Vector2I(9, 9)), 25.0f, 10.0f);
        CheckNear(grid.GetNotice(SignalGrid.CellToSector(new Vector2I(9, 9))), 0.0, "no notice");
        CheckEq(grid.GetNotices().Count, 0, "no sector record is created for zero gain");
    }

    /// <summary>Заметность клампится в NoticeMax.</summary>
    public void TestAccumulateClamps()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);

        grid.Accumulate(Iso.CellToWorld(Vector2I.Zero), 25.0f, 1000.0f);
        CheckNear(grid.GetNotice(Vector2I.Zero), SignalGrid.NoticeMax, "clamped at NoticeMax");
    }

    /// <summary>След пишется в сектор позиции, а не куда-то еще.</summary>
    public void TestAccumulateTargetsOwnSector()
    {
        SignalGrid grid = new(_origin, _size);
        Vector2I far = new(SignalGrid.SectorSize + 1, 1);
        grid.AddTower(Vector2I.Zero, Radius);
        grid.AddTower(far, Radius);

        grid.Accumulate(Iso.CellToWorld(far), 1.0f, 1.0f);
        CheckNear(grid.GetNotice(new Vector2I(1, 0)), SignalGrid.NoticeRate, "far sector got the notice");
        CheckNear(grid.GetNotice(Vector2I.Zero), 0.0, "home sector is untouched");
    }

    /// <summary>Спад по минутному тику: активный сектор медленнее, чужие быстрее, нули удаляются.</summary>
    public void TestDecayOnMinuteTick()
    {
        SignalGrid grid = new(_origin, _size);
        Vector2I far = new(SignalGrid.SectorSize + 1, 1);
        grid.AddTower(Vector2I.Zero, Radius);
        grid.AddTower(far, Radius);
        grid.Init();

        float start = 10.0f / SignalGrid.NoticeRate;
        grid.Accumulate(Iso.CellToWorld(far), start, 1.0f);
        grid.Accumulate(Iso.CellToWorld(Vector2I.Zero), start, 1.0f);
        PushMinute();
        CheckNear(
            grid.GetNotice(Vector2I.Zero), 10.0 - SignalGrid.DecayPerMinute,
            "active sector decays by DecayPerMinute");
        CheckNear(
            grid.GetNotice(new Vector2I(1, 0)),
            10.0 - (SignalGrid.DecayPerMinute * SignalGrid.AbsentDecayMultiplier),
            "absent sector decays faster");

        grid.Accumulate(Iso.CellToWorld(far), 1.0f, 1.0f);
        grid.Accumulate(Iso.CellToWorld(Vector2I.Zero), 0.0f, 1.0f);
        for (int idx = 0; idx < 10; idx++)
        {
            PushMinute();
        }
        CheckTrue(
            !grid.GetNotices().ContainsKey(new Vector2I(1, 0)),
            "sector that decayed to zero is erased");
        grid.Deinit();
    }

    /// <summary>Снимок секторов - копия: правка снимка не трогает грид.</summary>
    public void TestNoticesSnapshotIsACopy()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);
        grid.Accumulate(Iso.CellToWorld(Vector2I.Zero), 1.0f, 1.0f);

        Dictionary<Vector2I, float> snapshot = grid.GetNotices();
        snapshot[Vector2I.Zero] = 99.0f;
        CheckNear(grid.GetNotice(Vector2I.Zero), SignalGrid.NoticeRate, "grid is unaffected by snapshot edits");
    }

    /// <summary>Init подписывает грид на тики, Deinit - отписывает (по поведению).</summary>
    public void TestInitDeinitSubscription()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);
        Vector2 center = Iso.CellToWorld(Vector2I.Zero);
        grid.Accumulate(center, 10.0f / SignalGrid.NoticeRate, 1.0f);

        PushMinute();
        CheckNear(grid.GetNotice(Vector2I.Zero), 10.0, "no decay before Init");
        grid.Init();
        PushMinute();
        CheckNear(grid.GetNotice(Vector2I.Zero), 10.0 - SignalGrid.DecayPerMinute, "decays after Init");
        grid.Deinit();
        PushMinute();
        CheckNear(grid.GetNotice(Vector2I.Zero), 10.0 - SignalGrid.DecayPerMinute, "no decay after Deinit");
    }

    /// <summary>Подъем через 25% публикует None -> Curious с сектором и значением.</summary>
    public void TestLevelUpPublishes()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);
        List<SignalGrid.OnNoticeLevelChanged> messages = CollectLevels();

        grid.Accumulate(Iso.CellToWorld(Vector2I.Zero), 25.0f / SignalGrid.NoticeRate, 1.0f);
        CheckEq(messages.Count, 1, "one level message");
        if (messages.Count == 1)
        {
            CheckEq(messages[0].Sector, Vector2I.Zero, "sector in payload");
            CheckEq(messages[0].Previous, SignalGrid.Level.None, "previous None");
            CheckEq(messages[0].Current, SignalGrid.Level.Curious, "level Curious");
            CheckNear(messages[0].Value, 25.0, "value in payload", CoverageTolerance);
        }
        CheckEq(grid.GetLevel(Vector2I.Zero), SignalGrid.Level.Curious, "GetLevel");
        CheckEq(
            grid.GetLevelAt(Iso.CellToWorld(Vector2I.Zero)), SignalGrid.Level.Curious,
            "GetLevelAt agrees");
    }

    /// <summary>Скачок через несколько порогов разом - одно сообщение до верхнего уровня.</summary>
    public void TestLevelJumpPublishesOnce()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);
        List<SignalGrid.OnNoticeLevelChanged> messages = CollectLevels();

        grid.Accumulate(Iso.CellToWorld(Vector2I.Zero), 60.0f / SignalGrid.NoticeRate, 1.0f);
        CheckEq(messages.Count, 1, "a single message for a multi-threshold jump");
        if (messages.Count == 1)
        {
            CheckEq(messages[0].Previous, SignalGrid.Level.None, "from None");
            CheckEq(messages[0].Current, SignalGrid.Level.Scout, "straight to Scout");
        }
    }

    /// <summary>Гистерезис: уровень держится до порога минус LevelHysteresis.</summary>
    public void TestLevelHysteresisOnDecay()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);
        List<SignalGrid.OnNoticeLevelChanged> messages = CollectLevels();
        grid.Init();
        grid.Accumulate(Iso.CellToWorld(Vector2I.Zero), 25.0f / SignalGrid.NoticeRate, 1.0f);
        CheckEq(messages.Count, 1, "Curious reached");

        // Один тик: 25 -> 23.5, это выше 25 - 5 - уровень держится, сообщений нет.
        PushMinute();
        CheckEq(grid.GetLevel(Vector2I.Zero), SignalGrid.Level.Curious, "still Curious at 23.5");
        CheckEq(messages.Count, 1, "no message inside the hysteresis band");

        // Тикаем, пока не упадем ниже 20: должно прийти Curious -> None.
        for (int idx = 0; idx < 3; idx++)
        {
            PushMinute();
        }
        CheckEq(grid.GetLevel(Vector2I.Zero), SignalGrid.Level.None, "dropped to None below 20");
        CheckEq(messages.Count, 2, "one message for the drop");
        if (messages.Count == 2)
        {
            CheckEq(messages[1].Previous, SignalGrid.Level.Curious, "drop: previous Curious");
            CheckEq(messages[1].Current, SignalGrid.Level.None, "drop: level None");
        }
        grid.Deinit();
    }

    /// <summary>Сектор, остывший до нуля, снимает уровень сообщением.</summary>
    public void TestLevelClearedOnErase()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, Radius);
        List<SignalGrid.OnNoticeLevelChanged> messages = CollectLevels();
        grid.Init();
        grid.Accumulate(Iso.CellToWorld(Vector2I.Zero), 26.0f / SignalGrid.NoticeRate, 1.0f);

        for (int idx = 0; idx < 30; idx++)
        {
            PushMinute();
        }
        CheckEq(grid.GetNotices().Count, 0, "record erased");
        CheckEq(grid.GetLevel(Vector2I.Zero), SignalGrid.Level.None, "level None after erase");
        CheckTrue(messages.Count > 0, "level messages were published");
        if (messages.Count > 0)
        {
            CheckEq(messages[^1].Current, SignalGrid.Level.None, "last message is the drop to None");
        }
        grid.Deinit();
    }

    /// <summary>Подписка-сборщик сообщений о смене уровня.</summary>
    /// <returns>Список, куда шина складывает сообщения.</returns>
    private static List<SignalGrid.OnNoticeLevelChanged> CollectLevels()
    {
        List<SignalGrid.OnNoticeLevelChanged> messages = [];
        EventBus.Subscribe<SignalGrid.OnNoticeLevelChanged>(messages.Add);
        return messages;
    }

    /// <summary>Публикация минутного тика с валидным снимком времени.</summary>
    private static void PushMinute() =>
        new GameClock.OnMinutePassed(1, new GameClock.GameTime(Day: 1, Hour: 0, Minute: 1)).Push();
}
