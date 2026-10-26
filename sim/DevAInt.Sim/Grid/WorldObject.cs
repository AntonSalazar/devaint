using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Grid;

/// <summary>Объект мира на клетках: свой id, номер вида, северо-западная клетка, поворот, занятый прямоугольник и вместимость контейнера, 0 - не контейнер.</summary>
public sealed record WorldObject(long Id, ushort Def, CellPos Cell, int Rotation, int Width, int Depth, bool BlocksMove, bool BlocksSight, double Capacity = 0)
{
    /// <summary>Число поворотов: на 0, 90, 180 и 270 градусов.</summary>
    public const int Rotations = 4;

    /// <summary>Можно ли в объект класть предметы.</summary>
    public bool IsContainer => Capacity > 0;

    /// <summary>Объект вида def с номером index в клетке cell с поворотом rotation; поворот на 90 градусов меняет ширину и глубину местами.</summary>
    public static WorldObject Create(long id, ushort index, ObjectDef def, CellPos cell, int rotation)
    {
        bool turned = rotation % 2 == 1;
        return new WorldObject(id, index, cell, rotation, turned ? def.H : def.W, turned ? def.W : def.H, def.BlocksMove, def.BlocksSight, def.Container?.Capacity ?? 0);
    }

    /// <summary>Занимает ли объект клетку.</summary>
    public bool Covers(CellPos cell) => cell.Z == Cell.Z && cell.X >= Cell.X && cell.Y >= Cell.Y && cell.X < Cell.X + Width && cell.Y < Cell.Y + Depth;
}
