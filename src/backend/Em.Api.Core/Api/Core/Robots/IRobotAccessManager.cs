using Em.Api.Core.Models;

namespace Em.Api.Core;

/// <summary>
/// Register a scoped implementation to add a manager to UserManager's robot access editor.
/// Each manager owns its resources, access codes, grant storage and authorization at login/use.
/// </summary>
public interface IRobotAccessManager
{
   /// <summary>Identifier of the manager that owns the resources, e.g. its module.</summary>
   string Id { get; }
   /// <summary>Describes the manager and the resources a robot can be given access to.</summary>
   Task<RobotAccessManagerInfo> DescribeAsync(CancellationToken ct);
   /// <summary>Reads every access grant of this manager.</summary>
   Task<RobotAccessInfo[]> ReadAsync(CancellationToken ct);
   /// <summary>Sets the access of a robot to one resource; empty access removes the grant.</summary>
   Task SetAsync(string robotId, string resourceId, string access, CancellationToken ct);
   /// <summary>
   /// Remove grants/references using the supplied identity context's transaction.
   /// Do not commit it. Failure rolls back the whole identity deletion.
   /// </summary>
   Task PrepareDeleteAsync(RobotContext identities, string robotId, CancellationToken ct);
   /// <summary>Optional file cleanup, called only after the deletion commits.</summary>
   Task AfterDeleteAsync(CancellationToken ct);
}
