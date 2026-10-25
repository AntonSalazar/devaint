using System;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Creatures;

/// <summary>Видимость своего персонажа на клиенте: пересчёт, только когда сдвинулись глаза, взгляд или стены, и память увиденного.</summary>
public sealed class VisionTracker
{
    /// <summary>Поворот взгляда в градусах, после которого видимость считается заново.</summary>
    public const double TurnDegrees = 3;

    /// <summary>Сдвиг глаз в метрах, после которого видимость считается заново: мельче клетки, чтобы память не шла ступеньками.</summary>
    public const double MoveMeters = 0.25;

    /// <summary>Косинус порога поворота.</summary>
    private static readonly double _turnCos = Math.Cos(TurnDegrees * Math.PI / 180);

    /// <summary>Глаза прошлого расчёта, null - расчёта ещё не было или стены изменились.</summary>
    private WorldPos? _eye;

    /// <summary>Взгляд прошлого расчёта.</summary>
    private (double X, double Y) _facing;

    /// <summary>Видимость сейчас.</summary>
    public VisibilityMap Map { get; } = new();

    /// <summary>Что персонаж уже видел.</summary>
    public SeenCells Seen { get; } = new();

    /// <summary>Пересчёт по глазам eye, взгляду facing и обзору vision, если что-то сменилось; true - видимость пересчитана.</summary>
    public bool Update(World world, WorldPos eye, (double X, double Y) facing, VisionDef vision)
    {
        bool turned = (facing.X * _facing.X) + (facing.Y * _facing.Y) < _turnCos;
        if (_eye is WorldPos last && last.Z == eye.Z && !turned && Moved(last, eye) < MoveMeters)
        {
            return false;
        }

        Vision.Compute(world, eye, facing, vision, Map);
        Seen.Mark(world, eye, facing, vision, Map);
        (_eye, _facing) = (eye, facing);
        return true;
    }

    /// <summary>Стены или двери изменились: следующий Update посчитает заново.</summary>
    public void Invalidate() => _eye = null;

    /// <summary>Расстояние между глазами по полу.</summary>
    private static double Moved(WorldPos from, WorldPos to) => Math.Sqrt(((to.X - from.X) * (to.X - from.X)) + ((to.Y - from.Y) * (to.Y - from.Y)));
}
