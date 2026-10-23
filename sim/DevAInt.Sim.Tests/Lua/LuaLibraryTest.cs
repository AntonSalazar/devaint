using DevAInt.Sim.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Lua;

/// <summary>Тесты замещённых функций библиотеки: результат как у стандартного Lua.</summary>
public class LuaLibraryTest
{
    /// <summary>table.sort сортирует числа, строки и по своей функции сравнения.</summary>
    [Theory]
    [InlineData("local t = {5, 3, 9, 1, 7} table.sort(t)", "1 3 5 7 9")]
    [InlineData("local t = {'pear', 'apple', 'fig'} table.sort(t)", "apple fig pear")]
    [InlineData("local t = {5, 3, 9, 1, 7} table.sort(t, function(a, b) return a > b end)", "9 7 5 3 1")]
    [InlineData("local t = {} table.sort(t)", "")]
    [InlineData("local t = {2, 2, 1, 1} table.sort(t)", "1 1 2 2")]
    public void SortOrders(string code, string expected) => Assert.Equal(expected, LuaRun.Print(code + " print(table.concat(t, ' '))"));

    /// <summary>table.sort большого списка совпадает с сортировкой C# и укладывается в лимит.</summary>
    [Fact]
    public void SortHandlesLargeList()
    {
        string output = LuaRun.Print("""
            local t = {}
            local x = 7
            for i = 1, 1000 do x = (x * 31 + 17) % 1009 t[i] = x end
            table.sort(t)
            local ok = true
            for i = 2, #t do if t[i - 1] > t[i] then ok = false end end
            print(tostring(ok) .. " " .. #t)
            """);

        Assert.Equal("true 1000", output);
    }

    /// <summary>string.gsub с функцией и таблицей даёт те же строку и число замен, что стандартный Lua.</summary>
    [Theory]
    [InlineData("""string.gsub("hello world", "o", { o = "0" })""", "hell0 w0rld 2")]
    [InlineData("""string.gsub("abc", "%w", function(c) return c:upper() end)""", "ABC 3")]
    [InlineData("""string.gsub("a=1, b=2", "(%w+)=(%w+)", function(k, v) return v .. "=" .. k end)""", "1=a, 2=b 2")]
    [InlineData("""string.gsub("abc", "%w", function(c) if c == "b" then return nil end return "-" end)""", "-b- 3")]
    [InlineData("""string.gsub("aaa", "a", function() return "b" end, 2)""", "bba 2")]
    [InlineData("""string.gsub("aaa", "^a", function() return "b" end)""", "baa 1")]
    [InlineData("""string.gsub("abc", "x*", function() return "-" end)""", "-a-b-c- 4")]
    [InlineData("""string.gsub("$name is $age", "%$(%w+)", { name = "Bob", age = 3 })""", "Bob is 3 2")]
    [InlineData("""string.gsub("hello", "l", "L")""", "heLLo 2")]
    public void GsubMatchesStandardLua(string call, string expected) => Assert.Equal(expected, LuaRun.Print($"local s, n = {call} print(s .. ' ' .. n)"));

    /// <summary>Неверное значение замены - ошибка, как в стандартном Lua.</summary>
    [Fact]
    public void GsubRejectsBadReplacement()
    {
        LuaRun run = LuaRun.Start("""string.gsub("a", "a", function() return {} end)""");

        ModError error = Assert.Single(run.Sim.ScriptErrors);
        Assert.Contains("invalid replacement value (a table)", error.Message);
    }

    /// <summary>xpcall возвращает результат функции или результат обработчика ошибки.</summary>
    [Fact]
    public void XpcallCallsHandler()
    {
        string output = LuaRun.Print("""
            print(tostring(select(2, xpcall(function() return "fine" end, print))))
            local ok, message = xpcall(function() error("bad") end, function(m) return "handled" end)
            print(tostring(ok) .. " " .. message)
            """);

        Assert.Equal("fine\nfalse handled", output);
    }

    /// <summary>string.rep работает в пределах лимита длины и отказывает сверх него.</summary>
    [Fact]
    public void RepIsLimited()
    {
        Assert.Equal("ab-ab-ab", LuaRun.Print("print(string.rep('ab', 3, '-'))"));

        LuaRun run = LuaRun.Start("string.rep('x', 2000000)");
        Assert.Contains("string.rep result is longer than 1000000 characters", Assert.Single(run.Sim.ScriptErrors).Message);
    }
}
