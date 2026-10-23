using DevAInt.Sim.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Mods;

/// <summary>Тесты <see cref="ModVersion"/> и <see cref="ModDependency"/>.</summary>
public class ModVersionTest
{
    /// <summary>Версия из двух частей получает patch 0, из трёх читается целиком.</summary>
    [Theory]
    [InlineData("0.1", 0, 1, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    public void ParsesVersion(string text, int major, int minor, int patch)
    {
        Assert.True(ModVersion.TryParse(text, out ModVersion version));
        Assert.Equal(new ModVersion(major, minor, patch), version);
    }

    /// <summary>Неверные строки не разбираются.</summary>
    [Theory]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b")]
    [InlineData("-1.0")]
    [InlineData("1..2")]
    public void RejectsBadVersion(string text) => Assert.False(ModVersion.TryParse(text, out _));

    /// <summary>Сравнение идёт по major, затем minor, затем patch.</summary>
    [Fact]
    public void ComparesParts()
    {
        Assert.True(new ModVersion(1, 0, 0).CompareTo(new ModVersion(0, 9, 9)) > 0);
        Assert.True(new ModVersion(0, 2, 0).CompareTo(new ModVersion(0, 10, 0)) < 0);
        Assert.Equal(0, new ModVersion(0, 1, 0).CompareTo(new ModVersion(0, 1, 0)));
    }

    /// <summary>Зависимость без версии и с минимальной версией.</summary>
    [Fact]
    public void ParsesDependency()
    {
        Assert.Equal(new ModDependency("core", null), ModDependency.Parse("core"));
        Assert.Equal(new ModDependency("core", new ModVersion(0, 1, 0)), ModDependency.Parse("core >= 0.1"));
    }

    /// <summary>Неверные зависимости не разбираются.</summary>
    [Theory]
    [InlineData("core > 0.1")]
    [InlineData("core >=")]
    [InlineData("Core")]
    [InlineData("core >= x")]
    public void RejectsBadDependency(string text) => Assert.Null(ModDependency.Parse(text));

    /// <summary>Минимальная версия пропускает равную и новее.</summary>
    [Fact]
    public void DependencyChecksMinVersion()
    {
        ModDependency dependency = ModDependency.Parse("core >= 0.2")!;
        Assert.True(dependency.IsSatisfiedBy(new ModVersion(0, 2, 0)));
        Assert.True(dependency.IsSatisfiedBy(new ModVersion(1, 0, 0)));
        Assert.False(dependency.IsSatisfiedBy(new ModVersion(0, 1, 9)));
    }
}
