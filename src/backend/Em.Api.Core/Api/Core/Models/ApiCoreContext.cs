using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Approval;

namespace Em.Api.Core.Models
{
   public class ApiCoreContext(DbContextOptions<ApiCoreContext> options) : EmDbContext(options)
   {
      public DbSet<ta_Meta> ta_Metas => Set<ta_Meta>();
      public DbSet<ta_Contact> ta_Contacts => Set<ta_Contact>();
      public DbSet<ta_Address> ta_Addresses => Set<ta_Address>();
      public DbSet<ta_Comm> ta_Comms => Set<ta_Comm>();
      public DbSet<vi_Contact> vi_Contacts => Set<vi_Contact>();
      public DbSet<vi_Address> vi_Addresses => Set<vi_Address>();
      public DbSet<vi_Comm> vi_Comms => Set<vi_Comm>();
      public DbSet<ta_User> ta_Users => Set<ta_User>();
      public DbSet<vi_User> vi_Users => Set<vi_User>();
      public DbSet<ta_UserCredential> ta_UserCredentials => Set<ta_UserCredential>();
      public DbSet<vi_UserCredential> vi_UserCredentials => Set<vi_UserCredential>();
      public DbSet<ta_UserSession> ta_UserSessions => Set<ta_UserSession>();
      public DbSet<ta_UserClaim> ta_UserClaims => Set<ta_UserClaim>();
      public DbSet<ta_UserRole> ta_UserRoles => Set<ta_UserRole>();
      public DbSet<ta_Role> ta_Roles => Set<ta_Role>();
      public DbSet<ta_RoleClaim> ta_RoleClaims => Set<ta_RoleClaim>();
      public DbSet<vi_Role> vi_Roles => Set<vi_Role>();
      public DbSet<ta_Emp> ta_Emps => Set<ta_Emp>();
      public DbSet<ta_ApprovalRequest> ta_ApprovalRequests => Set<ta_ApprovalRequest>();
      public DbSet<ta_ApprovalRequestStep> ta_ApprovalRequestSteps => Set<ta_ApprovalRequestStep>();
      public DbSet<ta_ApprovalRequestStepSigner> ta_ApprovalRequestStepSigners => Set<ta_ApprovalRequestStepSigner>();
      public DbSet<ta_ApprovalRequestItem> ta_ApprovalRequestItems => Set<ta_ApprovalRequestItem>();
      public DbSet<ta_ApprovalRequestItemKey> ta_ApprovalRequestItemKeys => Set<ta_ApprovalRequestItemKey>();
      public DbSet<ta_ApprovalRequestItemField> ta_ApprovalRequestItemFields => Set<ta_ApprovalRequestItemField>();
      public DbSet<ta_ApprovalRequestComment> ta_ApprovalRequestComments => Set<ta_ApprovalRequestComment>();

      // Internal because the entity behind it is: its rows never leave the server as they are.
      // EF only discovers entities from public DbSet properties, so this one is named explicitly
      // in OnModelCreating below - without that the model would not know the type at all.
      internal DbSet<ta_SystemSession> ta_SystemSessions => Set<ta_SystemSession>();

      protected override void OnModelCreating(ModelBuilder modelBuilder) {
         base.OnModelCreating(modelBuilder);
         modelBuilder.Entity<ta_SystemSession>();

         // JSON_VALUE exists for sorting the approval list on a summary column; it is a built-in
         // function, so nothing is created in the database.
         modelBuilder.HasDbFunction(typeof(ApprovalJsonFunctions).GetMethod(nameof(ApprovalJsonFunctions.JsonValue))!)
            .HasName("JSON_VALUE")
            .IsBuiltIn();
         
         modelBuilder.Entity<ta_RoleClaim>().HasKey(k => new { k.cRoleId, k.cClaimName });
         modelBuilder.Entity<ta_UserRole>().HasKey(k => new { k.cUserId, k.cRoleId });

         // The three lean approval tables carry no standard column set of their own, so their key is
         // the combination that identifies the row - the same shape ta_RoleClaim above has.
         modelBuilder.Entity<ta_ApprovalRequestStepSigner>()
            .HasKey(k => new { k.cApprovalRequestId, k.cApprovalRequestStepName, k.cUserId });
         modelBuilder.Entity<ta_ApprovalRequestItemKey>()
            .HasKey(k => new { k.cApprovalRequestItemId, k.cApprovalRequestItemKeyName });
         modelBuilder.Entity<ta_ApprovalRequestItemField>()
            .HasKey(k => new { k.cApprovalRequestItemId, k.cApprovalRequestItemFieldName });
      }
   }
}
