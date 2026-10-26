using System;
using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Mods;

namespace DevAInt.Sim.Net;

/// <summary>Предмет в сообщении: id экземпляра и номер определения.</summary>
public readonly record struct ItemData(long Id, int Item);

/// <summary>Сообщение сервера клиенту.</summary>
public abstract record ServerMessage;

/// <summary>Подключение принято: своя сущность, размер мира, ускорение времени и моды сервера в порядке загрузки.</summary>
public sealed record WelcomeMessage(long EntityId, int Width, int Height, double TimeScale, IReadOnlyList<ModInfo> Mods) : ServerMessage
{
    /// <summary>Сравнение с модами по значению, а не по ссылке на список.</summary>
    public bool Equals(WelcomeMessage? other)
    {
        return other is not null && (EntityId, Width, Height, TimeScale) == (other.EntityId, other.Width, other.Height, other.TimeScale)
            && Mods.SequenceEqual(other.Mods);
    }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(EntityId, Width, Height, TimeScale, Mods.Count);
}

/// <summary>Снимок чанка: все созданные слои этажей целиком.</summary>
public sealed record ChunkSnapshotMessage(int ChunkX, int ChunkY, IReadOnlyList<ChunkLevelData> Levels, IReadOnlyList<ObjectData> Objects) : ServerMessage;

/// <summary>Объект поставлен в чанке, который клиент уже знает.</summary>
public sealed record ObjectPlaceMessage(ObjectData Object) : ServerMessage;

/// <summary>Объект убран.</summary>
public sealed record ObjectRemoveMessage(long Id) : ServerMessage;

/// <summary>Чанк вышел из зоны клиента, клиент забывает его вместе с сущностями в нём.</summary>
public sealed record ChunkUnloadMessage(int ChunkX, int ChunkY) : ServerMessage;

/// <summary>Сущность появилась у клиента: позиция, номер вида из CreatureTable или -1 и уровни всех мудлов.</summary>
public sealed record EntitySpawnMessage(long Id, float X, float Y, int Z, int Creature, IReadOnlyList<int> MoodleLevels) : ServerMessage;

/// <summary>Удар или толчок прошёл: кто, кого и как, для выпада и вспышки на клиенте.</summary>
public sealed record AttackMessage(long Attacker, long Target, AttackKind Kind = AttackKind.Strike) : ServerMessage;

/// <summary>Своя сущность сменилась: прежняя умерла, у игрока новый персонаж.</summary>
public sealed record OwnEntityMessage(long EntityId) : ServerMessage;

/// <summary>Сущность сдвинулась.</summary>
public sealed record EntityMoveMessage(long Id, float X, float Y, int Z) : ServerMessage;

/// <summary>Уровень мудла сущности сменился.</summary>
public sealed record MoodleLevelMessage(long Id, int Moodle, int Level) : ServerMessage;

/// <summary>Сущность исчезла у клиента.</summary>
public sealed record EntityRemoveMessage(long Id) : ServerMessage;

/// <summary>Значения всех статов своей сущности.</summary>
public sealed record OwnStatsMessage(IReadOnlyList<float> Values) : ServerMessage;

/// <summary>Пол клетки изменился.</summary>
public sealed record FloorChangeMessage(CellPos Cell, ushort Floor) : ServerMessage;

/// <summary>Северное или западное ребро клетки-владельца изменилось.</summary>
public sealed record EdgeChangeMessage(CellPos Owner, bool North, Edge Edge) : ServerMessage;

/// <summary>Свой инвентарь целиком: id предметов и номера их определений из ItemTable.</summary>
public sealed record OwnInventoryMessage(IReadOnlyList<ItemData> Items) : ServerMessage;

/// <summary>Содержимое контейнера объекта, на который подписан клиент: при подписке и при каждом изменении.</summary>
public sealed record ContainerMessage(long Object, IReadOnlyList<ItemData> Items) : ServerMessage
{
    /// <inheritdoc/>
    public bool Equals(ContainerMessage? other) => other is not null && Object == other.Object && Items.SequenceEqual(other.Items);

    /// <inheritdoc/>
    public override int GetHashCode() => Object.GetHashCode();
}

/// <summary>Контейнер больше не виден клиенту: отошёл, отписался или объект убран.</summary>
public sealed record ContainerCloseMessage(long Object) : ServerMessage;

/// <summary>Сущность начала действие: номер действия из ItemTable, тик начала, длительность в тиках и подпись - id что делается и над чем.</summary>
public sealed record ActionStartedMessage(long Entity, int Action, long StartTick, int DurationTicks, string Verb = "", string Subject = "") : ServerMessage;

/// <summary>Действие сущности закончилось: выполнено или прервано.</summary>
public sealed record ActionEndedMessage(long Entity, bool Completed) : ServerMessage;

/// <summary>Пакет сервера за тик: номер тика, сообщения по порядку и номер последнего применённого ввода клиента.</summary>
public sealed record ServerFrame(long Tick, IReadOnlyList<ServerMessage> Messages, uint LastInput = 0);

/// <summary>Сообщение клиента серверу.</summary>
public abstract record ClientMessage;

/// <summary>Первое сообщение клиента: кто играет; по id сервер возвращает сохранённого персонажа.</summary>
public sealed record HelloMessage(string PlayerId) : ClientMessage;

/// <summary>Ввод движения: номер по порядку, направление, точка под курсором на земле и смотреть ли на неё без прицела.</summary>
public sealed record InputMessage(uint Sequence, float X, float Y, float AimX = 0, float AimY = 0, bool FaceAim = false) : ClientMessage;

/// <summary>Действие управления нажато или отпущено: номер из ControlTable.</summary>
public sealed record ControlMessage(int Control, bool Pressed) : ClientMessage;

/// <summary>Просьба начать действие предметом: id предмета и номер действия из ItemTable.</summary>
public sealed record StartActionMessage(long ItemId, int Action) : ClientMessage;

/// <summary>Контейнеры, содержимое которых клиент хочет видеть: ближние, пока открыто окно инвентаря; пустой список - отписка от всех.</summary>
public sealed record WatchContainersMessage(IReadOnlyList<long> Objects) : ClientMessage
{
    /// <inheritdoc/>
    public bool Equals(WatchContainersMessage? other) => other is not null && Objects.SequenceEqual(other.Objects);

    /// <inheritdoc/>
    public override int GetHashCode() => Objects.Count;
}

/// <summary>Просьба построить или разобрать: цель постройки.</summary>
public sealed record BuildMessage(BuildTarget Target) : ClientMessage;

/// <summary>Просьба начать крафт по рецепту: номер рецепта из ItemTable.</summary>
public sealed record StartCraftMessage(int Recipe) : ClientMessage;

/// <summary>Просьба переложить предмет: откуда и куда, 0 - свой инвентарь, иначе id объекта-контейнера.</summary>
public sealed record MoveItemMessage(long Item, long From, long To) : ClientMessage;

/// <summary>Просьба прервать своё действие.</summary>
public sealed record CancelActionMessage : ClientMessage;

/// <summary>Объект в снимке чанка и в сообщении о постановке: id, номер вида в WorldTable, северо-западная клетка и поворот.</summary>
public sealed record ObjectData(long Id, ushort Def, CellPos Cell, int Rotation)
{
    /// <summary>Данные объекта мира.</summary>
    public static ObjectData From(WorldObject obj) => new(obj.Id, obj.Def, obj.Cell, obj.Rotation);
}

/// <summary>Один слой этажа чанка в снимке.</summary>
public sealed record ChunkLevelData(int Z, ushort[] Floors, ushort[] NorthWalls, ushort[] WestWalls, ushort[] Flags);
