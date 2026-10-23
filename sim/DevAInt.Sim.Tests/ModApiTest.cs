using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>Тесты <see cref="ModApi"/>.</summary>
public class ModApiTest
{
    /// <summary>Первая версия API равна единице.</summary>
    [Fact]
    public void VersionStartsFromOne() => Assert.Equal(1, ModApi.Version);
}
