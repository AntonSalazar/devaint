using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Lua;

/// <summary>Тесты Lua для объектов мира и контейнеров на стартовой комнате core.</summary>
public class LuaObjectsTest
{
    /// <summary>objects.at находит объект по любой его клетке, objects.near - по ближайшей точке, пустая клетка - nil.</summary>
    [Fact]
    public void QueriesFindObjects()
    {
        string output = Run("""
            local t = objects.at(5, 4, 0)
            print(t.object .. " " .. t.width .. "x" .. t.depth .. " " .. t.x .. "," .. t.y)
            local near = objects.near(8.5, 8.5, 0, 1)
            print(#near .. " " .. near[1].object)
            print(tostring(objects.at(6, 6, 0)))
            """, out _);

        Assert.Equal("core:table 2x1 4,4\n1 core:crate\nnil", output);
    }

    /// <summary>objects.place ставит объект с поворотом и возвращает id, без места - nil и причину; objects.remove убирает.</summary>
    [Fact]
    public void PlaceAndRemove()
    {
        string output = Run("""
            local id = objects.place("core:table", 2, 2, 0, 1)
            print(objects.at(2, 3, 0).id == id)
            local bad, why = objects.place("core:crate", 2, 3, 0)
            print(tostring(bad) .. " " .. why)
            local none, unknown = objects.place("core:sofa", 3, 3, 0)
            print(unknown)
            print(tostring(objects.remove(id)) .. " " .. tostring(objects.remove(id)))
            """, out Simulation sim);

        Assert.Equal("true\nnil cell 2,3,0 is taken by another object\nunknown object core:sofa\ntrue false", output);
        Assert.Equal(4, sim.World.Objects.Count);
    }

    /// <summary>containers.items, add и remove работают с содержимым ящика; не контейнер - nil, переполнение - nil и причина.</summary>
    [Fact]
    public void ContainersAddAndRemove()
    {
        string output = Run("""
            local crate = objects.at(9, 8, 0).id
            local items = containers.items(crate)
            print(#items .. " " .. items[1].item)
            local id = containers.add(crate, "core:apple")
            print(#containers.items(crate) .. " " .. tostring(containers.remove(crate, id)) .. " " .. tostring(containers.remove(crate, id)))
            print(tostring(containers.items(objects.at(5, 4, 0).id)))
            local added = 0
            local last, why
            repeat
              last, why = containers.add(crate, "core:apple")
              if last then added = added + 1 end
            until not last
            print(added .. " " .. why)
            print(select(2, containers.add(crate, "core:nope")))
            """, out Simulation sim);

        long crate = sim.World.ObjectAt(new CellPos(9, 8, 0))!.Id;
        Assert.Equal($"4 core:apple\n5 true false\nnil\n58 object {crate} is full\nunknown item core:nope", output);
        Assert.Equal(20, sim.World.Container(crate)!.Weight, 9);
    }

    /// <summary>Вывод print скрипта, который выполняется в первом тике стартовой комнаты: к этому времени ящик уже наполнен.</summary>
    private static string Run(string body, out Simulation sim)
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        string script = "local done = false\nhooks.add(\"tick\", function()\n  if done then return end\n  done = true\n" + body + "\nend)\n";
        mods.Manifest("alpha", depends: "core").Write("alpha/scripts/main.lua", script);
        ModLoadResult result = mods.Load("core", "alpha");
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        ListLog log = new();
        sim = new Simulation(result, log, "core:starter_room");

        sim.Tick();

        Assert.Empty(log.Errors);
        return string.Join("\n", log.Infos.Select(line => line.Replace("[alpha] ", string.Empty)));
    }
}
