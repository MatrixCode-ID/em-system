using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Robot identities and access across all registered managers.</summary>
public interface IRobotServices : IServices
{
   /// <summary>Claim name that manages robots, written without its module name.</summary>
   const string RobotClaim = "User Manager Access";

   /// <summary>Every robot with its grants.</summary>
   Task<RobotInfo[]> GetMeta_Robots();

   /// <summary>Every registered manager that robots can be granted access to, with its resources and access codes.</summary>
   Task<RobotAccessManagerInfo[]> GetMeta_RobotManagers();

   /// <summary>Users eligible to own a robot.</summary>
   Task<RobotOwnerInfo[]> GetMeta_RobotOwners();

   /// <summary>Creates a robot and returns its token; the token is shown only once.</summary>
   Task<RobotToken> PostGetMeta_RobotCreate(string name, string? description, DateTime? tokenExpiry, string? ownerUserId = null);

   /// <summary>Replaces a robot's token and returns the new one; the old token stops working.</summary>
   Task<RobotToken> PostGetMeta_RobotRegenerate(string robotId, DateTime? tokenExpiry);

   /// <summary>Changes the description, active state and token expiry of a robot.</summary>
   Task PostMeta_RobotUpdate(string robotId, string? description, bool isActive, DateTime? tokenExpiry);

   /// <summary>Deletes a robot with its grants.</summary>
   Task PostMeta_RobotDelete(string robotId);
   /// <summary>Empty access revokes the grant. Codes are defined by the selected manager.</summary>
   Task PostMeta_RobotAccessSet(string robotId, string managerId, string resourceId, string access);
}

/// <summary>A robot identity with its grants, without its token.</summary>
public class RobotInfo
{
   /// <summary>Robot ID.</summary>
   public string Id { get; set; } = "";

   /// <summary>Robot name.</summary>
   public string Name { get; set; } = "";

   /// <summary>Optional description.</summary>
   public string? Description { get; set; }

   /// <summary>Owning user, metadata only; it grants no rights.</summary>
   public string? OwnerUserId { get; set; }

   /// <summary>Account name of the owning user.</summary>
   public string? OwnerAccount { get; set; }

   /// <summary><c>false</c> = the robot cannot authenticate.</summary>
   public bool IsActive { get; set; }

   /// <summary>First characters of the token, to recognize it.</summary>
   public string TokenPrefix { get; set; } = "";

   /// <summary>When the token expires; <c>null</c> = never.</summary>
   public DateTime? TokenExpiry { get; set; }

   /// <summary>When the token was last used.</summary>
   public DateTime? TokenLastUsed { get; set; }

   /// <summary>Grants of this robot across all managers.</summary>
   public RobotAccessInfo[] Accesses { get; set; } = [];
}

/// <summary>A robot together with its plain token, returned only when the token is created.</summary>
public class RobotToken
{
   /// <summary>The robot.</summary>
   public RobotInfo Robot { get; set; } = new();

   /// <summary>The plain token. It is not stored and cannot be read again.</summary>
   public string Token { get; set; } = "";
}

/// <summary>One grant of a robot on a resource of a manager.</summary>
public class RobotAccessInfo
{
   /// <summary>Robot ID.</summary>
   public string RobotId { get; set; } = "";

   /// <summary>Manager that defines the resource and access codes.</summary>
   public string ManagerId { get; set; } = "";

   /// <summary>Resource ID within the manager.</summary>
   public string ResourceId { get; set; } = "";

   /// <summary>Access code defined by the manager.</summary>
   public string Access { get; set; } = "";
}

/// <summary>A manager robots can be granted access to.</summary>
public class RobotAccessManagerInfo
{
   /// <summary>Manager ID.</summary>
   public string Id { get; set; } = "";

   /// <summary>Display name.</summary>
   public string Name { get; set; } = "";

   /// <summary>Description.</summary>
   public string Description { get; set; } = "";

   /// <summary>Resources that can be granted.</summary>
   public RobotAccessResourceInfo[] Resources { get; set; } = [];

   /// <summary>Access codes that can be granted.</summary>
   public RobotAccessOptionInfo[] Options { get; set; } = [];
}

/// <summary>A resource of a manager.</summary>
public class RobotAccessResourceInfo
{
   /// <summary>Resource ID.</summary>
   public string Id { get; set; } = "";

   /// <summary>Display name.</summary>
   public string Name { get; set; } = "";
}

/// <summary>An access code of a manager.</summary>
public class RobotAccessOptionInfo
{
   /// <summary>Access code.</summary>
   public string Code { get; set; } = "";

   /// <summary>Display label.</summary>
   public string Label { get; set; } = "";
}

/// <summary>A regular active user eligible to own a robot.</summary>
public class RobotOwnerInfo
{
   /// <summary>User ID.</summary>
   public string Id { get; set; } = "";

   /// <summary>Account name.</summary>
   public string Account { get; set; } = "";
}
