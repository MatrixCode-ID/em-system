using Em.Api.Core;
using Em.Shared;
using Em.Api.Core.Models;
using Em.Api.Core.ObjectStorage;
using Em.Api.Core.ObjectStorage.Volumes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Em.Api.Shared;

public partial class EmAppBuilder
{
   private bool _objectStorageAdded;
   internal bool ObjectStorageAdded => _objectStorageAdded;

   /// <summary>Managed buckets declared with <see cref="AddManagedBucket"/>; handed to object storage when it is turned on.</summary>
   internal ManagedBucketRegistry ManagedBuckets { get; } = new();

   /// <summary>
   /// Turns on object storage: the <see cref="IObjectStore"/> service for modules, the management actions of
   /// <see cref="IObjectStorageServices"/>, background maintenance and the public <c>/store</c> path for
   /// public-read buckets. Volumes, policies and buckets are kept in the database and managed from the
   /// Object Storage Manager screen; changes apply without a restart.
   /// </summary>
   /// <remarks>
   /// Requires <c>doc/sqlscript/mssql/tables/060-objectstore.sql</c> and the <c>views/vi_Store*.sql</c> files
   /// on the core database (SQL Server); startup fails without them. Assumes one API instance per database and
   /// volume set. Guide: <c>doc/engine/engine-object-storage.md</c>.
   /// </remarks>
   public void AddObjectStorage() => AddObjectStorage(_ => { });

   /// <summary>
   /// Turns on object storage like <see cref="AddObjectStorage()"/> with host options, such as the folder of the
   /// volume created for managed buckets (<see cref="ObjectStorageOptions.DefaultVolumePath"/>).
   /// </summary>
   /// <param name="configure">Sets the options.</param>
   public void AddObjectStorage(Action<ObjectStorageOptions> configure) {
      ArgumentNullException.ThrowIfNull(configure);
      if (_objectStorageAdded) throw new InvalidOperationException("AddObjectStorage may only be called once.");
      var options = new ObjectStorageOptions();
      configure(options);
      if (string.IsNullOrWhiteSpace(options.DefaultVolumePath)) options.DefaultVolumePath = ObjectStorageOptions.DefaultVolumeFallback;
      _objectStorageAdded = true;
      AddDbContext<ObjectStorageDbContext>();
      Services.AddSingleton<IStoreVolumeProvider>(sp => new LocalFolderVolumeProvider(sp.GetRequiredService<IHostEnvironment>().ContentRootPath));
      Services.AddSingleton(options);
      Services.AddSingleton(sp => new ObjectStorageRuntime(sp.GetServices<IStoreVolumeProvider>()) { ManagedBuckets = this.ManagedBuckets, Options = options });
      Services.AddScoped<ObjectStore>();
      Services.AddScoped<IObjectStore, SwitchedObjectStore>();
      Services.AddScoped<ObjectStorageMaintenance>();
      AddService<IObjectStorageServices, ObjectStorageServices>();
      AddClaims(ClaimAction.Create<ObjectStorageServices>(IObjectStorageServices.ManagerClaim),
         ClaimAction.Create<ObjectStorageServices>(IObjectStorageServices.SettingsClaim));
      Services.AddHostedService<ObjectStorageBackground>();
      AddPublicEndpoint(ObjectStorageEndpoint.PublicRequestPath, ObjectStorageEndpoint.HandleAsync);
   }

   /// <summary>
   /// Declares a managed bucket: a bucket owned by an application module instead of an administrator. At startup
   /// object storage creates the bucket, with its own policy built from <paramref name="configure"/> on the default
   /// volume (<see cref="ObjectStorageOptions.DefaultVolumePath"/>), or adopts an existing bucket of the same name
   /// with its contents. The module then reads and writes it through <see cref="IObjectStore"/> without any setup
   /// by an administrator.
   /// </summary>
   /// <remarks>
   /// <para>
   /// Administrators keep the infrastructure: volumes, replicas, retention, quotas, maximum object size, state and
   /// notes. They can browse and download the contents, but cannot upload, delete, restore or purge objects, move
   /// the bucket to another policy, change its access or delete it. The policy values given here are only used when
   /// the policy is created; the access is enforced at every startup, and the description is updated when it changes.
   /// </para>
   /// <para>
   /// Declarations may come before or after <see cref="AddObjectStorage()"/> and are ignored when object storage is
   /// not turned on. A bucket that stops being declared stays locked and is marked "not declared" until an
   /// administrator releases it. Guide: <c>doc/engine/engine-object-storage.md</c>, section "Managed buckets".
   /// </para>
   /// </remarks>
   /// <param name="name">Bucket name (3–63 lowercase letters, digits, <c>-</c> and <c>.</c>).</param>
   /// <param name="owner">Application or module that owns the bucket (1–100 characters), shown as "Managed by ...".</param>
   /// <param name="description">
   /// Written for administrators (10–500 characters): what the bucket holds, which module fills it, and what changing
   /// its volumes or quotas affects. It is shown in Object Storage Manager and cannot be edited there.
   /// </param>
   /// <param name="configure">Starting values of the bucket's policy; access defaults to private.</param>
   /// <exception cref="ArgumentException">The name, owner, description or a policy value is invalid.</exception>
   /// <exception cref="InvalidOperationException">The bucket name is already declared.</exception>
   public void AddManagedBucket(string name, string owner, string description, Action<ManagedBucketOptions>? configure = null) {
      var defaults = new ManagedBucketOptions();
      configure?.Invoke(defaults);
      ManagedBuckets.Add(ManagedBucketDeclaration.Create(name, owner, description, defaults));
   }
}
