using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vigil.Infrastructure;

/// <summary>
/// Lets `dotnet ef` build the model without starting the API, whose connection string only exists under Aspire.
/// The connection string is never opened when adding migrations.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VigilDbContext>
{
    public VigilDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<VigilDbContext>()
            .UseNpgsql("Host=localhost;Database=vigil")
            .Options);
}
