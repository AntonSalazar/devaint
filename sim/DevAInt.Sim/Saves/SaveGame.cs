using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Net;

namespace DevAInt.Sim.Saves;

/// <summary>Сохранение в папке: meta.json, регионы чанков с сущностями и персонажи игроков отдельными файлами.</summary>
public sealed partial class SaveGame(string directory)
{
    /// <summary>Версия формата файлов сохранения: в 3 у чанков появились объекты, в 4 - содержимое контейнеров, в 5 - вид идущего действия.</summary>
    public const byte FormatVersion = 5;

    /// <summary>Самый старый формат, который ещё читается: 2, без объектов в регионах.</summary>
    public const byte OldestFormat = 2;

    /// <summary>Первый формат с содержимым контейнеров.</summary>
    private const byte FormatWithContainers = 4;

    /// <summary>Первый формат с объектами в чанках.</summary>
    private const byte FormatWithObjects = 3;

    /// <summary>Наибольшее число предметов в контейнере и номер id в таблице, защита от мусора.</summary>
    private const int MaxItems = 1_000_000;

    /// <summary>Наибольшее число объектов в чанке, защита от мусора.</summary>
    private const int MaxObjects = World.ChunkSize * World.ChunkSize * World.LevelCount;

    /// <summary>Имя файла меты.</summary>
    private const string MetaName = "meta.json";

    /// <summary>Метка файла региона.</summary>
    private const string RegionMagic = "DVRG";

    /// <summary>Метка файла игрока.</summary>
    private const string PlayerMagic = "DVPL";

    /// <summary>Наибольшее число сущностей в чанке, защита от мусора.</summary>
    private const int MaxEntities = 1_000_000;

    /// <summary>Настройки JSON меты.</summary>
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Таблица id сохранения.</summary>
    private SaveIds _ids = new();

    /// <summary>Регионы, где лежали сущности при последней записи или загрузке: их нужно переписать, даже если сущности ушли.</summary>
    private HashSet<(int X, int Y)> _entityRegions = [];

    /// <summary>Папка сохранения.</summary>
    public string Directory { get; } = directory;

    /// <summary>Есть ли сохранение в папке.</summary>
    public bool Exists => File.Exists(MetaPath);

    /// <summary>Путь меты.</summary>
    private string MetaPath => Path.Combine(Directory, MetaName);

    /// <summary>Подходит ли имя сохранения или id игрока: оно становится именем файла, поэтому только латиница, цифры, - и _.</summary>
    public static bool IsValidName(string id) => PlayerIdRegex().IsMatch(id);

    /// <summary>Sim из сохранения в папке или новый на карте mapId, если сохранения ещё нет.</summary>
    public Simulation LoadOrCreate(ModLoadResult mods, ISimLog log, string mapId, double timeScale = 1) => Exists ? Load(mods, log, timeScale) : new Simulation(mods, log, mapId, new Stats.GameClock(timeScale));

    /// <summary>Запись изменённых регионов, персонажей игроков в мире и меты.</summary>
    public void Save(Simulation sim)
    {
        string mapId = sim.MapId ?? throw new InvalidOperationException("a simulation without a map cannot be saved");
        ushort[] floors = ToSaveNumbers(sim.WorldTable.FloorCount, i => sim.WorldTable.Floor(i).Id, _ids.Floors);
        ushort[] walls = ToSaveNumbers(sim.WorldTable.WallCount, i => sim.WorldTable.Wall(i).Id, _ids.Walls);
        ushort[] objects = ToSaveNumbers(sim.WorldTable.ObjectCount, i => sim.WorldTable.Object(i).Id, _ids.Objects);
        RegisterIds(sim);

        // Мета первой и с полной таблицей id: если запись оборвётся дальше, старые и новые файлы
        // читаются по этой таблице, а счётчики в ней не меньше id в любом файле
        WriteMeta(sim, mapId);

        // Сущности меняются каждый тик, поэтому их регионы переписываются всегда
        Dictionary<(int X, int Y), List<Entity>> entities = EntitiesByRegion(sim);
        HashSet<(int X, int Y)> regions = [.. sim.World.DirtyRegions, .. entities.Keys, .. _entityRegions];
        foreach ((int X, int Y) region in regions)
        {
            WriteRegion(sim, region, entities.GetValueOrDefault(region) ?? [], new SaveNumbers(floors, walls, objects));
        }

        foreach (Entity player in sim.Entities.Where(entity => entity.PlayerId is not null))
        {
            WritePlayer(sim, player);
        }

        sim.World.ClearDirtyRegions();
        _entityRegions = [.. entities.Keys];
    }

    /// <summary>Sim из сохранения на текущих модах; повреждённые файлы или нужные id без модов - SaveException.</summary>
    public Simulation Load(ModLoadResult mods, ISimLog log, double timeScale = 1)
    {
        SaveMeta meta = ReadMeta();
        if (!mods.Defs.TryGet<MapDef>(meta.MapId, out _))
        {
            throw new SaveException($"{MetaName}: map {meta.MapId} is not in the loaded mods");
        }

        _ids = new SaveIds(meta.Ids);
        Simulation sim = new(mods, log, meta.MapId, new Stats.GameClock(timeScale, meta.Tick), CreateWorld(meta));
        SaveTables tables = new(sim.StatTable, sim.ItemTable, sim.WorldTable, sim.CreatureTable);
        RegionReader regions = new(sim.World, _ids, tables, log);
        foreach (string file in RegionFiles())
        {
            // Формат у каждого файла свой: запись могла оборваться посреди перехода на новый формат
            byte[] payload = SaveFile.ReadBinary(file, RegionMagic, out byte format);
            regions.Read(file, payload, format);
        }

        if (regions.Missing.Count > 0)
        {
            throw new SaveException($"save needs ids missing from loaded mods: {string.Join(", ", regions.Missing.Order())}");
        }

        foreach (Entity entity in regions.Entities)
        {
            sim.RestoreEntity(entity);
        }

        // Мета пишется до файлов, поэтому её счётчики не меньше id в любом файле, в том числе у игроков не в сети
        sim.NextEntityId = Math.Max(sim.NextEntityId, meta.NextEntityId);
        sim.NextItemId = Math.Max(sim.NextItemId, meta.NextItemId);
        sim.World.NextObjectId = Math.Max(sim.World.NextObjectId, meta.NextObjectId);

        sim.World.ClearDirtyRegions();
        sim.World.ClearChanges();

        // Регионы старого формата переписываются при следующей записи все сразу
        foreach ((int X, int Y) region in regions.OldRegions)
        {
            sim.World.MarkRegionDirty(region);
        }

        _entityRegions = [.. regions.Entities.Select(entity => World.RegionOf(entity.Position.Cell))];
        return sim;
    }

    /// <summary>Запись одного персонажа игрока, например когда игрок вышел; мета пишется первой, как при полной записи.</summary>
    public void SavePlayer(Simulation sim, Entity player)
    {
        string mapId = sim.MapId ?? throw new InvalidOperationException("a simulation without a map cannot be saved");
        RegisterIds(sim);
        WriteMeta(sim, mapId);
        WritePlayer(sim, player.PlayerId is null ? throw new ArgumentException("entity is not a player", nameof(player)) : player);
    }

    /// <summary>Персонаж игрока из сохранения в мир, id сущности; null если у игрока ещё нет персонажа.</summary>
    public long? LoadPlayer(Simulation sim, string playerId, ISimLog log)
    {
        string path = PlayerPath(playerId);
        if (!File.Exists(path))
        {
            return null;
        }

        HashSet<string> missing = [];
        Entity entity = ReadPlayer(path, new SaveTables(sim.StatTable, sim.ItemTable, sim.WorldTable, sim.CreatureTable), missing, log);
        if (missing.Count > 0)
        {
            throw new SaveException($"player {playerId} needs ids missing from loaded mods: {string.Join(", ", missing.Order())}");
        }

        sim.RestoreEntity(entity);
        return entity.Id;
    }

    /// <summary>Номера сохранения для номеров таблицы мира; 0 остаётся пустотой.</summary>
    private static ushort[] ToSaveNumbers(int count, Func<ushort, string> idOf, SaveIdList ids)
    {
        ushort[] numbers = new ushort[count + 1];
        for (int i = 1; i <= count; i++)
        {
            numbers[i] = checked((ushort)ids.NumberOf(idOf((ushort)i)));
        }

        return numbers;
    }

    /// <summary>Сущности не игроков по регионам их позиции.</summary>
    private static Dictionary<(int X, int Y), List<Entity>> EntitiesByRegion(Simulation sim)
    {
        Dictionary<(int X, int Y), List<Entity>> regions = [];
        foreach (Entity entity in sim.Entities.Where(entity => entity.PlayerId is null))
        {
            (int X, int Y) region = World.RegionOf(entity.Position.Cell);
            if (!regions.TryGetValue(region, out List<Entity>? list))
            {
                regions[region] = list = [];
            }

            list.Add(entity);
        }

        return regions;
    }

    /// <summary>Пустой мир размера из меты.</summary>
    private static World CreateWorld(SaveMeta meta)
    {
        try
        {
            return new World(meta.Width, meta.Height);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new SaveException($"{MetaName}: world size {meta.Width}x{meta.Height} is not valid");
        }
    }

    /// <summary>Чанк, в котором лежит позиция.</summary>
    private static (int X, int Y) ChunkOf(Entity entity) => (entity.Position.Cell.X / World.ChunkSize, entity.Position.Cell.Y / World.ChunkSize);

    /// <summary>Замена номеров по таблице перевода.</summary>
    private static void Renumber(ushort[] values, ushort[] map)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = map[values[i]];
        }
    }

    /// <summary>Разбор данных файла целиком; ошибки чтения - SaveException с именем файла.</summary>
    private static T Decode<T>(string path, byte[] payload, Func<PacketReader, T> read)
    {
        PacketReader reader = new(payload);
        try
        {
            T result = read(reader);
            return reader.AtEnd ? result : throw new ProtocolException("file has trailing bytes");
        }
        catch (ProtocolException error)
        {
            throw new SaveException($"{Path.GetFileName(path)}: {error.Message}");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex PlayerIdRegex();

    [GeneratedRegex(@"^r\.(\d+)\.(\d+)\.bin$")]
    private static partial Regex RegionNameRegex();

    /// <summary>Все id статов, предметов и действий текущих модов в таблицу сохранения до записи файлов.</summary>
    private void RegisterIds(Simulation sim)
    {
        for (int i = 0; i < sim.StatTable.StatCount; i++)
        {
            _ids.Stats.NumberOf(sim.StatTable.Stat(i).Id);
        }

        for (int i = 0; i < sim.ItemTable.ItemCount; i++)
        {
            _ids.Items.NumberOf(sim.ItemTable.Item(i).Id);
        }

        for (int i = 0; i < sim.ItemTable.ActionCount; i++)
        {
            _ids.Actions.NumberOf(sim.ItemTable.Action(i).Id);
        }

        for (int i = 0; i < sim.ItemTable.RecipeCount; i++)
        {
            _ids.Recipes.NumberOf(sim.ItemTable.Recipe(i).Id);
        }

        for (int i = 0; i < sim.CreatureTable.Count; i++)
        {
            _ids.Creatures.NumberOf(sim.CreatureTable.Creature(i).Id);
        }
    }

    /// <summary>Мета: формат, карта, время, счётчики id, моды и таблица id.</summary>
    private void WriteMeta(Simulation sim, string mapId)
    {
        SaveMeta meta = new()
        {
            Format = FormatVersion,
            MapId = mapId,
            Width = sim.World.Width,
            Height = sim.World.Height,
            Tick = sim.Clock.Tick,
            NextEntityId = sim.NextEntityId,
            NextItemId = sim.NextItemId,
            NextObjectId = sim.World.NextObjectId,
            Mods = [.. sim.Mods],
            Ids = _ids.ToLists(),
        };
        SaveFile.WriteText(MetaPath, JsonSerializer.Serialize(meta, _json) + "\n");
    }

    /// <summary>Файл персонажа игрока.</summary>
    private void WritePlayer(Simulation sim, Entity player)
    {
        PacketWriter writer = new();
        EntityCodec.Write(writer, player, _ids, sim.StatTable, sim.WorldTable);
        SaveFile.WriteBinary(PlayerPath(player.PlayerId!), PlayerMagic, writer.ToArray());
    }

    /// <summary>Файл региона: чанки со слоями, объектами или сущностями; пустой регион пишется, только если его файл уже есть.</summary>
    private void WriteRegion(Simulation sim, (int X, int Y) region, List<Entity> entities, SaveNumbers numbers)
    {
        World world = sim.World;
        List<((int X, int Y) Chunk, List<(int Z, ChunkLevel Level)> Levels, List<Entity> Entities)> chunks = [];
        for (int y = region.Y * World.RegionSize; y < Math.Min((region.Y + 1) * World.RegionSize, world.ChunksY); y++)
        {
            for (int x = region.X * World.RegionSize; x < Math.Min((region.X + 1) * World.RegionSize, world.ChunksX); x++)
            {
                List<(int Z, ChunkLevel Level)> levels = [.. world.ChunkLevels(x, y)];
                List<Entity> inside = [.. entities.Where(entity => ChunkOf(entity) == (x, y))];
                if (levels.Count > 0 || inside.Count > 0 || world.ChunkObjects(x, y).Count > 0)
                {
                    chunks.Add(((x, y), levels, inside));
                }
            }
        }

        string path = RegionPath(region);
        if (chunks.Count == 0 && !File.Exists(path))
        {
            return;
        }

        PacketWriter writer = new();
        writer.WriteVarUInt((ulong)chunks.Count);
        foreach (((int X, int Y) chunk, List<(int Z, ChunkLevel Level)> levels, List<Entity> inside) in chunks)
        {
            writer.WriteVarUInt((ulong)chunk.X);
            writer.WriteVarUInt((ulong)chunk.Y);
            writer.WriteVarUInt((ulong)levels.Count);
            foreach ((int z, ChunkLevel level) in levels)
            {
                ChunkLevelData data = ChunkCodec.Copy(z, level);
                Renumber(data.Floors, numbers.Floors);
                Renumber(data.NorthWalls, numbers.Walls);
                Renumber(data.WestWalls, numbers.Walls);
                ChunkCodec.Write(writer, data);
            }

            IReadOnlyList<WorldObject> objects = world.ChunkObjects(chunk.X, chunk.Y);
            writer.WriteVarUInt((ulong)objects.Count);
            foreach (WorldObject obj in objects)
            {
                writer.WriteVarInt(obj.Id);
                writer.WriteVarUInt(numbers.Objects[obj.Def]);
                writer.WriteVarInt(obj.Cell.X);
                writer.WriteVarInt(obj.Cell.Y);
                writer.WriteVarInt(obj.Cell.Z);
                writer.WriteByte((byte)obj.Rotation);

                // Счётчик пишется и у объекта без контейнера: вид мог перестать быть контейнером, а файл читается дальше
                IReadOnlyList<Item> items = world.Container(obj.Id)?.Items ?? [];
                writer.WriteVarUInt((ulong)items.Count);
                foreach (Item item in items)
                {
                    writer.WriteVarInt(item.Id);
                    writer.WriteVarUInt((ulong)_ids.Items.NumberOf(item.Def.Id));
                }
            }

            writer.WriteVarUInt((ulong)inside.Count);
            foreach (Entity entity in inside)
            {
                EntityCodec.Write(writer, entity, _ids, sim.StatTable, sim.WorldTable);
            }
        }

        SaveFile.WriteBinary(path, RegionMagic, writer.ToArray());
    }

    /// <summary>Мета из файла; нет файла, повреждённый JSON или другой формат - SaveException.</summary>
    private SaveMeta ReadMeta()
    {
        if (!Exists)
        {
            throw new SaveException($"no save in {Directory}");
        }

        SaveMeta? meta;
        try
        {
            meta = JsonSerializer.Deserialize<SaveMeta>(File.ReadAllText(MetaPath), _json);
        }
        catch (JsonException error)
        {
            throw new SaveException($"{MetaName}: {Jsonc.ShortMessage(error.Message)}");
        }

        if (meta is null)
        {
            throw new SaveException($"{MetaName}: file is empty");
        }

        bool known = meta.Format is >= OldestFormat and <= FormatVersion;
        return known ? meta : throw new SaveException($"{MetaName}: save format {meta.Format} is not supported, expected {FormatVersion}");
    }

    /// <summary>Файлы регионов по имени.</summary>
    private IEnumerable<string> RegionFiles()
    {
        string regions = Path.Combine(Directory, "regions");
        if (!System.IO.Directory.Exists(regions))
        {
            return [];
        }

        return System.IO.Directory.GetFiles(regions, "r.*.bin").Where(file => RegionNameRegex().IsMatch(Path.GetFileName(file))).Order(StringComparer.Ordinal);
    }

    /// <summary>Персонаж из файла игрока.</summary>
    private Entity ReadPlayer(string path, SaveTables tables, ISet<string> missing, ISimLog log)
    {
        byte[] payload = SaveFile.ReadBinary(path, PlayerMagic, out byte format);
        return Decode(path, payload, reader => EntityCodec.Read(reader, _ids, tables, missing, log, format));
    }

    /// <summary>Путь файла региона.</summary>
    private string RegionPath((int X, int Y) region) => Path.Combine(Directory, "regions", $"r.{region.X}.{region.Y}.bin");

    /// <summary>Путь файла игрока; неверный id - ArgumentException, чтобы не выйти из папки сохранения.</summary>
    private string PlayerPath(string playerId)
    {
        return IsValidName(playerId)
            ? Path.Combine(Directory, "players", playerId + ".bin")
            : throw new ArgumentException($"player id {playerId} must be 1-64 latin letters, digits, - or _", nameof(playerId));
    }

    /// <summary>Номера сохранения для номеров таблицы мира: полы, стены и объекты.</summary>
    private sealed record SaveNumbers(ushort[] Floors, ushort[] Walls, ushort[] Objects);

    /// <summary>Чтение регионов в мир: слои чанков с переводом номеров, объекты, если они есть в формате, сущности и id, которых нет в модах.</summary>
    private sealed class RegionReader(World world, SaveIds ids, SaveTables tables, ISimLog log)
    {
        /// <summary>Номера полов сохранения в номера текущих модов, -1 если пола нет в модах.</summary>
        private readonly int[] _floors = FromSaveNumbers(ids.Floors, id => tables.World.TryFloorIndex(id, out ushort index) ? index : -1);

        /// <summary>Номера стен сохранения в номера текущих модов, -1 если стены нет в модах.</summary>
        private readonly int[] _walls = FromSaveNumbers(ids.Walls, id => tables.World.TryWallIndex(id, out ushort index) ? index : -1);

        /// <summary>Номера объектов сохранения в номера текущих модов, -1 если объекта нет в модах.</summary>
        private readonly int[] _objects = FromSaveNumbers(ids.Objects, id => tables.World.TryObjectIndex(id, out ushort index) ? index : -1);

        /// <summary>Id из сохранения, которых нет в модах.</summary>
        public HashSet<string> Missing { get; } = [];

        /// <summary>Регионы старого формата без объектов.</summary>
        public List<(int X, int Y)> OldRegions { get; } = [];

        /// <summary>Сущности регионов.</summary>
        public List<Entity> Entities { get; } = [];

        /// <summary>Один файл региона формата format.</summary>
        public void Read(string path, byte[] payload, byte format)
        {
            Match name = RegionNameRegex().Match(Path.GetFileName(path));
            (int X, int Y) region = (int.Parse(name.Groups[1].Value), int.Parse(name.Groups[2].Value));
            if (format < FormatVersion)
            {
                OldRegions.Add(region);
            }

            Decode(path, payload, reader =>
            {
                int count = reader.ReadCount(World.RegionSize * World.RegionSize);
                for (int i = 0; i < count; i++)
                {
                    ReadChunk(reader, region, format);
                }

                return true;
            });
        }

        /// <summary>Перевод номеров сохранения в номера модов через id.</summary>
        private static int[] FromSaveNumbers(SaveIdList ids, Func<string, int> current)
        {
            int[] numbers = new int[ids.Ids.Count + 1];
            for (int i = 0; i < ids.Ids.Count; i++)
            {
                numbers[i + 1] = current(ids.Ids[i]);
            }

            return numbers;
        }

        /// <summary>Чанк: координаты внутри региона и мира, слои, объекты, если они есть в формате, и сущности.</summary>
        private void ReadChunk(PacketReader reader, (int X, int Y) region, byte format)
        {
            int chunkX = reader.ReadCount(World.MaxSize);
            int chunkY = reader.ReadCount(World.MaxSize);
            bool inside = chunkX / World.RegionSize == region.X && chunkY / World.RegionSize == region.Y;
            if (!inside || chunkX >= world.ChunksX || chunkY >= world.ChunksY)
            {
                throw new ProtocolException($"chunk {chunkX},{chunkY} does not belong to this region");
            }

            int levels = reader.ReadCount(World.LevelCount);
            for (int i = 0; i < levels; i++)
            {
                ReadLevel(ChunkCodec.Read(reader), chunkX, chunkY);
            }

            int objects = format >= FormatWithObjects ? reader.ReadCount(MaxObjects) : 0;
            for (int i = 0; i < objects; i++)
            {
                ReadObject(reader, chunkX, chunkY, format);
            }

            int entities = reader.ReadCount(MaxEntities);
            for (int i = 0; i < entities; i++)
            {
                Entities.Add(EntityCodec.Read(reader, ids, tables, Missing, log, format));
            }
        }

        /// <summary>Слой этажа в мир с переводом номеров полов и стен.</summary>
        private void ReadLevel(ChunkLevelData data, int chunkX, int chunkY)
        {
            if (data.Z is < World.MinLevel or > World.MaxLevel)
            {
                throw new ProtocolException($"level {data.Z} is outside the world");
            }

            ChunkLevel level = world.LoadChunkLevel(chunkX, chunkY, data.Z);
            for (int i = 0; i < data.Floors.Length; i++)
            {
                level.Floors[i] = Translate(data.Floors[i], _floors, ids.Floors);
                level.NorthWalls[i] = Translate(data.NorthWalls[i], _walls, ids.Walls);
                level.WestWalls[i] = Translate(data.WestWalls[i], _walls, ids.Walls);
                level.Flags[i] = data.Flags[i] <= (ChunkLevel.NorthOpen | ChunkLevel.WestOpen)
                    ? (byte)data.Flags[i]
                    : throw new ProtocolException($"edge flags {data.Flags[i]} are not valid");
            }
        }

        /// <summary>Объект чанка: id, номер вида, клетка внутри чанка, поворот и с формата 4 - предметы контейнера.</summary>
        private void ReadObject(PacketReader reader, int chunkX, int chunkY, byte format)
        {
            long id = reader.ReadVarInt();
            ushort number = (ushort)Math.Min(reader.ReadVarUInt(), ushort.MaxValue);
            CellPos cell = new((int)reader.ReadVarInt(), (int)reader.ReadVarInt(), (int)reader.ReadVarInt());
            int rotation = reader.ReadByte();
            bool inside = world.Contains(cell) && cell.X / World.ChunkSize == chunkX && cell.Y / World.ChunkSize == chunkY;
            if (!inside || rotation >= WorldObject.Rotations || world.FindObject(id) is not null)
            {
                throw new ProtocolException($"object {id} at {cell} with rotation {rotation} is not valid");
            }

            ushort def = Translate(number, _objects, ids.Objects);
            if (def != 0)
            {
                world.AddObject(WorldObject.Create(id, def, tables.World.Object(def), cell, rotation));
            }

            int items = format >= FormatWithContainers ? reader.ReadCount(MaxItems) : 0;
            for (int i = 0; i < items; i++)
            {
                ReadContainerItem(reader, id);
            }
        }

        /// <summary>Предмет контейнера объекта objectId; предмет без мода попадает в Missing, контейнер, переставший им быть, теряет предмет с записью в лог.</summary>
        private void ReadContainerItem(PacketReader reader, long objectId)
        {
            long itemId = reader.ReadVarInt();
            string item = ids.Items.IdAt(reader.ReadCount(MaxItems));
            if (!tables.Items.TryItemIndex(item, out int index))
            {
                Missing.Add(item);
            }
            else if (world.Container(objectId) is Inventory container)
            {
                container.Add(new Item(itemId, tables.Items.Item(index)));
            }
            else
            {
                log.Info($"save: dropped {item} {itemId}: object {objectId} is not a container any more");
            }
        }

        /// <summary>Номер мода для номера сохранения; id без мода записывается в Missing.</summary>
        private ushort Translate(ushort number, int[] map, SaveIdList ids)
        {
            if (number == 0)
            {
                return 0;
            }

            if (number >= map.Length)
            {
                throw new ProtocolException($"id number {number} is not in the save id table");
            }

            if (map[number] < 0)
            {
                Missing.Add(ids.IdAt(number));
                return 0;
            }

            return (ushort)map[number];
        }
    }
}
