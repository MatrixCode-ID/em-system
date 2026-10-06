using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Registry
{
   // Semua entitas registry internal: baris-barisnya tidak pernah keluar dari server apa adanya, yang
   // keluar adalah DTO di Em.Libs. Dipetakan lewat CtnContext.OnModelCreating, bukan DbSet publik.
   // Kolom mengikuti doc/convention/dahlia-convention.md; kunci gabungan didaftarkan di CtnContext.

   [Table("ta_CtnRoot")]
   internal class ta_CtnRoot
   {
      [Key] public string cCtnRootId { get; set; } = string.Empty;
      public string cCtnRootName { get; set; } = string.Empty;
      public int cCtnRootState { get; set; }
      public string? cCtnRootDescription { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
      public string? json_object { get; set; }
   }

   [Table("ta_CtnFolder")]
   internal class ta_CtnFolder
   {
      [Key] public string cCtnFolderId { get; set; } = string.Empty;
      public string cCtnRootId { get; set; } = string.Empty;
      public string? cCtnFolderParent_cCtnFolderId { get; set; }
      public string cCtnFolderName { get; set; } = string.Empty;
      public int cCtnFolderOrder { get; set; } = -1;
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
      public string? json_object { get; set; }
   }

   [Table("ta_CtnImage")]
   internal class ta_CtnImage
   {
      [Key] public string cCtnImageId { get; set; } = string.Empty;
      public string cCtnRootId { get; set; } = string.Empty;
      public string? cCtnFolderId { get; set; }
      public string cCtnImageName { get; set; } = string.Empty;
      public int cCtnImageState { get; set; }
      public string? cCtnImageDescription { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
      public string? json_object { get; set; }
   }

   [Table("ta_CtnManifest")]
   internal class ta_CtnManifest
   {
      [Key] public string cCtnManifestId { get; set; } = string.Empty;
      public string cCtnImageId { get; set; } = string.Empty;
      public string cCtnManifestDigest { get; set; } = string.Empty;
      public string cCtnManifestMediaType { get; set; } = string.Empty;
      public long cCtnManifestSize { get; set; }

      // Byte persis seperti yang dikirim: digest dihitung dari byte-nya, diserialisasi ulang mengubahnya.
      public byte[] cCtnManifestContent { get; set; } = [];
      public string? cCtnManifestPushedBy_cRobotId { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
      public string? json_object { get; set; }
   }

   [Table("ta_CtnTag")]
   internal class ta_CtnTag
   {
      public string cCtnImageId { get; set; } = string.Empty;
      public string cCtnTagName { get; set; } = string.Empty;
      public string cCtnManifestId { get; set; } = string.Empty;
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
   }

   [Table("ta_CtnBlob")]
   internal class ta_CtnBlob
   {
      [Key] public string cCtnBlobId { get; set; } = string.Empty;
      public string cCtnBlobDigest { get; set; } = string.Empty;
      public long cCtnBlobSize { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
      public string? json_object { get; set; }
   }

   [Table("ta_CtnBlobLink")]
   internal class ta_CtnBlobLink
   {
      public string cCtnImageId { get; set; } = string.Empty;
      public string cCtnBlobId { get; set; } = string.Empty;
      public DateTime datestamp { get; set; }
   }

   [Table("ta_CtnManifestBlob")]
   internal class ta_CtnManifestBlob
   {
      public string cCtnManifestId { get; set; } = string.Empty;
      public int cCtnManifestBlobOrder { get; set; }
      public string cCtnBlobId { get; set; } = string.Empty;

      // "config" atau "layer".
      public string cCtnManifestBlobRole { get; set; } = string.Empty;
   }

   [Table("ta_CtnUpload")]
   internal class ta_CtnUpload
   {
      [Key] public string cCtnUploadId { get; set; } = string.Empty;
      public string cCtnImageId { get; set; } = string.Empty;
      public string cRobotId { get; set; } = string.Empty;

      // Offset: berapa byte yang sudah diterima.
      public long cCtnUploadSize { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
      public string? json_object { get; set; }
   }

   [Table("ta_CtnRootRobot")]
   internal class ta_CtnRootRobot
   {
      public string cRobotId { get; set; } = string.Empty;
      public string cCtnRootId { get; set; } = string.Empty;

      // "R" (pull) atau "W" (push, mencakup pull).
      public string cCtnRootRobotAccess { get; set; } = string.Empty;
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
   }

   [Table("ta_CtnDeploy")]
   internal class ta_CtnDeploy
   {
      [Key] public string cCtnDeployId { get; set; } = string.Empty;
      public string cCtnImageId { get; set; } = string.Empty;
      public int cCtnDeployState { get; set; }
      public int cCtnDeployKind { get; set; }
      public int cCtnDeployMode { get; set; }
      public string? cCtnDeployTagFilter { get; set; }
      public string cCtnDeployRegistryHost { get; set; } = string.Empty;
      public string? cCtnDeployRegistryUser { get; set; }

      // Credentials are encrypted by CtnDeploySecrets; never sent to a client.
      public byte[]? cCtnDeployRegistrySecret { get; set; }
      public string cCtnDeployHost { get; set; } = string.Empty;
      public int? cCtnDeployPort { get; set; }
      public string? cCtnDeployUser { get; set; }
      public int cCtnDeployAuth { get; set; }
      public byte[]? cCtnDeploySecret { get; set; }
      public byte[]? cCtnDeployPassphrase { get; set; }
      public string? cCtnDeployFingerprint { get; set; }
      public int? cCtnDeployEndpointId { get; set; }
      public string? cCtnDeployStack { get; set; }
      public int? cCtnDeployStackId { get; set; }
      public string? cCtnDeployService { get; set; }
      public string? cCtnDeployContainer { get; set; }
      public string? cCtnDeployImageVar { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
      public string? json_object { get; set; }
   }

   [Table("ta_CtnDeployRun")]
   internal class ta_CtnDeployRun
   {
      [Key] public string cCtnDeployRunId { get; set; } = string.Empty;
      public string cCtnDeployId { get; set; } = string.Empty;
      public int cCtnDeployRunTrigger { get; set; }
      public string? cCtnDeployRunTag { get; set; }
      public string cCtnDeployRunDigest { get; set; } = string.Empty;
      public string? cCtnDeployRunPrevDigest { get; set; }
      public int cCtnDeployRunResult { get; set; }
      public string? cCtnDeployRunOutput { get; set; }
      public string? cCtnDeployRunOldFile { get; set; }
      public string? cCtnDeployRunBy_cUserId { get; set; }
      public DateTime cCtnDeployRunStarted { get; set; }
      public DateTime? cCtnDeployRunFinished { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
   }
}
