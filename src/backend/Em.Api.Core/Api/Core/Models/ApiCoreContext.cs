using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Approval;

namespace Em.Api.Core.Models
{
   /// <summary>Database context of the core tables: metadata, contacts, users, roles, claims, sessions, and approvals.</summary>
   public class ApiCoreContext(DbContextOptions<ApiCoreContext> options) : EmDbContext(options)
   {
      /// <summary>Rows of table <c>ta_Meta</c>.</summary>
      public DbSet<ta_Meta> ta_Metas => Set<ta_Meta>();
      /// <summary>Named SMTP configurations; raw credential rows stay on the server.</summary>
      public DbSet<ta_Smtp> ta_Smtps => Set<ta_Smtp>();
      /// <summary>Rows of table <c>ta_Contact</c>.</summary>
      public DbSet<ta_Contact> ta_Contacts => Set<ta_Contact>();
      /// <summary>Rows of table <c>ta_Address</c>.</summary>
      public DbSet<ta_Address> ta_Addresses => Set<ta_Address>();
      /// <summary>Rows of table <c>ta_Comm</c>.</summary>
      public DbSet<ta_Comm> ta_Comms => Set<ta_Comm>();
      /// <summary>Rows of view <c>vi_Contact</c>.</summary>
      public DbSet<vi_Contact> vi_Contacts => Set<vi_Contact>();
      /// <summary>Rows of view <c>vi_Address</c>.</summary>
      public DbSet<vi_Address> vi_Addresses => Set<vi_Address>();
      /// <summary>Rows of view <c>vi_Comm</c>.</summary>
      public DbSet<vi_Comm> vi_Comms => Set<vi_Comm>();
      /// <summary>Rows of table <c>ta_User</c>.</summary>
      public DbSet<ta_User> ta_Users => Set<ta_User>();
      /// <summary>Rows of view <c>vi_User</c>.</summary>
      public DbSet<vi_User> vi_Users => Set<vi_User>();
      /// <summary>Rows of table <c>ta_UserCredential</c>.</summary>
      public DbSet<ta_UserCredential> ta_UserCredentials => Set<ta_UserCredential>();
      /// <summary>Rows of view <c>vi_UserCredential</c>.</summary>
      public DbSet<vi_UserCredential> vi_UserCredentials => Set<vi_UserCredential>();
      /// <summary>Rows of table <c>ta_UserSession</c>.</summary>
      public DbSet<ta_UserSession> ta_UserSessions => Set<ta_UserSession>();
      /// <summary>Rows of table <c>ta_UserClaim</c>.</summary>
      public DbSet<ta_UserClaim> ta_UserClaims => Set<ta_UserClaim>();
      /// <summary>Rows of table <c>ta_UserRole</c>.</summary>
      public DbSet<ta_UserRole> ta_UserRoles => Set<ta_UserRole>();
      /// <summary>Rows of table <c>ta_Role</c>.</summary>
      public DbSet<ta_Role> ta_Roles => Set<ta_Role>();
      /// <summary>Rows of table <c>ta_RoleClaim</c>.</summary>
      public DbSet<ta_RoleClaim> ta_RoleClaims => Set<ta_RoleClaim>();
      /// <summary>Rows of view <c>vi_Role</c>.</summary>
      public DbSet<vi_Role> vi_Roles => Set<vi_Role>();
      /// <summary>Rows of table <c>ta_Emp</c>.</summary>
      public DbSet<ta_Emp> ta_Emps => Set<ta_Emp>();
      /// <summary>Rows of table <c>ta_ApprovalRequest</c>.</summary>
      public DbSet<ta_ApprovalRequest> ta_ApprovalRequests => Set<ta_ApprovalRequest>();
      /// <summary>Rows of table <c>ta_ApprovalRequestStep</c>.</summary>
      public DbSet<ta_ApprovalRequestStep> ta_ApprovalRequestSteps => Set<ta_ApprovalRequestStep>();
      /// <summary>Rows of table <c>ta_ApprovalRequestStepSigner</c>.</summary>
      public DbSet<ta_ApprovalRequestStepSigner> ta_ApprovalRequestStepSigners => Set<ta_ApprovalRequestStepSigner>();
      /// <summary>Rows of table <c>ta_ApprovalRequestItem</c>.</summary>
      public DbSet<ta_ApprovalRequestItem> ta_ApprovalRequestItems => Set<ta_ApprovalRequestItem>();
      /// <summary>Rows of table <c>ta_ApprovalRequestItemKey</c>.</summary>
      public DbSet<ta_ApprovalRequestItemKey> ta_ApprovalRequestItemKeys => Set<ta_ApprovalRequestItemKey>();
      /// <summary>Rows of table <c>ta_ApprovalRequestItemField</c>.</summary>
      public DbSet<ta_ApprovalRequestItemField> ta_ApprovalRequestItemFields => Set<ta_ApprovalRequestItemField>();
      /// <summary>Rows of table <c>ta_ApprovalRequestComment</c>.</summary>
      public DbSet<ta_ApprovalRequestComment> ta_ApprovalRequestComments => Set<ta_ApprovalRequestComment>();

      // Internal because the entity behind it is: its rows never leave the server as they are.
      // EF only discovers entities from public DbSet properties, so this one is named explicitly
      // in OnModelCreating below - without that the model would not know the type at all.
      internal DbSet<ta_SystemSession> ta_SystemSessions => Set<ta_SystemSession>();

      /// <summary>Configures the keys, relations, and conversions of the core entities.</summary>
      protected override void OnModelCreating(ModelBuilder modelBuilder) {
         base.OnModelCreating(modelBuilder);
         modelBuilder.Entity<ta_SystemSession>();
         var smtp = modelBuilder.Entity<ta_Smtp>();
         smtp.HasIndex(x => x.cSmtpName).IsUnique();
         if (Database.IsSqlServer()) {
            smtp.Property(x => x.cSmtpId).HasColumnType("char(26)");
            smtp.Property(x => x.cSmtpName).IsUnicode(false).UseCollation("SQL_Latin1_General_CP1_CI_AS");
            smtp.Property(x => x.cSmtpHost).IsUnicode(false);
            smtp.Property(x => x.cSmtpNote).IsUnicode(false);
            smtp.Property(x => x.cSmtpFromAddress).IsUnicode(false);
            smtp.Property(x => x.ustamp).HasColumnType("datetime");
            smtp.Property(x => x.datestamp).HasColumnType("datetime");
            smtp.HasIndex(x => x.cSmtpDefault).IsUnique().HasFilter("[cSmtpDefault] = 1");
         }

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
