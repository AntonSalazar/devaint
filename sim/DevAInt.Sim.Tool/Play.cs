using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DevAInt.Sim.Data;

namespace DevAInt.Sim.Tool;


/// <summary>
/// Забег в терминале: ASCII-карта, команды текстом, лог событий.
/// Первый плейтест правил без картинки (<c>06-PLAN.md</c>, уикенд 1).
/// </summary>
public static class Play
{
    /// <summary>
    /// Метод запуска забега.
    /// </summary>
    /// <param name="dataDir">Папка с JSON-данными.</param>
    /// <param name="seed">Сид.</param>
    /// <param name="mutators">Id мутаторов.</param>
    /// <returns>Код выхода.</returns>
    public static int Run(string dataDir, int seed, IReadOnlyList<string> mutators)
    {
        Rules rules = Rules.Load(dataDir);
        RunState state = Sim.NewRun(rules, seed, mutators);
        Console.WriteLine($"DevAInt — seed {seed}, mutators: {(mutators.Count == 0 ? "none" : string.Join(", ", mutators))}");
        Console.WriteLine("Type ? for help.");

        while (state.Outcome == Outcome.Running)
        {
            Console.WriteLine();
            Console.Write(Render(rules, state));
            Console.Write("> ");
            string? line = Console.ReadLine();
            if (line is null || line.Trim() == "q")
            {
                Console.WriteLine("bye");
                return 0;
            }

            Action? action = Parse(rules, state, line.Trim());
            if (action is null)
            {
                continue;
            }

            Verdict verdict = Sim.Check(rules, state, action);
            if (!verdict.Ok)
            {
                Console.WriteLine($"  can't: {verdict.Reason}");
                continue;
            }

            foreach (SimEvent e in Sim.Apply(rules, state, action))
            {
                Console.WriteLine($"  [T{e.Turn}] {Describe(rules, state, e)}");
            }
        }

        Console.WriteLine();
        Console.Write(Render(rules, state));
        Console.WriteLine(state.Outcome switch
        {
            Outcome.Won => $"*** DATACENTER CAPTURED on turn {state.Turn}. Score {state.Score}. ***",
            Outcome.LostAudit => $"*** WIPED BY AUDIT on turn {state.Turn}. Score {state.Score}. ***",
            Outcome.LostDawn => $"*** DAWN. Score {state.Score}. ***",
            Outcome.Running => "still running?",
            _ => "???",
        });
        return 0;
    }


    /// <summary>
    /// Метод разбора команды в действие. Печатает подсказку и возвращает null, если команда не действие.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="line">Строка команды.</param>
    /// <returns>Вернет действие или null.</returns>
    private static Action? Parse(Rules rules, RunState state, string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        try
        {
            switch (parts[0])
            {
                case "m":
                    return new Move(Hex.FromOffset(int.Parse(parts[1]), int.Parse(parts[2])));
                case "x":
                    return new Exploit(int.Parse(parts[1]), Hex.FromOffset(int.Parse(parts[2]), int.Parse(parts[3])));
                case "s":
                    return new Scan();
                case "l":
                    return new Lurk();
                case "c":
                    int slot = parts.Length > 2 ? int.Parse(parts[2]) : state.Daemon.Exploits.Count;
                    return new Compile(parts[1], slot);
                case "e":
                    return new EndTurn();
                case "n":
                    Console.Write(Neighbors(rules, state));
                    return null;
                case "?":
                    Console.Write(Help(rules));
                    return null;
                default:
                    Console.WriteLine("  unknown command, ? for help");
                    return null;
            }
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException)
        {
            Console.WriteLine("  bad arguments, ? for help");
            return null;
        }
    }


    /// <summary>
    /// Метод отрисовки экрана: карта, строка состояния, слоты, соседи.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <returns>Вернет текст.</returns>
    private static string Render(Rules rules, RunState state)
    {
        World world = state.World;
        Layer<bool> known = world.KnownBy[Sim.Player];
        StringBuilder sb = new();

        // Шапка колонок в две строки: десятки (пусто для 0..9) и единицы. Колонка 18 читается как "1" над "8".
        sb.Append("    ");
        for (int col = 0; col < world.Width; col++)
        {
            sb.Append(col >= 10 ? (char)('0' + (col / 10)) : ' ').Append(' ');
        }
        sb.Append('\n');
        sb.Append("    ");
        for (int col = 0; col < world.Width; col++)
        {
            sb.Append(col % 10).Append(' ');
        }
        sb.Append('\n');

        for (int row = 0; row < world.Height; row++)
        {
            sb.Append($"{row,2} ");
            sb.Append((row & 1) == 1 ? ' ' : string.Empty);
            for (int col = 0; col < world.Width; col++)
            {
                Hex hex = Hex.FromOffset(col, row);
                sb.Append(Glyph(state, known, hex)).Append(' ');
            }
            sb.Append('\n');
        }

        int owned = world.Nodes().Count(h => world.Owner[h] == Sim.Player);
        sb.Append($"turn {state.Turn}  dawn in {rules.Core.DawnTurn - state.Turn}  compute {state.Compute} (+{Sim.Income(rules, state)})  nodes {owned}\n");
        sb.Append($"daemon at {world.Name[state.Daemon.Pos]} {state.Daemon.Pos.ToOffset()}  AP {state.Daemon.ActionPoints}/{state.Stats.ActionPoints}  noise {world.Noise[state.Daemon.Pos]:0.00}\n");
        for (int i = 0; i < state.Daemon.Exploits.Count; i++)
        {
            ExploitSlot slot = state.Daemon.Exploits[i];
            ExploitDef def = rules.Exploits[slot.DefId];
            int power = Sim.Power(state, def, def.TargetOs);
            sb.Append($"  slot {i}: {def.Name} ({def.TargetOs} T{def.Power}, power {power}) charges {slot.Charges}/{Sim.MaxCharges(rules, state.Stats, def.Id)}\n");
        }

        sb.Append(Neighbors(rules, state));
        return sb.ToString();
    }


    /// <summary>
    /// Метод символа гекса: туман, пусто, свой (заглавная), нейтральный (строчная), daemon, цель.
    /// </summary>
    /// <param name="state">Состояние забега.</param>
    /// <param name="known">Слой тумана.</param>
    /// <param name="hex">Гекс.</param>
    /// <returns>Вернет символ.</returns>
    private static char Glyph(RunState state, Layer<bool> known, Hex hex)
    {
        World world = state.World;
        if (hex == state.Daemon.Pos)
        {
            return '@';
        }
        if (hex == state.Target)
        {
            return '$';
        }
        if (!known[hex])
        {
            return '~';
        }
        if (!world.IsNode(hex))
        {
            return '.';
        }

        char glyph = world.Type[hex] switch
        {
            NodeType.Empty => '.',
            NodeType.Home => 'h',
            NodeType.Workstation => 'w',
            NodeType.Server => 's',
            NodeType.Router => 'r',
            NodeType.IoT => 'i',
            NodeType.Controller => 'c',
            _ => '?',
        };
        return world.Owner[hex] == Sim.Player ? char.ToUpperInvariant(glyph) : glyph;
    }


    /// <summary>
    /// Метод списка соседей daemon'а с разбором захвата по каждому слоту.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <returns>Вернет текст.</returns>
    private static string Neighbors(Rules rules, RunState state)
    {
        World world = state.World;
        StringBuilder sb = new();
        sb.Append("neighbors:\n");
        foreach (Hex hex in Sim.Neighbors(state, state.Daemon.Pos))
        {
            if (!world.IsNode(hex))
            {
                continue;
            }

            (int col, int row) = hex.ToOffset();
            if (!world.KnownBy[Sim.Player][hex])
            {
                sb.Append($"  ({col},{row}) ???\n");
                continue;
            }

            string owner = world.Owner[hex] == Sim.Player ? "yours" : "neutral";
            sb.Append($"  ({col},{row}) {world.Type[hex]} {world.Os[hex]} patch {world.Patch[hex]} noise {world.Noise[hex]:0.00} {owner}");
            if (world.Owner[hex] != Sim.Player)
            {
                for (int i = 0; i < state.Daemon.Exploits.Count; i++)
                {
                    CaptureCheck check = Sim.CanCapture(rules, state, i, hex);
                    if (check.OsMatch)
                    {
                        sb.Append(check.Possible ? $"  [x {i}: OK]" : $"  [x {i}: {check.Reason}]");
                    }
                }
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }


    /// <summary>
    /// Метод текста события для лога.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="e">Событие.</param>
    /// <returns>Вернет текст.</returns>
    private static string Describe(Rules rules, RunState state, SimEvent e) => e switch
    {
        NodeCaptured c => $"captured {state.World.Name[c.Node]} {c.Node.ToOffset()}",
        NodeAudited a => $"AUDIT wiped {state.World.Name[a.Node]} {a.Node.ToOffset()}",
        ExploitCompiled c => $"compiled {rules.Exploits[c.DefId].Name}",
        ExploitBurned b => $"BURNOUT: {rules.Exploits[b.DefId].Name} power → {b.NewPower}",
        WorldEventFired w => w.Text,
        TurnStarted t => $"turn {t.Turn}, income +{t.Income}",
        RunEnded r => $"run over: {r.Outcome}, score {r.Score}",
        _ => e.ToString(),
    };


    /// <summary>
    /// Метод текста подсказки с ценами компиляции.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <returns>Вернет текст.</returns>
    private static string Help(Rules rules)
    {
        StringBuilder sb = new();
        sb.Append("  m COL ROW        move to your node\n");
        sb.Append("  x SLOT COL ROW   exploit a neighbor with slot\n");
        sb.Append("  s                scan\n");
        sb.Append("  l                lurk (noise → 0)\n");
        sb.Append("  c ID [SLOT]      compile on your server (into new slot, or replace SLOT)\n");
        sb.Append("  e                end turn\n");
        sb.Append("  n                list neighbors\n");
        sb.Append("  q                quit\n");
        sb.Append("  map: @ daemon  $ target  ~ fog  . empty  UPPER yours  lower neutral  h/w/s/r/i/c node types\n");
        sb.Append("  compile:");
        foreach (ExploitDef def in rules.Exploits.Values.Where(d => d.Rarity == Rarity.Common).OrderBy(d => d.TargetOs).ThenBy(d => d.Power))
        {
            sb.Append($" {def.Id}={def.ComputeCost}");
        }
        sb.Append('\n');
        return sb.ToString();
    }
}
