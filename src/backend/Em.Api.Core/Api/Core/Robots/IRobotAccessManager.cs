using Em.Api.Core.Models;

namespace Em.Api.Core;

/// <summary>
/// Register a scoped implementation to add a manager to UserManager's robot access editor.
/// Each manager owns its resources, access codes, grant storage and authorization at login/use.
/// </summary>
public interface IRobotAccessManager
{
   string Id { get; }
   Task<RobotAccessManagerInfo> DescribeAsync(CancellationToken ct);
   Task<RobotAccessInfo[]> ReadAsync(CancellationToken ct);
   Task SetAsync(string robotId, string resourceId, string access, CancellationToken ct);
   /// <summary>
   /// Remove grants/references using the supplied identity context's transaction.
   /// Do not commit it. Failure rolls back the whole identity deletion.
   /// </summary>
   Task PrepareDeleteAsync(RobotContext identities, string robotId, CancellationToken ct);
   /// <summary>Optional file cleanup, called only after the deletion commits.</summary>
   Task AfterDeleteAsync(CancellationToken ct);
}
