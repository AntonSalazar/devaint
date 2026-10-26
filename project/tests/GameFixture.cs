using DevAInt.Sim;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Net;

/// <summary>Сервер и клиент в одном процессе на модах и стартовой комнате, как в игре.</summary>
public sealed class GameFixture
{
    /// <summary>Загрузка модов mods, по умолчанию только core, запуск сервера и подключение клиента.</summary>
    public GameFixture(params string[] mods)
    {
        ModLoadResult loaded = ModLoader.Load(ModsPath.Find(), mods.Length > 0 ? mods : ["core"]);
        Loaded = loaded;
        Sim = new Simulation(loaded, new GodotLog(), GameRoot.MapId);
        LoopbackTransport transport = new();
        Server = new GameServer(Sim, transport, new GodotLog());
        Client = new GameClient(transport.Connect(), ClientTables.From(Sim), "player");
    }

    /// <summary>Итог загрузки модов.</summary>
    public ModLoadResult Loaded { get; }

    /// <summary>Sim сервера.</summary>
    public Simulation Sim { get; }

    /// <summary>Сервер.</summary>
    public GameServer Server { get; }

    /// <summary>Клиент.</summary>
    public GameClient Client { get; }

    /// <summary>Тики сервера с применением пакетов клиентом.</summary>
    public void Tick(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            Server.Tick();
            Client.Update();
        }
    }
}
