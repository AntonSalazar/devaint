using System;
using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Net;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim.Saves;

/// <summary>Сущность в файле сохранения: позиция, значения статов, инвентарь и идущее действие.</summary>
internal static class EntityCodec
{
    /// <summary>Наибольшая длина id игрока в байтах.</summary>
    private const int MaxPlayerId = 64;

    /// <summary>Наибольшее число статов или предметов у сущности, защита от мусора.</summary>
    private const int MaxList = 65_536;

    /// <summary>Первый формат, где у действия записан его вид: применение предмета или рецепт.</summary>
    private const byte FormatWithActionKind = 5;

    /// <summary>Вид действия в файле: применение предмета.</summary>
    private const byte UseAction = 0;

    /// <summary>Вид действия в файле: крафт по рецепту.</summary>
    private const byte RecipeAction = 1;

    /// <summary>Вид действия в файле: постройка или разбор.</summary>
    private const byte BuildAction = 2;

    /// <summary>Запись сущности с номерами из таблицы id сохранения; world - для видов построек.</summary>
    public static void Write(PacketWriter writer, Entity entity, SaveIds ids, StatTable stats, WorldTable world)
    {
        writer.WriteVarInt(entity.Id);
        writer.WriteVarUInt(entity.Creature is null ? 0 : (ulong)ids.Creatures.NumberOf(entity.Creature.Id));
        writer.WriteBool(entity.PlayerId is not null);
        if (entity.PlayerId is string playerId)
        {
            writer.WriteString(playerId);
        }

        // Позиция в double, как в sim: после загрузки движение продолжается без сдвига
        writer.WriteDouble(entity.Position.X);
        writer.WriteDouble(entity.Position.Y);
        writer.WriteVarInt(entity.Position.Z);

        // Только значения: мудлы и модификаторы следуют из них и из определений текущих модов
        writer.WriteVarUInt((ulong)stats.StatCount);
        for (int i = 0; i < stats.StatCount; i++)
        {
            writer.WriteVarUInt((ulong)ids.Stats.NumberOf(stats.Stat(i).Id));
            writer.WriteDouble(entity.Stats.Value(i));
        }

        writer.WriteVarUInt((ulong)entity.Inventory.Items.Count);
        foreach (Item item in entity.Inventory.Items)
        {
            writer.WriteVarInt(item.Id);
            writer.WriteVarUInt((ulong)ids.Items.NumberOf(item.Def.Id));
        }

        writer.WriteBool(entity.Action is not null);
        if (entity.Action is ActionState action)
        {
            writer.WriteVarUInt((ulong)ids.Actions.NumberOf(action.Action.Id));
            writer.WriteVarInt(action.ItemId);
            writer.WriteVarInt(action.StartTick);
            writer.WriteVarUInt((ulong)action.DurationTicks);
            if (action.Recipe is RecipeDef recipe)
            {
                writer.WriteByte(RecipeAction);
                writer.WriteVarUInt((ulong)ids.Recipes.NumberOf(recipe.Id));
            }
            else if (action.Build is BuildTarget build)
            {
                writer.WriteByte(BuildAction);
                WriteBuild(writer, build, ids, world);
            }
            else
            {
                writer.WriteByte(UseAction);
            }
        }
    }

    /// <summary>Сущность из файла формата format; неизвестные предметы попадают в missing, неизвестные статы и действия выбрасываются с записью в лог.</summary>
    public static Entity Read(PacketReader reader, SaveIds ids, SaveTables tables, ISet<string> missing, ISimLog log, byte format)
    {
        long id = reader.ReadVarInt();
        CreatureDef? creature = ReadCreature(reader, ids, tables, missing);
        string? playerId = reader.ReadBool() ? reader.ReadString(MaxPlayerId) : null;
        double x = reader.ReadDouble();
        double y = reader.ReadDouble();
        int z = (int)reader.ReadVarInt();
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new SaveException($"entity {id} has a broken position");
        }

        List<(int Stat, double Value)> values = [];
        int statCount = reader.ReadCount(MaxList);
        for (int i = 0; i < statCount; i++)
        {
            string stat = ids.Stats.IdAt(reader.ReadCount(MaxList));
            double value = reader.ReadDouble();
            if (tables.Stats.TryStatIndex(stat, out int index))
            {
                values.Add((index, value));
            }
            else
            {
                log.Info($"save: dropped unknown stat {stat} of entity {id}");
            }
        }

        Entity entity = new(id, new StatSet(tables.Stats, creature), new WorldPos(x, y, z), creature)
        {
            PlayerId = playerId,
            Brain = creature?.Brain is null ? null : new BrainState(),
        };
        entity.Stats.Restore(values);
        int itemCount = reader.ReadCount(MaxList);
        for (int i = 0; i < itemCount; i++)
        {
            long itemId = reader.ReadVarInt();
            string item = ids.Items.IdAt(reader.ReadCount(MaxList));
            if (tables.Items.TryItemIndex(item, out int index))
            {
                entity.Inventory.Add(new Item(itemId, tables.Items.Item(index)));
            }
            else
            {
                missing.Add(item);
            }
        }

        if (reader.ReadBool())
        {
            entity.Action = ReadAction(reader, ids, tables, entity, log, format);
        }

        return entity;
    }

    /// <summary>Вид существа или null без вида; вид без мода попадает в missing.</summary>
    private static CreatureDef? ReadCreature(PacketReader reader, SaveIds ids, SaveTables tables, ISet<string> missing)
    {
        int number = reader.ReadCount(MaxList);
        if (number == 0)
        {
            return null;
        }

        string creature = ids.Creatures.IdAt(number);
        if (tables.Creatures.TryIndex(creature, out int index))
        {
            return tables.Creatures.Creature(index);
        }

        missing.Add(creature);
        return null;
    }

    /// <summary>Цель постройки: вид, номер вида по таблице id сохранения, клетка и поворот.</summary>
    private static void WriteBuild(PacketWriter writer, BuildTarget build, SaveIds ids, WorldTable world)
    {
        writer.WriteByte((byte)build.Kind);
        int number = build.Kind switch
        {
            BuildKind.Object => ids.Objects.NumberOf(world.Object(build.Def).Id),
            BuildKind.Wall => ids.Walls.NumberOf(world.Wall(build.Def).Id),
            BuildKind.Fill => ids.Fills.NumberOf(world.Fill(build.Def).Id),
            _ => 0,
        };
        writer.WriteVarUInt((ulong)number);
        writer.WriteVarInt(build.Cell.X);
        writer.WriteVarInt(build.Cell.Y);
        writer.WriteVarInt(build.Cell.Z);
        writer.WriteByte((byte)build.Turn);
    }

    /// <summary>Цель постройки из файла или null, если вида постройки нет в текущих модах.</summary>
    private static BuildTarget? ReadBuild(PacketReader reader, SaveIds ids, SaveTables tables)
    {
        byte kind = reader.ReadByte();
        int number = reader.ReadCount(MaxList);
        CellPos cell = new((int)reader.ReadVarInt(), (int)reader.ReadVarInt(), (int)reader.ReadVarInt());
        int turn = reader.ReadByte();
        if (!Enum.IsDefined((BuildKind)kind))
        {
            throw new ProtocolException($"build kind {kind} is unknown");
        }

        ushort def = 0;
        bool known = (BuildKind)kind switch
        {
            BuildKind.Object => tables.World.TryObjectIndex(ids.Objects.IdAt(number), out def),
            BuildKind.Wall => tables.World.TryWallIndex(ids.Walls.IdAt(number), out def),
            BuildKind.Fill => tables.World.TryFillIndex(ids.Fills.IdAt(number), out def),
            _ => true,
        };
        return known ? new BuildTarget((BuildKind)kind, def, cell, turn) : null;
    }

    /// <summary>Идущее действие или null, если его действие, предмет или рецепт пропали из модов.</summary>
    private static ActionState? ReadAction(PacketReader reader, SaveIds ids, SaveTables tables, Entity entity, ISimLog log, byte format)
    {
        string action = ids.Actions.IdAt(reader.ReadCount(MaxList));
        long itemId = reader.ReadVarInt();
        long startTick = reader.ReadVarInt();
        int durationTicks = reader.ReadCount(int.MaxValue);
        byte kind = format >= FormatWithActionKind ? reader.ReadByte() : UseAction;
        if (kind == RecipeAction)
        {
            string recipe = ids.Recipes.IdAt(reader.ReadCount(MaxList));
            if (!tables.Items.TryActionIndex(action, out int actionIndex) || !tables.Items.TryRecipeIndex(recipe, out int recipeIndex))
            {
                log.Info($"save: dropped recipe {recipe} of entity {entity.Id}");
                return null;
            }

            return new ActionState(tables.Items.Action(actionIndex), null, 0, startTick, durationTicks) { Recipe = tables.Items.Recipe(recipeIndex) };
        }

        if (kind == BuildAction)
        {
            BuildTarget? build = ReadBuild(reader, ids, tables);
            if (build is null || !tables.Items.TryActionIndex(action, out int buildIndex))
            {
                log.Info($"save: dropped build of entity {entity.Id}");
                return null;
            }

            return new ActionState(tables.Items.Action(buildIndex), null, 0, startTick, durationTicks) { Build = build };
        }

        if (kind != UseAction)
        {
            throw new ProtocolException($"action kind {kind} is unknown");
        }

        // Применение берётся из определения предмета текущих модов: длительность уже посчитана в тиках
        ItemUseDef? use = entity.Inventory.Find(itemId)?.Def.Uses.FirstOrDefault(candidate => candidate.Action == action);
        if (!tables.Items.TryActionIndex(action, out int index) || use is null)
        {
            log.Info($"save: dropped action {action} of entity {entity.Id}");
            return null;
        }

        return new ActionState(tables.Items.Action(index), use, itemId, startTick, durationTicks);
    }
}

/// <summary>Таблицы текущих модов, по которым номера сохранения переводятся в определения.</summary>
internal sealed record SaveTables(StatTable Stats, ItemTable Items, WorldTable World, CreatureTable Creatures);
