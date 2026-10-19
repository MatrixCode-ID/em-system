using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Ui.Wpf.Shared;

/// <summary>Opens the reusable employee editor from any screen through <see cref="EmApp.EmployeeEditorNavigationName"/>.</summary>
public sealed class EmployeeNavigationPayload : NavigationPayloadBase
{
   private EmployeeNavigationPayload(string tenantId, string? employeeId) : base(employeeId ?? "new:" + tenantId) {
      TenantId = tenantId; EmployeeId = employeeId;
      DataState = employeeId is null ? DataState.NewData : DataState.EditData;
   }
   /// <summary>Tenant owning the employee; fixed throughout editing.</summary>
   public string TenantId { get; }
   /// <summary>Employee identity, or null for a new draft.</summary>
   public string? EmployeeId { get; }
   /// <summary>Opens a saved employee in its tenant.</summary>
   public static EmployeeNavigationPayload Open(string tenantId, string employeeId) {
      ArgumentException.ThrowIfNullOrWhiteSpace(tenantId); ArgumentException.ThrowIfNullOrWhiteSpace(employeeId);
      return new(tenantId, employeeId);
   }
   /// <summary>Starts an employee draft for a selected tenant.</summary>
   public static EmployeeNavigationPayload New(string tenantId) {
      ArgumentException.ThrowIfNullOrWhiteSpace(tenantId); return new(tenantId, null);
   }
   /// <inheritdoc />
   public override string? Title => EmployeeId is null ? "New Employee: " + TenantId : "Employee: " + EmployeeId;
}
