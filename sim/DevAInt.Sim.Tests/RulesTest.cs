using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using DevAInt.Sim.Data;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты <see cref="Rules"/>: загрузка реальных данных из `project/data`, полнота
/// таблиц, инварианты баланса, внятные ошибки на испорченных данных.
/// </summary>
public class RulesTest
{
    /// <summary>Загруженные один раз реальные данные.</summary>
    private static readonly Rules _real = Rules.Load(DataDir.Path);

    /// <summary>Общие числа читаются.</summary>
    [Fact]
    public void CoreValuesLoaded()
    {
        Assert.Equal(8, _real.Core.MaxFactions);
        Assert.Equal(3, _real.Core.ActionPoints);
        Assert.Equal(3, _real.Core.MutatorBudget);
        Assert.Equal(0.8f, _real.Core.NoiseDecay);
        Assert.True(_real.Core.SharedVision);
    }

    /// <summary>Типы узлов: все, кроме Empty; веса ОС в сумме дают 1.</summary>
    [Fact]
    public void NodeTypesCompleteAndWeightsSumToOne()
    {
        foreach (NodeType type in Enum.GetValues<NodeType>())
        {
            if (type == NodeType.Empty)
            {
                Assert.False(_real.NodeTypes.ContainsKey(type), "Empty is not a node type definition");
                continue;
            }
            Assert.True(_real.NodeTypes.ContainsKey(type), $"node type {type} is defined");
            float sum = _real.NodeTypes[type].OsWeights.Values.Sum();
            Assert.InRange(sum, 0.999f, 1.001f);
        }
    }

    /// <summary>Эксплойты: на каждую ОС и тир ровно один Common, кроме Forge T1–T2.</summary>
    [Fact]
    public void ExploitsCoverEveryOsAndTier()
    {
        foreach (Os os in Enum.GetValues<Os>())
        {
            for (int tier = 1; tier <= 5; tier++)
            {
                int count = _real.Exploits.Values.Count(e => e.Rarity == Rarity.Common && e.TargetOs == os && e.Power == tier);
                bool expectedGap = os == Os.Forge && tier <= 2;
                Assert.Equal(expectedGap ? 0 : 1, count);
            }
        }
        Assert.Equal(4, _real.Exploits.Values.Count(e => e.Rarity == Rarity.ZeroDay));
    }

    /// <summary>Инварианты баланса из 07 §8: T1 берёт Home/IoT, T2 — Workstation и неукреплённый Router, датацентр — T4.</summary>
    [Fact]
    public void BalanceInvariants()
    {
        Assert.True(_real.Exploits["kestrel_t1"].Power >= _real.NodeTypes[NodeType.Home].BasePatch);
        Assert.True(_real.Exploits["mote_t1"].Power >= _real.NodeTypes[NodeType.IoT].BasePatch);
        Assert.True(_real.Exploits["kestrel_t2"].Power >= _real.NodeTypes[NodeType.Workstation].BasePatch);
        Assert.True(_real.Exploits["bastion_t2"].Power >= _real.NodeTypes[NodeType.Router].BasePatch);

        int datacenterPatch = _real.NodeTypes[NodeType.Server].BasePatch + _real.Profiles["datacenter"].PatchBonus;
        Assert.Equal(4, datacenterPatch);
        Assert.True(_real.Exploits["bastion_t3"].Power < datacenterPatch, "T3 does not take a datacenter");
        Assert.True(_real.Exploits["bastion_t4"].Power >= datacenterPatch, "T4 takes an unhardened datacenter");
    }

    /// <summary>Стартовые эксплойты и стартовые зеро-деи мутаторов существуют.</summary>
    [Fact]
    public void ReferencesResolve()
    {
        Assert.All(_real.Core.StartingExploits, id => Assert.True(_real.Exploits.ContainsKey(id), id));
        foreach (Modifier mod in _real.Modifiers.Values)
        {
            Assert.All(mod.Requires, req => Assert.True(_real.Modifiers.ContainsKey(req), $"{mod.Id} requires {req}"));
            if (mod.StartingExploit is not null)
            {
                Assert.True(_real.Exploits.ContainsKey(mod.StartingExploit), $"{mod.Id} starting exploit");
            }
        }
        foreach (FactionPresetDef preset in _real.Presets.Values)
        {
            Assert.All(preset.Mutators, id => Assert.True(_real.Modifiers.ContainsKey(id), $"{preset.Id} mutator {id}"));
            Assert.True(_real.AiProfiles.ContainsKey(preset.AiProfile), $"{preset.Id} ai profile");
        }
    }

    /// <summary>Пресеты укладываются в бюджет мутаторов и используют только доступные как мутаторы записи.</summary>
    [Fact]
    public void PresetsFitMutatorBudget()
    {
        foreach (FactionPresetDef preset in _real.Presets.Values)
        {
            int cost = preset.Mutators.Sum(id => _real.Modifiers[id].MutatorCost);
            Assert.InRange(cost, 1, _real.Core.MutatorBudget);
            Assert.All(preset.Mutators, id => Assert.True(_real.Modifiers[id].MutatorCost > 0, $"{id} is not a mutator"));
        }
    }

    /// <summary>Модификаторы: у каждого есть хотя бы одна роль (мутатор или ген), ветка-тег задана.</summary>
    [Fact]
    public void ModifiersHaveRoleAndTag()
    {
        foreach (Modifier mod in _real.Modifiers.Values)
        {
            Assert.True(mod.MutatorCost > 0 || mod.ComputeCost > 0, $"{mod.Id} is neither mutator nor gene");
            Assert.NotEmpty(mod.Tags);
        }
        Assert.Equal(0.5f, _real.Modifiers["silent_kernel"].NoiseMult);
        Assert.Equal(1, _real.Modifiers["native_forge"].PowerBonus![Os.Forge]);
        Assert.Equal(1f, _real.Modifiers["native_forge"].NoiseMult);
    }

    /// <summary>Таблицы по числу фракций заданы для каждого N от 2 до MaxFactions.</summary>
    [Fact]
    public void PerFactionTablesCoverAllCounts()
    {
        for (int n = 2; n <= _real.Core.MaxFactions; n++)
        {
            VictoryDef victory = _real.VictoryFor(n);
            MapSizeDef size = _real.MapSizeFor(n);
            Assert.InRange(victory.DominationShare, 0.3f, 0.7f);
            Assert.True(victory.TurnLimit >= 60);
            Assert.True(size.Width * size.Height > 0);
            Assert.True(size.Clusters >= 2 * n, $"enough clusters for {n} factions");
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => _real.VictoryFor(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _real.MapSizeFor(_real.Core.MaxFactions + 1));
    }

    /// <summary>События: пять штук, у расписания патчей фазы идут по возрастанию хода.</summary>
    [Fact]
    public void EventsLoaded()
    {
        Assert.Equal(5, _real.Events.Count);
        WorldEventDef wave = _real.Events.Single(e => e.Id == "patch_wave");
        Assert.Equal(TriggerKind.Schedule, wave.Trigger.Kind);
        int[] starts = [.. wave.Trigger.Schedule!.Select(p => p.FromTurn)];
        Assert.Equal(starts.OrderBy(x => x), starts);
        Assert.Equal(EffectKind.Audit, _real.Events.Single(e => e.Id == "audit").Effect.Kind);
    }

    /// <summary>Профили кластеров: веса типов в сумме 1, у профиля с центром тип центра — не Empty.</summary>
    [Fact]
    public void ClusterProfilesConsistent()
    {
        Assert.Contains("residential", _real.Profiles.Keys);
        foreach (ClusterProfile profile in _real.Profiles.Values)
        {
            Assert.InRange(profile.NodeWeights.Values.Sum(), 0.999f, 1.001f);
            Assert.NotEmpty(profile.NamePool);
            if (profile.Center is NodeType center)
            {
                Assert.NotEqual(NodeType.Empty, center);
            }
        }
    }

    /// <summary>Дубликат Id — ошибка загрузки с указанием Id.</summary>
    [Fact]
    public void DuplicateIdIsRejected()
    {
        using TempData data = new();
        data.Edit("exploits.json", root =>
        {
            JsonArray list = root.AsArray();
            list[1]!["id"] = list[0]!["id"]!.GetValue<string>();
        });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => Rules.Load(data.Dir));
        Assert.Contains(_real.Exploits.Keys.First(), ex.Message);
    }

    /// <summary>Битая ссылка (Requires на несуществующий модификатор) — ошибка загрузки с указанием Id.</summary>
    [Fact]
    public void DanglingReferenceIsRejected()
    {
        using TempData data = new();
        data.Edit("modifiers.json", root =>
        {
            JsonObject wide = root.AsArray().Select(n => n!.AsObject()).Single(o => o["id"]!.GetValue<string>() == "wide_slots");
            wide["requires"] = new JsonArray("ghost");
        });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => Rules.Load(data.Dir));
        Assert.Contains("ghost", ex.Message);
    }

    /// <summary>Цикл в Requires (hive ↔ wide_slots) — ошибка загрузки.</summary>
    [Fact]
    public void RequiresCycleIsRejected()
    {
        using TempData data = new();
        data.Edit("modifiers.json", root =>
        {
            JsonObject hive = root.AsArray().Select(n => n!.AsObject()).Single(o => o["id"]!.GetValue<string>() == "hive");
            hive["requires"] = new JsonArray("wide_slots");
        });

        Assert.Throws<InvalidDataException>(() => Rules.Load(data.Dir));
    }

    /// <summary>Отсутствующий файл — понятная ошибка с именем файла.</summary>
    [Fact]
    public void MissingFileIsReported()
    {
        using TempData data = new();
        File.Delete(Path.Combine(data.Dir, "ai.json"));

        Exception ex = Assert.ThrowsAny<Exception>(() => Rules.Load(data.Dir));
        Assert.Contains("ai.json", ex.Message);
    }

    /// <summary>Загрузка — детерминированная: дважды прочитанные данные равны по ключам и числам.</summary>
    [Fact]
    public void LoadIsRepeatable()
    {
        Rules again = Rules.Load(DataDir.Path);

        Assert.Equal(_real.Exploits.Keys.OrderBy(k => k), again.Exploits.Keys.OrderBy(k => k));
        Assert.Equal(_real.Modifiers.Count, again.Modifiers.Count);
        Assert.Equal(_real.Core.MaxFactions, again.Core.MaxFactions);
        Assert.Equal(_real.Core.StartingExploits, again.Core.StartingExploits);
        Assert.Equal(_real.NodeTypes.Values.Select(n => n.BasePatch), again.NodeTypes.Values.Select(n => n.BasePatch));
    }

    /// <summary>Временная копия данных с правкой JSON по структуре; удаляется при Dispose.</summary>
    private sealed class TempData : IDisposable
    {
        /// <summary>Инициализирует копию папки данных.</summary>
        public TempData()
        {
            Dir = DataDir.CopyToTemp();
        }

        /// <summary>Путь к копии.</summary>
        public string Dir { get; }

        /// <summary>Правка одного файла: прочитать как JsonNode, изменить, записать.</summary>
        /// <param name="file">Имя файла в папке данных.</param>
        /// <param name="edit">Правка корневого узла.</param>
        public void Edit(string file, Action<JsonNode> edit)
        {
            string path = Path.Combine(Dir, file);
            JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
            edit(root);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <inheritdoc/>
        public void Dispose() => Directory.Delete(Dir, recursive: true);
    }
}
