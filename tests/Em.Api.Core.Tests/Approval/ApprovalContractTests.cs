using Em.Api.Core.Approval;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Core.Tests.Approval;

public sealed class ApprovalContractTests {
   [Theory]
   [InlineData("Customer", true)]
   [InlineData("A1 B2", true)]
   [InlineData(" A", false)]
   [InlineData("A  B", false)]
   [InlineData("A-B", false)]
   [InlineData("1A", false)]
   [InlineData("É", false)]
   [InlineData("", false)]
   public void NameContract_IsStrictAscii(string name, bool expected) => Assert.Equal(expected, ApprovalSubjectKey.IsValidName(name));

   [Fact]
   public void SubjectSerialization_PreservesStringsAndCompositeOrder() {
      Assert.Equal("plain", ApprovalSubjectKey.Serialize("plain"));
      Assert.Equal("{\"Number\":\"001\",\"Issue\":2}", ApprovalSubjectKey.Serialize(new { Number = "001", Issue = 2 }));
      Assert.Throws<ArgumentException>(() => ApprovalSubjectKey.Serialize(new string('a', 401)));
      Assert.False(ApprovalSubjectKey.IsValidName(new string('a', 51)));
   }

   [Fact]
   public void Values_RoundTripWithoutCultureOrDecimalScaleConflicts() {
      Assert.Equal("12.5", ApprovalValue.Normalize(12.5000m));
      Assert.Equal(12.5m, ApprovalValue.Parse("12.5", typeof(decimal?)));
      Assert.Equal("true", ApprovalValue.Normalize(true));
      Assert.Equal(ApprovalStage.Cancelled, ApprovalValue.Parse("-2", typeof(ApprovalStage)));
      var date = new DateTime(2026, 10, 17, 9, 10, 11, DateTimeKind.Utc).AddTicks(1234);
      Assert.Equal(date, ApprovalValue.Parse(ApprovalValue.Normalize(date), typeof(DateTime)));
      Assert.Null(ApprovalValue.Parse(null, typeof(string)));
   }

   [Fact]
   public void Registry_RejectsDuplicateSystemNamesWithoutOpeningDatabase() {
      var services = new ServiceCollection().AddLogging().BuildServiceProvider();
      var registry = new ApprovalRegistry([], [new(typeof(First), new First(), null), new(typeof(Second), new Second(), null)], [], _ => { });
      Assert.Contains("Duplicate System", Assert.Throws<InvalidOperationException>(() => registry.Initialize(services)).Message);
   }

   [Fact]
   public void Registry_RejectsMismatchedCompositeKeyOrder() {
      var services = new ServiceCollection().AddLogging();
      services.AddDbContext<KeyContext>(o => o.UseSqlServer("Server=unopened.invalid;Database=Test;Integrated Security=True;TrustServerCertificate=True"));
      services.AddScoped<WrongKey>();
      using var provider = services.BuildServiceProvider();
      var registry = new ApprovalRegistry([new(typeof(WrongKey), typeof(Row), null)], [], [typeof(KeyContext)], _ => { });
      Assert.Contains("complete EF primary key", Assert.Throws<InvalidOperationException>(() => registry.Initialize(provider)).Message);
   }

   [Fact]
   public void Registry_RequiresDeclaredOwnerWhenSharedEntityIsAmbiguous() {
      using var provider = SharedRowProvider();
      var registry = new ApprovalRegistry([new(typeof(RightKey), typeof(Row), null)], [], [typeof(KeyContext), typeof(OtherContext)], _ => { });
      Assert.Contains("AddDataApproval<TDefinition, TContext>", Assert.Throws<InvalidOperationException>(() => registry.Initialize(provider)).Message);
   }

   [Fact]
   public void Registry_UsesDeclaredOwnerForSharedEntity() {
      using var provider = SharedRowProvider();
      var registration = new DataRegistration(typeof(RightKey), typeof(Row), null, typeof(OtherContext));
      new ApprovalRegistry([registration], [], [typeof(KeyContext), typeof(OtherContext)], _ => { }).Initialize(provider);
      Assert.Equal(typeof(OtherContext), registration.ContextType);
   }

   private static ServiceProvider SharedRowProvider() {
      const string unopened = "Server=unopened.invalid;Database=Test;Integrated Security=True;TrustServerCertificate=True";
      var services = new ServiceCollection().AddLogging();
      services.AddDbContext<KeyContext>(o => o.UseSqlServer(unopened));
      services.AddDbContext<OtherContext>(o => o.UseSqlServer(unopened));
      services.AddScoped<RightKey>();
      return services.BuildServiceProvider();
   }

   [Approval("Same")]
   public sealed class First : IEmApprovalSystem<string> { public IReadOnlyList<ApprovalSystemStep> Steps => [new(1, "Sign")]; }
   [Approval("same")]
   public sealed class Second : IEmApprovalSystem<string> { public IReadOnlyList<ApprovalSystemStep> Steps => [new(1, "Sign")]; }
   public sealed class Row { public string A { get; set; } = ""; public string B { get; set; } = ""; public string Text { get; set; } = ""; }
   public sealed class KeyContext(DbContextOptions<KeyContext> options) : DbContext(options) {
      protected override void OnModelCreating(ModelBuilder b) => b.Entity<Row>().HasKey(x => new { x.A, x.B });
   }
   public sealed class OtherContext(DbContextOptions<OtherContext> options) : DbContext(options) {
      protected override void OnModelCreating(ModelBuilder b) => b.Entity<Row>().HasKey(x => new { x.A, x.B });
   }
   [Approval("Right Key")]
   public sealed class RightKey : IEmApprovalData<Row> {
      public string[] KeyColumns => [nameof(Row.A), nameof(Row.B)];
      public string[] TrackedColumns => [nameof(Row.Text)];
   }
   [Approval("Wrong Key")]
   public sealed class WrongKey : IEmApprovalData<Row> {
      public string[] KeyColumns => [nameof(Row.B), nameof(Row.A)];
      public string[] TrackedColumns => [nameof(Row.Text)];
   }
}
