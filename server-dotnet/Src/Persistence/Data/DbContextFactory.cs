using Microsoft.EntityFrameworkCore;

namespace Armament.Persistence.Data;

public static class DbContextFactory
{
    public static DbContextOptions<ArmamentDbContext> BuildOptions(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ArmamentDbContext>();
        builder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(ArmamentDbContext).Assembly.FullName));
        return builder.Options;
    }
}
