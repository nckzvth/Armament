using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Armament.Persistence.Data;

public sealed class DesignTimeArmamentDbContextFactory : IDesignTimeDbContextFactory<ArmamentDbContext>
{
    public ArmamentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ARMAMENT_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=armament_dev;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ArmamentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ArmamentDbContext(options);
    }
}
