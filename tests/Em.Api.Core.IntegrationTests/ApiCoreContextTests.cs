using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;

namespace Em.Api.Core.IntegrationTests
{
   public class ApiCoreContextTests(SqlServerDatabase database)
   {
      // The EF model of the core tables has to be something SQL Server accepts as a schema: a mapping
      // error (an unsupported type, a key EF cannot build) only shows up here, not at compile time.
      [Fact]
      public async Task Model_CreatesSchemaOnSqlServer() {
         var ct = TestContext.Current.CancellationToken;
         await using var db = new ApiCoreContext(
            new DbContextOptionsBuilder<ApiCoreContext>().UseSqlServer(database.ConnectionString).Options);

         await db.Database.EnsureCreatedAsync(ct);

         Assert.Equal(0, await db.ta_Users.CountAsync(ct));
         Assert.Equal(0, await db.ta_Metas.CountAsync(ct));
      }
   }
}
