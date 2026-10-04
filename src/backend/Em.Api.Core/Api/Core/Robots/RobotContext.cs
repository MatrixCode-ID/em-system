using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;

namespace Em.Api.Core;

/// <summary>Robot identities are available independently of container registry.</summary>
public class RobotContext(DbContextOptions<RobotContext> options) : EmDbContext(options)
{
   public DbSet<ta_Robot> Robots => Set<ta_Robot>();
   public DbSet<ta_User> Users => Set<ta_User>();
   protected override void OnModelCreating(ModelBuilder builder) {
      base.OnModelCreating(builder);
      builder.Entity<ta_Robot>();
   }
}
