using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Mods;

/// <summary>Загрузка модов: манифесты, порядок, определения и патчи в итоговую DefDatabase.</summary>
public static class ModLoader
{
    /// <summary>Загружает моды из папок modsRoot/id и собирает все ошибки сразу.</summary>
    public static ModLoadResult Load(string modsRoot, IReadOnlyList<string> modIds)
    {
        List<ModError> errors = [];
        string shownRoot = Path.GetDirectoryName(Path.GetFullPath(modsRoot))!;

        List<ModManifest> manifests = [];
        foreach (string modId in modIds)
        {
            if (ReadManifest(modsRoot, shownRoot, modId, errors) is ModManifest manifest)
            {
                manifests.Add(manifest);
            }
        }

        // Данные грузим только при верных манифестах и порядке, иначе ошибки потянут за собой лишние
        List<ModManifest> ordered = errors.Count == 0 ? ModOrder.Sort(manifests, errors) : [];
        if (errors.Count > 0)
        {
            return new ModLoadResult(ordered, new DefDatabase(), errors);
        }

        Dictionary<string, DefEntry> defs = [];
        foreach (ModManifest mod in ordered)
        {
            LoadData(mod, shownRoot, defs, errors);
        }

        DefDatabase database = new();
        foreach (DefEntry entry in defs.Values)
        {
            database.Add((IDef)JsonSerializer.Deserialize(entry.Node, entry.Type, Jsonc.Options)!);
        }

        return new ModLoadResult(ordered, database, errors);
    }

    /// <summary>Чтение и проверка mod.jsonc, null при ошибке.</summary>
    private static ModManifest? ReadManifest(string modsRoot, string shownRoot, string modId, List<ModError> errors)
    {
        string directory = Path.GetFullPath(Path.Combine(modsRoot, modId));
        string file = Path.Combine(directory, "mod.jsonc");
        string shown = Path.GetRelativePath(shownRoot, file);
        if (!File.Exists(file))
        {
            errors.Add(new ModError(shown, null, string.Empty, "manifest file not found"));
            return null;
        }

        ManifestFile? data;
        try
        {
            data = JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(file), Jsonc.Options);
        }
        catch (JsonException error)
        {
            errors.Add(Jsonc.ToError(shown, error));
            return null;
        }

        if (data is null)
        {
            errors.Add(new ModError(shown, null, string.Empty, "manifest is empty"));
            return null;
        }

        int before = errors.Count;
        if (data.Id != modId)
        {
            errors.Add(new ModError(shown, null, "$.id", $"id {data.Id} does not match folder {modId}"));
        }

        if (data.Api != ModApi.Version)
        {
            errors.Add(new ModError(shown, null, "$.api", $"mod targets API {data.Api}, core supports API {ModApi.Version}"));
        }

        if (!ModVersion.TryParse(data.Version, out ModVersion version))
        {
            errors.Add(new ModError(shown, null, "$.version", $"version {data.Version} is not in 0.1 or 0.1.0 format"));
        }

        List<ModDependency> depends = [];
        for (int i = 0; i < data.Depends.Count; i++)
        {
            if (ModDependency.Parse(data.Depends[i]) is ModDependency dependency)
            {
                depends.Add(dependency);
            }
            else
            {
                errors.Add(new ModError(shown, null, $"$.depends[{i}]", $"dependency {data.Depends[i]} is not in 'id' or 'id >= 0.1' format"));
            }
        }

        return errors.Count == before ? new ModManifest(data.Id, data.Name, version, depends, data.LoadAfter, directory, shown) : null;
    }

    /// <summary>Определения и патчи из data/*.jsonc мода; файлы по имени, патчи после всех определений мода.</summary>
    private static void LoadData(ModManifest mod, string shownRoot, Dictionary<string, DefEntry> defs, List<ModError> errors)
    {
        string dataDir = Path.Combine(mod.Directory, "data");
        string[] files = Directory.Exists(dataDir) ? Directory.GetFiles(dataDir, "*.jsonc") : [];
        Array.Sort(files, StringComparer.Ordinal);

        List<(string File, int Index, PatchDef Patch)> patches = [];
        foreach (string file in files)
        {
            string shown = Path.GetRelativePath(shownRoot, file);
            string text = File.ReadAllText(file);

            // Типизированный разбор ловит синтаксис, неверные типы и неизвестные поля со строкой
            DefFile? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<DefFile>(text, Jsonc.Options);
            }
            catch (JsonException error)
            {
                errors.Add(Jsonc.ToError(shown, error));
                continue;
            }

            // Дерево того же текста нужно для патчей, разбор уже проверен выше
            JsonNode? root = JsonNode.Parse(text, null, Jsonc.DocumentOptions);
            if (parsed is null || root is not JsonObject rootObject)
            {
                errors.Add(new ModError(shown, null, string.Empty, "file must be an object"));
                continue;
            }

            foreach ((string category, Type type) in DefFile.Categories)
            {
                if (rootObject[category] is JsonArray list)
                {
                    AddDefs(mod, shown, category, type, list, defs, errors);
                }
            }

            List<PatchDef?> filePatches = parsed.Patches ?? [];
            for (int i = 0; i < filePatches.Count; i++)
            {
                if (filePatches[i] is PatchDef patch)
                {
                    patches.Add((shown, i, patch));
                }
                else
                {
                    errors.Add(new ModError(shown, null, $"$.patches[{i}]", "patch must be an object"));
                }
            }
        }

        foreach ((string file, int index, PatchDef patch) in patches)
        {
            ApplyPatch(file, $"$.patches[{index}]", patch, defs, errors);
        }
    }

    /// <summary>Добавление определений одной категории файла с проверкой id.</summary>
    private static void AddDefs(ModManifest mod, string file, string category, Type type, JsonArray list, Dictionary<string, DefEntry> defs, List<ModError> errors)
    {
        for (int i = 0; i < list.Count; i++)
        {
            string path = $"$.{category}[{i}]";
            if (list[i] is not JsonObject node)
            {
                errors.Add(new ModError(file, null, path, "definition must be an object"));
                continue;
            }

            // Поле id обязательное и строковое, это уже проверил типизированный разбор
            string id = node["id"]!.GetValue<string>();
            if (!ModIds.IsDefId(id) || !ModIds.BelongsTo(id, mod.Id))
            {
                errors.Add(new ModError(file, null, path + ".id", $"id {id} must look like {mod.Id}:name using a-z, 0-9 and _"));
                continue;
            }

            if (defs.TryGetValue(id, out DefEntry? existing))
            {
                errors.Add(new ModError(file, null, path + ".id", $"{id} is already defined in {existing.File}, use a patch to change it"));
                continue;
            }

            defs[id] = new DefEntry(type, (JsonObject)node.DeepClone(), file);
        }
    }

    /// <summary>Применение патча; при ошибке определение не меняется.</summary>
    private static void ApplyPatch(string file, string path, PatchDef patch, Dictionary<string, DefEntry> defs, List<ModError> errors)
    {
        if (!defs.TryGetValue(patch.Target, out DefEntry? entry))
        {
            errors.Add(new ModError(file, null, path, $"target {patch.Target} not found"));
            return;
        }

        if (string.IsNullOrEmpty(patch.Path))
        {
            if (patch.Op == PatchOp.Remove)
            {
                defs.Remove(patch.Target);
            }
            else
            {
                errors.Add(new ModError(file, null, path, $"{patch.Op.ToString().ToLowerInvariant()} requires 'path'"));
            }

            return;
        }

        if (patch.Op != PatchOp.Remove && patch.Value is null)
        {
            errors.Add(new ModError(file, null, path, $"{patch.Op.ToString().ToLowerInvariant()} requires 'value'"));
            return;
        }

        // Патч применяется к копии, чтобы неудачный патч не испортил определение
        JsonObject changed = (JsonObject)entry.Node.DeepClone();
        if (!PatchApplier.TryApply(changed, patch, out string error))
        {
            errors.Add(new ModError(file, null, path, error));
            return;
        }

        if (changed["id"] is not JsonValue id || !id.TryGetValue(out string? newId) || newId != patch.Target)
        {
            errors.Add(new ModError(file, null, path, "patch cannot change id"));
            return;
        }

        try
        {
            JsonSerializer.Deserialize(changed, entry.Type, Jsonc.Options);
        }
        catch (JsonException exception)
        {
            string field = exception.Path is string fieldPath ? $" {fieldPath}:" : string.Empty;
            errors.Add(new ModError(file, null, path, $"{patch.Target} is invalid after patch:{field} {Jsonc.ShortMessage(exception.Message)}"));
            return;
        }

        entry.Node = changed;
    }

    /// <summary>Определение во время загрузки: тип, дерево JSON и файл, где оно объявлено.</summary>
    private sealed class DefEntry(Type type, JsonObject node, string file)
    {
        /// <summary>Тип записи определения.</summary>
        public Type Type { get; } = type;

        /// <summary>Текущее дерево JSON с применёнными патчами.</summary>
        public JsonObject Node { get; set; } = node;

        /// <summary>Файл, где определение объявлено.</summary>
        public string File { get; } = file;
    }

    /// <summary>Содержимое mod.jsonc до проверки.</summary>
    private sealed record ManifestFile
    {
        /// <summary>Id мода, совпадает с именем папки.</summary>
        public required string Id { get; init; }

        /// <summary>Название для людей.</summary>
        public required string Name { get; init; }

        /// <summary>Версия мода.</summary>
        public required string Version { get; init; }

        /// <summary>Версия API ядра, для которой написан мод.</summary>
        public required int Api { get; init; }

        /// <summary>Зависимости вида id или id >= 0.1.</summary>
        public List<string> Depends { get; init; } = [];

        /// <summary>Моды, после которых грузиться, если они есть.</summary>
        public List<string> LoadAfter { get; init; } = [];
    }
}
