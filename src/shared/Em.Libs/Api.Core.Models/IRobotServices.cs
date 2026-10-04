using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Robot identities and access across all registered managers.</summary>
public interface IRobotServices : IServices
{
   const string RobotClaim = "User Manager Access";
   Task<RobotInfo[]> GetMeta_Robots();
   Task<RobotAccessManagerInfo[]> GetMeta_RobotManagers();
   Task<RobotOwnerInfo[]> GetMeta_RobotOwners();
   Task<RobotToken> PostGetMeta_RobotCreate(string name, string? description, DateTime? tokenExpiry, string? ownerUserId = null);
   Task<RobotToken> PostGetMeta_RobotRegenerate(string robotId, DateTime? tokenExpiry);
   Task PostMeta_RobotUpdate(string robotId, string? description, bool isActive, DateTime? tokenExpiry);
   Task PostMeta_RobotDelete(string robotId);
   /// <summary>Empty access revokes the grant. Codes are defined by the selected manager.</summary>
   Task PostMeta_RobotAccessSet(string robotId, string managerId, string resourceId, string access);
}

public class RobotInfo
{
   public string Id { get; set; } = "";
   public string Name { get; set; } = "";
   public string? Description { get; set; }
   public string? OwnerUserId { get; set; }
   public string? OwnerAccount { get; set; }
   public bool IsActive { get; set; }
   public string TokenPrefix { get; set; } = "";
   public DateTime? TokenExpiry { get; set; }
   public DateTime? TokenLastUsed { get; set; }
   public RobotAccessInfo[] Accesses { get; set; } = [];
}

public class RobotToken
{
   public RobotInfo Robot { get; set; } = new();
   public string Token { get; set; } = "";
}

public class RobotAccessInfo
{
   public string RobotId { get; set; } = "";
   public string ManagerId { get; set; } = "";
   public string ResourceId { get; set; } = "";
   public string Access { get; set; } = "";
}

public class RobotAccessManagerInfo
{
   public string Id { get; set; } = "";
   public string Name { get; set; } = "";
   public string Description { get; set; } = "";
   public RobotAccessResourceInfo[] Resources { get; set; } = [];
   public RobotAccessOptionInfo[] Options { get; set; } = [];
}

public class RobotAccessResourceInfo
{
   public string Id { get; set; } = "";
   public string Name { get; set; } = "";
}

public class RobotAccessOptionInfo
{
   public string Code { get; set; } = "";
   public string Label { get; set; } = "";
}

/// <summary>A regular active user eligible to own a robot.</summary>
public class RobotOwnerInfo
{
   public string Id { get; set; } = "";
   public string Account { get; set; } = "";
}
