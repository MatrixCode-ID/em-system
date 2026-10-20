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
      /// <summary>Tenants of the business module manager.</summary>
      public DbSet<ta_Tenant> ta_Tenants => Set<ta_Tenant>();
      /// <summary>Tenants with their parent name and child count.</summary>
      public DbSet<vi_Tenant> vi_Tenants => Set<vi_Tenant>();
      /// <summary>Tenant organization records.</summary>
      public DbSet<ta_TenantDept> ta_TenantDepts => Set<ta_TenantDept>();
      /// <summary>Tenant organization records.</summary>
      public DbSet<vi_TenantDept> vi_TenantDepts => Set<vi_TenantDept>();
      /// <summary>Tenant organization records.</summary>
      public DbSet<ta_TenantEmp> ta_TenantEmps => Set<ta_TenantEmp>();
      /// <summary>Tenant organization records.</summary>
      public DbSet<vi_TenantEmp> vi_TenantEmps => Set<vi_TenantEmp>();

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
      /// <summary>Persistent approval ApprovalRequest rows.</summary>
      public DbSet<ta_ApprovalRequest> ta_ApprovalRequests => Set<ta_ApprovalRequest>();
      /// <summary>Persistent approval ApprovalRequestItem rows.</summary>
      public DbSet<ta_ApprovalRequestItem> ta_ApprovalRequestItems => Set<ta_ApprovalRequestItem>();
      /// <summary>Persistent approval ApprovalRequestItemField rows.</summary>
      public DbSet<ta_ApprovalRequestItemField> ta_ApprovalRequestItemFields => Set<ta_ApprovalRequestItemField>();
      /// <summary>Persistent approval ApprovalRequestStep rows.</summary>
      public DbSet<ta_ApprovalRequestStep> ta_ApprovalRequestSteps => Set<ta_ApprovalRequestStep>();
      /// <summary>Persistent approval ApprovalRequestStepCandidate rows.</summary>
      public DbSet<ta_ApprovalRequestStepCandidate> ta_ApprovalRequestStepCandidates => Set<ta_ApprovalRequestStepCandidate>();
      /// <summary>Persistent approval ApprovalRequestSignature rows.</summary>
      public DbSet<ta_ApprovalRequestSignature> ta_ApprovalRequestSignatures => Set<ta_ApprovalRequestSignature>();
      /// <summary>Persistent approval ApprovalRequestComment rows.</summary>
      public DbSet<ta_ApprovalRequestComment> ta_ApprovalRequestComments => Set<ta_ApprovalRequestComment>();
      /// <summary>Persistent approval ApprovalRequestAttachment rows.</summary>
      public DbSet<ta_ApprovalRequestAttachment> ta_ApprovalRequestAttachments => Set<ta_ApprovalRequestAttachment>();
      /// <summary>Persistent approval ApprovalSignerSetting rows.</summary>
      public DbSet<ta_ApprovalSignerSetting> ta_ApprovalSignerSettings => Set<ta_ApprovalSignerSetting>();
      /// <summary>Persistent approval ApprovalSignerSettingBackup rows.</summary>
      public DbSet<ta_ApprovalSignerSettingBackup> ta_ApprovalSignerSettingBackups => Set<ta_ApprovalSignerSettingBackup>();

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

         // datetime, not datetime2: the stale check compares ustamp in SQL, and a datetime2 parameter
         // may not equal a datetime value because of its 1/300 s rounding.
         if (Database.IsSqlServer()) foreach (var tenant in new[] { modelBuilder.Entity<ta_Tenant>().Metadata, modelBuilder.Entity<vi_Tenant>().Metadata }) {
            tenant.FindProperty(nameof(ta_Tenant.ustamp))!.SetColumnType("datetime");
            tenant.FindProperty(nameof(ta_Tenant.datestamp))!.SetColumnType("datetime");
            tenant.FindProperty(nameof(ta_Tenant.cTenantId))!.SetColumnType("char(26)");
            tenant.FindProperty(nameof(ta_Tenant.cTenantParent_cTenantId))!.SetColumnType("char(26)");
         }
         modelBuilder.Entity<ta_Tenant>().HasIndex(x => x.cTenantCode).IsUnique();
         if (Database.IsSqlServer()) foreach (var entity in new[] {
            modelBuilder.Entity<ta_TenantDept>().Metadata, modelBuilder.Entity<vi_TenantDept>().Metadata,
            modelBuilder.Entity<ta_TenantEmp>().Metadata, modelBuilder.Entity<vi_TenantEmp>().Metadata }) {
            entity.FindProperty("ustamp")!.SetColumnType("datetime");
            entity.FindProperty("datestamp")!.SetColumnType("datetime");
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(string) && p.Name.EndsWith("Id")))
               property.SetColumnType("char(26)");
         }

         modelBuilder.Entity<ta_RoleClaim>().HasKey(k => new { k.cRoleId, k.cClaimName });
         modelBuilder.Entity<ta_UserRole>().HasKey(k => new { k.cUserId, k.cRoleId });
         // Declared so EF knows to delete a role's assignments before the role itself within one SaveChanges;
         // without it the batch order is arbitrary and SQL Server rejects the role delete on the foreign key.
         modelBuilder.Entity<ta_UserRole>().HasOne<ta_Role>().WithMany().HasForeignKey(x => x.cRoleId).OnDelete(DeleteBehavior.NoAction);

         modelBuilder.Entity<ta_ApprovalRequest>().HasIndex(x => new { x.cApprovalRequestKind, x.cApprovalName, x.cSubjectKey }).IsUnique()
            .HasFilter("[cApprovalRequestKind] = 2 AND [cApprovalRequestStage] >= -1");
         modelBuilder.Entity<ta_ApprovalRequest>().Property(x => x.cApprovalRequestKind).HasConversion<byte>();
         modelBuilder.Entity<ta_ApprovalRequestStep>().HasIndex(x => new { x.cApprovalRequestId, x.cApprovalRequestStepName }).IsUnique();
         modelBuilder.Entity<ta_ApprovalRequestSignature>().HasIndex(x => x.cApprovalRequestStepId).IsUnique().HasFilter("[cApprovalRequestStepId] IS NOT NULL");
         modelBuilder.Entity<ta_ApprovalRequestSignature>().HasIndex(x => x.cApprovalRequestSignatureCode).IsUnique();
         modelBuilder.Entity<ta_ApprovalSignerSetting>().HasIndex(x => new { x.cApprovalName, x.cApprovalSignerSettingStep }).IsUnique();
         modelBuilder.Entity<ta_ApprovalRequest>().HasOne<ta_ApprovalRequest>().WithMany().HasForeignKey(x => x.cReinstateOf).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestItem>().HasOne<ta_ApprovalRequest>().WithMany().HasForeignKey(x => x.cApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestItem>().HasOne<ta_ApprovalRequestItem>().WithMany().HasForeignKey(x => x.cParentItem).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestItemField>().HasOne<ta_ApprovalRequestItem>().WithMany().HasForeignKey(x => x.cApprovalRequestItemId).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestStep>().HasOne<ta_ApprovalRequest>().WithMany().HasForeignKey(x => x.cApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestStepCandidate>().HasOne<ta_ApprovalRequestStep>().WithMany().HasForeignKey(x => x.cApprovalRequestStepId).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestSignature>().HasOne<ta_ApprovalRequest>().WithMany().HasForeignKey(x => x.cApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestSignature>().HasOne<ta_ApprovalRequestStep>().WithMany().HasForeignKey(x => x.cApprovalRequestStepId).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestComment>().HasOne<ta_ApprovalRequest>().WithMany().HasForeignKey(x => x.cApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestAttachment>().HasOne<ta_ApprovalRequest>().WithMany().HasForeignKey(x => x.cApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalRequestAttachment>().HasOne<ta_ApprovalRequestComment>().WithMany().HasForeignKey(x => x.cApprovalRequestCommentId).OnDelete(DeleteBehavior.NoAction);
         modelBuilder.Entity<ta_ApprovalSignerSettingBackup>().HasOne<ta_ApprovalSignerSetting>().WithMany().HasForeignKey(x => x.cApprovalSignerSettingId).OnDelete(DeleteBehavior.NoAction);
      }
   }
}
