namespace Em.Api.Core.Models
{
   // Deploy of registry containers to Docker servers. All times are UTC. Credentials never leave the
   // server: the info DTOs only carry HasXxx flags.

   /// <summary>How the API reaches the Docker server.</summary>
   public enum CtnDeployKind
   {
      /// <summary>SSH to the Docker host, running the docker CLI there.</summary>
      Ssh = 1,

      /// <summary>The Portainer HTTP API.</summary>
      Portainer = 2
   }

   /// <summary>What is recreated on the Docker server.</summary>
   public enum CtnDeployMode
   {
      /// <summary>One service of a compose project (SSH) or a Portainer stack.</summary>
      Stack = 1,

      /// <summary>A standalone container, recreated with its old settings and the new image.</summary>
      Container = 2
   }

   /// <summary>Credential kind of a deploy target.</summary>
   public enum CtnDeployAuth
   {
      /// <summary>SSH private key in OpenSSH or PEM format, optionally protected by a passphrase.</summary>
      SshKey = 1,

      /// <summary>SSH password.</summary>
      SshPassword = 2,

      /// <summary>Portainer access token (sent as <c>X-API-Key</c>).</summary>
      PortainerToken = 3
   }

   /// <summary>What started a deploy run.</summary>
   public enum CtnDeployTrigger
   {
      /// <summary>The WPF publisher, right after a successful push.</summary>
      AfterPush = 1,

      /// <summary>The Deploy button in Container Manager or Retry deploy in the publisher.</summary>
      Manual = 2,

      /// <summary>Deploying the digest of an earlier successful run again.</summary>
      Rollback = 3
   }

   /// <summary>Outcome of a deploy run.</summary>
   public enum CtnDeployResult
   {
      /// <summary>Still running.</summary>
      Running = 0,

      /// <summary>The image was pulled and the container recreated.</summary>
      Success = 1,

      /// <summary>The run stopped with an error; see the output.</summary>
      Failed = 2,

      /// <summary>Nothing was deployed (no active target, or the tag filter did not match). Not stored in the history.</summary>
      Skipped = 3
   }

   /// <summary>The deploy target of one registry container, without credentials.</summary>
   public class CtnDeployTargetInfo
   {
      /// <summary>Target ID.</summary>
      public string Id { get; set; } = "";

      /// <summary>Registry container this target deploys.</summary>
      public string ImageId { get; set; } = "";

      /// <summary><c>false</c> = automatic deploy after push is off; manual deploy still works.</summary>
      public bool IsActive { get; set; }

      /// <summary>How the API reaches the Docker server.</summary>
      public CtnDeployKind Kind { get; set; }

      /// <summary>What is recreated on the Docker server.</summary>
      public CtnDeployMode Mode { get; set; }

      /// <summary>Comma-separated tag patterns with <c>*</c> and <c>?</c>; empty = every tag.</summary>
      public string? TagFilter { get; set; }

      /// <summary>Registry address the Docker server pulls from, <c>host[:port]</c>.</summary>
      public string RegistryHost { get; set; } = "";

      /// <summary>User name for the registry login on the Docker server; empty = no login.</summary>
      public string? RegistryUser { get; set; }

      /// <summary><c>true</c> when a registry password or token is stored.</summary>
      public bool HasRegistrySecret { get; set; }

      /// <summary>SSH host name, or the base URL of Portainer.</summary>
      public string Host { get; set; } = "";

      /// <summary>SSH port; <c>null</c> = default.</summary>
      public int? Port { get; set; }

      /// <summary>SSH user name.</summary>
      public string? User { get; set; }

      /// <summary>Credential kind.</summary>
      public CtnDeployAuth Auth { get; set; }

      /// <summary><c>true</c> when a key, password or token is stored.</summary>
      public bool HasSecret { get; set; }

      /// <summary><c>true</c> when a passphrase for the SSH key is stored.</summary>
      public bool HasPassphrase { get; set; }

      /// <summary>Pinned SSH host key (<c>SHA256:...</c>) or TLS certificate fingerprint; empty = not pinned.</summary>
      public string? Fingerprint { get; set; }

      /// <summary>Portainer environment (endpoint) id.</summary>
      public int? EndpointId { get; set; }

      /// <summary>Compose folder on the SSH host, or the Portainer stack name.</summary>
      public string? Stack { get; set; }

      /// <summary>Portainer stack id.</summary>
      public int? StackId { get; set; }

      /// <summary>Compose service whose image is replaced (Stack mode).</summary>
      public string? Service { get; set; }

      /// <summary>Container name (Container mode).</summary>
      public string? Container { get; set; }

      /// <summary>Variable holding the image in the compose file; empty = <c>EM_IMAGE_&lt;SERVICE&gt;</c>.</summary>
      public string? ImageVariable { get; set; }

      /// <summary>Latest run, or <c>null</c> when the target never ran.</summary>
      public CtnDeployRunInfo? LastRun { get; set; }

      /// <summary>Last change of this target (UTC).</summary>
      public DateTime UpdatedAt { get; set; }
   }

   /// <summary>
   /// Create or update the deploy target of a container, or the input of a connection test. For
   /// <see cref="Secret"/>, <see cref="Passphrase"/> and <see cref="RegistrySecret"/>: <c>null</c> keeps the
   /// stored value, an empty string removes it, and any other value replaces it.
   /// </summary>
   public class CtnDeployTargetSave
   {
      /// <summary>Registry container this target deploys.</summary>
      public string ImageId { get; set; } = "";

      /// <summary><c>false</c> = automatic deploy after push is off.</summary>
      public bool IsActive { get; set; } = true;

      /// <summary>How the API reaches the Docker server.</summary>
      public CtnDeployKind Kind { get; set; } = CtnDeployKind.Ssh;

      /// <summary>What is recreated on the Docker server.</summary>
      public CtnDeployMode Mode { get; set; } = CtnDeployMode.Stack;

      /// <summary>Comma-separated tag patterns with <c>*</c> and <c>?</c>; empty = every tag.</summary>
      public string? TagFilter { get; set; }

      /// <summary>Registry address the Docker server pulls from, <c>host[:port]</c>.</summary>
      public string RegistryHost { get; set; } = "";

      /// <summary>User name for the registry login on the Docker server.</summary>
      public string? RegistryUser { get; set; }

      /// <summary>Registry password or token; see the class remarks for <c>null</c> and empty.</summary>
      public string? RegistrySecret { get; set; }

      /// <summary>SSH host name, or the base URL of Portainer.</summary>
      public string Host { get; set; } = "";

      /// <summary>SSH port; <c>null</c> = default.</summary>
      public int? Port { get; set; }

      /// <summary>SSH user name.</summary>
      public string? User { get; set; }

      /// <summary>Credential kind.</summary>
      public CtnDeployAuth Auth { get; set; } = CtnDeployAuth.SshKey;

      /// <summary>SSH key, SSH password or Portainer token; see the class remarks for <c>null</c> and empty.</summary>
      public string? Secret { get; set; }

      /// <summary>Passphrase of the SSH key; see the class remarks for <c>null</c> and empty.</summary>
      public string? Passphrase { get; set; }

      /// <summary>Pinned SSH host key or TLS certificate fingerprint; empty = not pinned.</summary>
      public string? Fingerprint { get; set; }

      /// <summary>Portainer environment (endpoint) id.</summary>
      public int? EndpointId { get; set; }

      /// <summary>Compose folder on the SSH host, or the Portainer stack name.</summary>
      public string? Stack { get; set; }

      /// <summary>Portainer stack id.</summary>
      public int? StackId { get; set; }

      /// <summary>Compose service whose image is replaced (Stack mode).</summary>
      public string? Service { get; set; }

      /// <summary>Container name (Container mode).</summary>
      public string? Container { get; set; }

      /// <summary>Variable holding the image in the compose file; empty = <c>EM_IMAGE_&lt;SERVICE&gt;</c>.</summary>
      public string? ImageVariable { get; set; }
   }

   /// <summary>A Portainer environment (endpoint).</summary>
   public class CtnDeployEndpoint
   {
      /// <summary>Endpoint id.</summary>
      public int Id { get; set; }

      /// <summary>Endpoint name.</summary>
      public string Name { get; set; } = "";
   }

   /// <summary>A Portainer stack.</summary>
   public class CtnDeployStack
   {
      /// <summary>Stack id.</summary>
      public int Id { get; set; }

      /// <summary>Stack name.</summary>
      public string Name { get; set; } = "";

      /// <summary>Environment (endpoint) the stack belongs to.</summary>
      public int EndpointId { get; set; }

      /// <summary><c>true</c> for a stack deployed from Git: its file cannot be rewritten.</summary>
      public bool IsGit { get; set; }
   }

   /// <summary>Result of a connection test. Nothing is deployed or changed on the server.</summary>
   public class CtnDeployTestResult
   {
      /// <summary><c>true</c> when every check passed.</summary>
      public bool Success { get; set; }

      /// <summary>What was checked, one line each, in order.</summary>
      public string[] Messages { get; set; } = [];

      /// <summary>
      /// Fingerprint the server offered while none (or another one) is pinned. The user confirms it, the
      /// dialog stores it in the target, and the test runs again.
      /// </summary>
      public string? OfferedFingerprint { get; set; }

      /// <summary>Portainer environments, for the endpoint picker.</summary>
      public CtnDeployEndpoint[] Endpoints { get; set; } = [];

      /// <summary>Portainer stacks of the selected environment.</summary>
      public CtnDeployStack[] Stacks { get; set; } = [];

      /// <summary>Services of the selected compose file or stack.</summary>
      public string[] Services { get; set; } = [];

      /// <summary>Container names on the Docker server (Container mode).</summary>
      public string[] Containers { get; set; } = [];

      /// <summary><c>true</c> when the image of the selected service already uses the image variable.</summary>
      public bool ImageVariablePresent { get; set; }

      /// <summary><c>true</c> when <c>docker login</c> to the registry host succeeded on the SSH host.</summary>
      public bool RegistryLoginOk { get; set; }

      /// <summary><c>true</c> when Portainer has no registry for the registry host; offer Register in Portainer.</summary>
      public bool PortainerRegistryMissing { get; set; }
   }

   /// <summary>What the publisher pushed: sent to the server right after a successful push.</summary>
   public class CtnDeployPushRequest
   {
      /// <summary>Pull name without host: <c>root/name</c>.</summary>
      public string Repository { get; set; } = "";

      /// <summary>Every tag pushed for this digest: the version tag plus the floating tags that succeeded.</summary>
      public string[] Tags { get; set; } = [];

      /// <summary>Manifest digest that was pushed.</summary>
      public string Digest { get; set; } = "";
   }

   /// <summary>One deploy run.</summary>
   public class CtnDeployRunInfo
   {
      /// <summary>Run id; empty for a <see cref="CtnDeployResult.Skipped"/> answer, which is not stored.</summary>
      public string Id { get; set; } = "";

      /// <summary>Registry container that was deployed.</summary>
      public string ImageId { get; set; } = "";

      /// <summary>What started the run.</summary>
      public CtnDeployTrigger Trigger { get; set; }

      /// <summary>Tag that was deployed, if known.</summary>
      public string? Tag { get; set; }

      /// <summary>Manifest digest that was deployed.</summary>
      public string Digest { get; set; } = "";

      /// <summary>Digest deployed before this run, when it could be read.</summary>
      public string? PrevDigest { get; set; }

      /// <summary>Outcome of the run.</summary>
      public CtnDeployResult Result { get; set; }

      /// <summary>Step log of the run, or the reason it was skipped. Never contains credentials.</summary>
      public string? Output { get; set; }

      /// <summary>Account of the user who started the run.</summary>
      public string? By { get; set; }

      /// <summary>When the run started (UTC).</summary>
      public DateTime Started { get; set; }

      /// <summary>When the run finished (UTC); <c>null</c> while running.</summary>
      public DateTime? Finished { get; set; }

      /// <summary><c>true</c> for a successful run that is not the latest successful one.</summary>
      public bool CanRollback { get; set; }
   }
}
