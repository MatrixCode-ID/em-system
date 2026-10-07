using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;

namespace Em.Api.Core;

/// <summary>Robot identities are available independently of container registry.</summary>
public class RobotContext(DbContextOptions<RobotContext> options) : EmDbContext(options)
{
   /// <summary>Robot identities.</summary>
   public DbSet<ta_Robot> Robots => Set<ta_Robot>();
   /// <summary>Users, read to validate robot owners.</summary>
   public DbSet<ta_User> Users => Set<ta_User>();
   /// <summary>Configures the keys and relations of the robot entities.</summary>
   protected override void OnModelCreating(ModelBuilder builder) {
      base.OnModelCreating(builder);
      builder.Entity<ta_Robot>();
   }
}
