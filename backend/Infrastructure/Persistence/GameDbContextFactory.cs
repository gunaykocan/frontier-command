using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Game.Infrastructure.Persistence;

public sealed class GameDbContextFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    private const string DevelopmentConnection =
        "Host=localhost;Port=5432;Database=game;Username=game";

    public GameDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__GameDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DevelopmentConnection;
        }

        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new GameDbContext(options);
    }
}
