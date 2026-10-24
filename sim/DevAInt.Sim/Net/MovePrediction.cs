using System.Collections.Generic;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;

namespace DevAInt.Sim.Net;

/// <summary>Предсказание движения своей сущности на клиенте с исправлением по ответам сервера.</summary>
public sealed class MovePrediction
{
    /// <summary>Отправленные вводы, которые сервер ещё не подтвердил, по возрастанию номера.</summary>
    private readonly List<(uint Sequence, MoveInput Input)> _pending = [];

    /// <summary>Предсказанная позиция, null пока своя сущность неизвестна.</summary>
    public WorldPos? Position { get; private set; }

    /// <summary>Число неподтверждённых вводов.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Отправленный ввод сразу двигает предсказанную позицию тела body; без позиции ввод не запоминается.</summary>
    public void Add(uint sequence, MoveInput input, World world, double secondsPerTick, MoveParams body)
    {
        if (Position is not WorldPos position)
        {
            return;
        }

        _pending.Add((sequence, input));
        Position = Movement.Step(world, position, input, secondsPerTick, body);
    }

    /// <summary>Сброс: своя сущность сменилась, прежние вводы к новой не относятся.</summary>
    public void Reset()
    {
        _pending.Clear();
        Position = null;
    }

    /// <summary>Позиция сервера после ввода lastInput плюс заново прогнанные неподтверждённые вводы тела body.</summary>
    public void Reconcile(uint lastInput, WorldPos server, World world, double secondsPerTick, MoveParams body)
    {
        // Подтверждённые вводы сервер уже учёл в своей позиции, их больше не прогоняем
        int confirmed = 0;
        while (confirmed < _pending.Count && _pending[confirmed].Sequence <= lastInput)
        {
            confirmed++;
        }

        _pending.RemoveRange(0, confirmed);

        // Если сервер посчитал так же, результат совпадёт с прежним предсказанием, иначе позиция поправится
        WorldPos position = server;
        foreach ((uint _, MoveInput input) in _pending)
        {
            position = Movement.Step(world, position, input, secondsPerTick, body);
        }

        Position = position;
    }
}
