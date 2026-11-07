using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// UI model of one tenant employee. A wrapper module derives from it to lift its own fields out of
   /// <c>json_object</c>: read them with <c>GetJson</c> in <see cref="ReadFrom"/> and write them in
   /// <see cref="BuildJson"/>, always after calling <c>base</c>. Fields a model does not know are preserved.
   /// <para>
   /// The employee actions need the Business Module Manager claim over HTTP. A wrapper with a claim of its own
   /// overrides <see cref="FetchAsync"/>, <see cref="UpdateAsync"/> and <see cref="InsertAsync"/> to go through its own
   /// service, and finishes both saves with <see cref="ApplySaved"/>.
   /// </para>
   /// </summary>
   public class TenantEmp : UiModel<vi_TenantEmp, ITenantEmpServices>
   {
      #region Statics

      /// <summary>Gets one employee by its id, or <c>null</c> when it does not exist.</summary>
      public static async Task<TenantEmp?> GetTenantEmp_ByIdAsync(IEmApp app, string tenantEmpId) {
         var data = await app.ServiceProvider.GetRequiredService<ITenantEmpServices>().GetVi_TenantEmp_ById(tenantEmpId);
         return data != null ? new TenantEmp(app, data) : null;
      }

      /// <summary>Wraps an employee view row in a model.</summary>
      public static TenantEmp Build(IEmApp app, vi_TenantEmp data) => new(app, data);

      /// <summary>Creates an unsaved employee of a tenant. It reaches the server on the first save.</summary>
      public static TenantEmp NewBlank(IEmApp app, string tenantId) => new(app, new vi_TenantEmp { cTenantId = tenantId }, true);

      #endregion

      /// <summary>Creates the model from a view row.</summary>
      /// <param name="app">The application object.</param>
      /// <param name="data">The row that fills the model.</param>
      /// <param name="blank">Whether the row has never been saved.</param>
      protected TenantEmp(IEmApp app, vi_TenantEmp data, bool blank = false) : base(app, data) {
         if (!blank) return;
         IsBlank = true;
         IsDirty = false;
      }

      #region Properties

      /// <summary>Id of the employee; empty until the first save.</summary>
      public string cTenantEmpId { get; private set => SetField(ref field, value); } = "";
      /// <summary>Tenant of the employee; cannot change after creation.</summary>
      public string cTenantId { get; private set => SetField(ref field, value); } = "";
      /// <summary>Person of the employee; cannot change after creation.</summary>
      public string cContactId { get; set => SetField(ref field, value); } = "";
      /// <summary>Optional department in the same tenant.</summary>
      public string? cTenantDeptId { get; set => SetField(ref field, value); }
      /// <summary>Employee number, unique within the tenant.</summary>
      public string cTenantEmpCode { get; set => SetField(ref field, value); } = "";
      /// <summary>Optional position in the same tenant.</summary>
      public string? cTenantPositionId { get; set => SetField(ref field, value); }
      /// <summary>Active or inactive employment state.</summary>
      public int cTenantEmpState { get; set => SetField(ref field, value); }
      /// <summary>Optional administrator note.</summary>
      public string? cTenantEmpNote { get; set => SetField(ref field, value); }
      /// <summary>Full name of the person; read-only.</summary>
      public string cvContactFullName { get; private set => SetField(ref field, value); } = "";
      /// <summary>Type of the person; read-only.</summary>
      public ContactType cvContactType { get; private set => SetField(ref field, value); }
      /// <summary>Department code; read-only.</summary>
      public string? cvTenantDeptCode { get; private set => SetField(ref field, value); }
      /// <summary>Department name; read-only.</summary>
      public string? cvTenantDeptName { get; private set => SetField(ref field, value); }
      /// <summary>Position code; read-only.</summary>
      public string? cvTenantPositionCode { get; private set => SetField(ref field, value); }
      /// <summary>Position name; read-only.</summary>
      public string? cvTenantPositionName { get; private set => SetField(ref field, value); }
      /// <summary>Position state; read-only.</summary>
      public int? cvTenantPositionState { get; private set => SetField(ref field, value); }
      /// <summary>Whether the employee heads the department; read-only.</summary>
      public bool ccTenantEmpIsDeptHead { get; private set => SetField(ref field, value); }
      /// <summary>Active user accounts of the person; read-only.</summary>
      public string? cvTenantEmpUserAccounts { get; private set => SetField(ref field, value); }

      /// <summary>Contact details to save with the employee. Null keeps the details of an existing person.</summary>
      public TenantEmpPerson? Person { get; set; }
      /// <summary>Name of the new person when <see cref="cContactId"/> is empty.</summary>
      public string? NewContactName { get; set; }
      /// <summary>Type of the new person when <see cref="cContactId"/> is empty.</summary>
      public ContactType NewContactType { get; set; } = ContactType.Male;
      /// <summary>Whether the employee heads its department after the save; null leaves the head unchanged.</summary>
      public bool? IsDeptHead { get; set; }

      #endregion

      #region Methods

      /// <inheritdoc />
      protected override void ReadFrom(vi_TenantEmp source) {
         cTenantEmpId = source.cTenantEmpId;
         cTenantId = source.cTenantId;
         cContactId = source.cContactId;
         cTenantDeptId = source.cTenantDeptId;
         cTenantEmpCode = source.cTenantEmpCode;
         cTenantPositionId = source.cTenantPositionId;
         cTenantEmpState = source.cTenantEmpState;
         cTenantEmpNote = source.cTenantEmpNote;
         cvContactFullName = source.cvContactFullName;
         cvContactType = source.cvContactType;
         cvTenantDeptCode = source.cvTenantDeptCode;
         cvTenantDeptName = source.cvTenantDeptName;
         cvTenantPositionCode = source.cvTenantPositionCode;
         cvTenantPositionName = source.cvTenantPositionName;
         cvTenantPositionState = source.cvTenantPositionState;
         ccTenantEmpIsDeptHead = source.ccTenantEmpIsDeptHead;
         cvTenantEmpUserAccounts = source.cvTenantEmpUserAccounts;
         ustamp = source.ustamp;
         datestamp = source.datestamp;
         json_object = source.json_object;
      }

      /// <inheritdoc />
      protected override void WriteTo(vi_TenantEmp target) {
         target.cTenantEmpId = cTenantEmpId;
         target.cTenantId = cTenantId;
         target.cContactId = cContactId;
         target.cTenantDeptId = cTenantDeptId;
         target.cTenantEmpCode = cTenantEmpCode;
         target.cTenantPositionId = cTenantPositionId;
         target.cTenantEmpState = cTenantEmpState;
         target.cTenantEmpNote = cTenantEmpNote;
         target.cvContactFullName = cvContactFullName;
         target.cvContactType = cvContactType;
         target.cvTenantDeptCode = cvTenantDeptCode;
         target.cvTenantDeptName = cvTenantDeptName;
         target.cvTenantPositionCode = cvTenantPositionCode;
         target.cvTenantPositionName = cvTenantPositionName;
         target.cvTenantPositionState = cvTenantPositionState;
         target.ccTenantEmpIsDeptHead = ccTenantEmpIsDeptHead;
         target.cvTenantEmpUserAccounts = cvTenantEmpUserAccounts;
         // SaveAsync refills ustamp with server time, while the employee save compares the stamp last read.
         target.ustamp = Original.ustamp;
         target.datestamp = datestamp;
         target.json_object = json_object;
      }

      /// <inheritdoc />
      protected override JsonObject BuildJson(JsonObject patch) => patch;

      /// <inheritdoc />
      protected override Task<vi_TenantEmp?> FetchAsync() => Service.GetVi_TenantEmp_ById(cTenantEmpId);

      /// <inheritdoc />
      protected override async Task UpdateAsync(vi_TenantEmp entity) =>
         ApplySaved(entity, await Service.PostGetMeta_TenantEmpSave(BuildSave(entity)));

      /// <inheritdoc />
      protected override async Task InsertAsync(vi_TenantEmp entity) =>
         ApplySaved(entity, await Service.PostGetMeta_TenantEmpSave(BuildSave(entity)));

      /// <summary>Builds the save request of a mapped row from <see cref="Person"/> and the new person fields.</summary>
      protected TenantEmpSave BuildSave(vi_TenantEmp entity) => new() {
         Row = entity, Person = Person, NewContactName = NewContactName, NewContactType = NewContactType, IsDeptHead = IsDeptHead
      };

      /// <summary>
      /// Copies the row returned by a save to the row being saved and refreshes the model from it, so the next
      /// save carries the new stamp. Overrides of <see cref="UpdateAsync"/> and <see cref="InsertAsync"/> must call it.
      /// </summary>
      /// <param name="entity">The row received by the override.</param>
      /// <param name="saved">The row the server returned.</param>
      protected void ApplySaved(vi_TenantEmp entity, vi_TenantEmp saved) {
         entity.cTenantEmpId = saved.cTenantEmpId;
         entity.cContactId = saved.cContactId;
         entity.cvContactFullName = saved.cvContactFullName;
         entity.cvContactType = saved.cvContactType;
         entity.cvTenantDeptCode = saved.cvTenantDeptCode;
         entity.cvTenantDeptName = saved.cvTenantDeptName;
         entity.cvTenantPositionCode = saved.cvTenantPositionCode;
         entity.cvTenantPositionName = saved.cvTenantPositionName;
         entity.cvTenantPositionState = saved.cvTenantPositionState;
         entity.ccTenantEmpIsDeptHead = saved.ccTenantEmpIsDeptHead;
         entity.cvTenantEmpUserAccounts = saved.cvTenantEmpUserAccounts;
         entity.ustamp = saved.ustamp;
         entity.datestamp = saved.datestamp;
         entity.json_object = saved.json_object;
         Load(() => ReadFrom(entity));
      }

      #endregion
   }
}
