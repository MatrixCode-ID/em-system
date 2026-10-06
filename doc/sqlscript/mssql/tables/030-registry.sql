/*
    Container registry tables (complete set; run once on the core database).

    Run on the target database (USE [DatabaseName]) after tables/010-core.sql (ta_Robot,
    ta_User). Safe to run again: each table is only created when missing. Tables are ordered by
    foreign key dependency:
    ta_CtnRoot, ta_CtnFolder, ta_CtnImage, ta_CtnManifest, ta_CtnTag, ta_CtnBlob,
    ta_CtnBlobLink, ta_CtnManifestBlob, ta_CtnUpload, ta_CtnRootRobot, ta_CtnDeploy,
    ta_CtnDeployRun.

    Notes:
    - Tag, digest and token hash columns use a CASE-SENSITIVE collation (SQL_Latin1_General_CP1_CS_AS):
      in OCI "Latest" and "latest" are two different tags. The default collation of this schema is case-insensitive.
    - Name length limits (root 64, container 128, tag 128, folder 100, folder depth 8) are enforced by
      the service, not the schema; the columns are wide enough.
    - Unique names among sibling folders are enforced by the service inside one transaction, not by a unique index.
    - Blob content (layers) lives on disk; ta_CtnBlob is only its index.
    - Existing databases created before 2026-10-07 get ta_CtnDeploy and ta_CtnDeployRun from
      updates/20261007-CtnDeploy.sql.
*/

SET XACT_ABORT ON;
GO

--region ta_CtnRoot
IF OBJECT_ID(N'[dbo].[ta_CtnRoot]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnRoot]
      (
         [cCtnRootId]          char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnRootName]        varchar(64)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnRootState]       int          NOT NULL,
         [cCtnRootDescription] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [ustamp]              datetime     NOT NULL,
         [datestamp]           datetime     NOT NULL,
         [json_object]         nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [PK_ta_CtnRoot] PRIMARY KEY CLUSTERED ([cCtnRootId]),
         CONSTRAINT [UQ_ta_CtnRoot_cCtnRootName] UNIQUE NONCLUSTERED ([cCtnRootName])
      );
   END;
GO
--endregion

--region ta_CtnFolder
IF OBJECT_ID(N'[dbo].[ta_CtnFolder]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnFolder]
      (
         [cCtnFolderId]                  char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnRootId]                    char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         -- Kosong = langsung di root.
         [cCtnFolderParent_cCtnFolderId] char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [cCtnFolderName]                varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnFolderOrder]               int          NOT NULL CONSTRAINT [DF_ta_CtnFolder_cCtnFolderOrder] DEFAULT (-1),
         [ustamp]                        datetime     NOT NULL,
         [datestamp]                     datetime     NOT NULL,
         [json_object]                   nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [PK_ta_CtnFolder] PRIMARY KEY CLUSTERED ([cCtnFolderId]),
         CONSTRAINT [FK_ta_CtnFolder_ta_CtnRoot] FOREIGN KEY ([cCtnRootId]) REFERENCES [dbo].[ta_CtnRoot] ([cCtnRootId]),
         CONSTRAINT [FK_ta_CtnFolder_cCtnFolderParent_cCtnFolderId] FOREIGN KEY ([cCtnFolderParent_cCtnFolderId]) REFERENCES [dbo].[ta_CtnFolder] ([cCtnFolderId])
      );
      CREATE NONCLUSTERED INDEX [IX_ta_CtnFolder_cCtnRootId] ON [dbo].[ta_CtnFolder] ([cCtnRootId]);
      CREATE NONCLUSTERED INDEX [IX_ta_CtnFolder_Parent] ON [dbo].[ta_CtnFolder] ([cCtnFolderParent_cCtnFolderId]);
   END;
GO
--endregion

--region ta_CtnImage
IF OBJECT_ID(N'[dbo].[ta_CtnImage]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnImage]
      (
         [cCtnImageId]          char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnRootId]           char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnFolderId]         char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [cCtnImageName]        varchar(128) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnImageState]       int          NOT NULL,
         [cCtnImageDescription] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [ustamp]               datetime     NOT NULL,
         [datestamp]            datetime     NOT NULL,
         [json_object]          nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [PK_ta_CtnImage] PRIMARY KEY CLUSTERED ([cCtnImageId]),
         CONSTRAINT [UQ_ta_CtnImage_Root_Name] UNIQUE NONCLUSTERED ([cCtnRootId], [cCtnImageName]),
         CONSTRAINT [FK_ta_CtnImage_ta_CtnRoot] FOREIGN KEY ([cCtnRootId]) REFERENCES [dbo].[ta_CtnRoot] ([cCtnRootId]),
         CONSTRAINT [FK_ta_CtnImage_ta_CtnFolder] FOREIGN KEY ([cCtnFolderId]) REFERENCES [dbo].[ta_CtnFolder] ([cCtnFolderId])
      );
      CREATE NONCLUSTERED INDEX [IX_ta_CtnImage_cCtnFolderId] ON [dbo].[ta_CtnImage] ([cCtnFolderId]);
   END;
GO
--endregion

--region ta_CtnManifest
IF OBJECT_ID(N'[dbo].[ta_CtnManifest]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnManifest]
      (
         [cCtnManifestId]                  char(26)      COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnImageId]                     char(26)      COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnManifestDigest]              varchar(128)  COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
         [cCtnManifestMediaType]           varchar(255)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnManifestSize]                bigint        NOT NULL,
         -- Byte persis seperti yang dikirim: digest dihitung dari byte-nya.
         [cCtnManifestContent]             varbinary(max) NOT NULL,
         [cCtnManifestPushedBy_cRobotId] char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [ustamp]                          datetime      NOT NULL,
         [datestamp]                       datetime      NOT NULL,
         [json_object]                     nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [PK_ta_CtnManifest] PRIMARY KEY CLUSTERED ([cCtnManifestId]),
         CONSTRAINT [UQ_ta_CtnManifest_Image_Digest] UNIQUE NONCLUSTERED ([cCtnImageId], [cCtnManifestDigest]),
         CONSTRAINT [FK_ta_CtnManifest_ta_CtnImage] FOREIGN KEY ([cCtnImageId]) REFERENCES [dbo].[ta_CtnImage] ([cCtnImageId]) ON DELETE CASCADE,
         CONSTRAINT [FK_ta_CtnManifest_cCtnManifestPushedBy_cRobotId] FOREIGN KEY ([cCtnManifestPushedBy_cRobotId]) REFERENCES [dbo].[ta_Robot] ([cRobotId])
      );
   END;
GO
--endregion

--region ta_CtnTag
IF OBJECT_ID(N'[dbo].[ta_CtnTag]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnTag]
      (
         [cCtnImageId]    char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnTagName]    varchar(128) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
         [cCtnManifestId] char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [ustamp]         datetime     NOT NULL,
         [datestamp]      datetime     NOT NULL,
         CONSTRAINT [PK_ta_CtnTag] PRIMARY KEY CLUSTERED ([cCtnImageId], [cCtnTagName]),
         CONSTRAINT [FK_ta_CtnTag_ta_CtnImage] FOREIGN KEY ([cCtnImageId]) REFERENCES [dbo].[ta_CtnImage] ([cCtnImageId]) ON DELETE CASCADE,
         CONSTRAINT [FK_ta_CtnTag_ta_CtnManifest] FOREIGN KEY ([cCtnManifestId]) REFERENCES [dbo].[ta_CtnManifest] ([cCtnManifestId])
      );
      CREATE NONCLUSTERED INDEX [IX_ta_CtnTag_cCtnManifestId] ON [dbo].[ta_CtnTag] ([cCtnManifestId]);
   END;
GO
--endregion

--region ta_CtnBlob
IF OBJECT_ID(N'[dbo].[ta_CtnBlob]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnBlob]
      (
         [cCtnBlobId]     char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnBlobDigest] varchar(128) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
         [cCtnBlobSize]   bigint       NOT NULL,
         [ustamp]         datetime     NOT NULL,
         [datestamp]      datetime     NOT NULL,
         [json_object]    nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [PK_ta_CtnBlob] PRIMARY KEY CLUSTERED ([cCtnBlobId]),
         CONSTRAINT [UQ_ta_CtnBlob_cCtnBlobDigest] UNIQUE NONCLUSTERED ([cCtnBlobDigest])
      );
   END;
GO
--endregion

--region ta_CtnBlobLink
-- Pagar keamanan: blob hanya bisa diambil lewat container yang terhubung dengannya.
IF OBJECT_ID(N'[dbo].[ta_CtnBlobLink]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnBlobLink]
      (
         [cCtnImageId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnBlobId]  char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [datestamp]   datetime NOT NULL,
         CONSTRAINT [PK_ta_CtnBlobLink] PRIMARY KEY CLUSTERED ([cCtnImageId], [cCtnBlobId]),
         CONSTRAINT [FK_ta_CtnBlobLink_ta_CtnImage] FOREIGN KEY ([cCtnImageId]) REFERENCES [dbo].[ta_CtnImage] ([cCtnImageId]) ON DELETE CASCADE,
         CONSTRAINT [FK_ta_CtnBlobLink_ta_CtnBlob] FOREIGN KEY ([cCtnBlobId]) REFERENCES [dbo].[ta_CtnBlob] ([cCtnBlobId])
      );
      CREATE NONCLUSTERED INDEX [IX_ta_CtnBlobLink_cCtnBlobId] ON [dbo].[ta_CtnBlobLink] ([cCtnBlobId]);
   END;
GO
--endregion

--region ta_CtnManifestBlob
IF OBJECT_ID(N'[dbo].[ta_CtnManifestBlob]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnManifestBlob]
      (
         [cCtnManifestId]        char(26)    COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnManifestBlobOrder] int         NOT NULL,
         [cCtnBlobId]            char(26)    COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         -- 'config' atau 'layer'.
         [cCtnManifestBlobRole]  varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         CONSTRAINT [PK_ta_CtnManifestBlob] PRIMARY KEY CLUSTERED ([cCtnManifestId], [cCtnManifestBlobOrder]),
         CONSTRAINT [FK_ta_CtnManifestBlob_ta_CtnManifest] FOREIGN KEY ([cCtnManifestId]) REFERENCES [dbo].[ta_CtnManifest] ([cCtnManifestId]) ON DELETE CASCADE,
         CONSTRAINT [FK_ta_CtnManifestBlob_ta_CtnBlob] FOREIGN KEY ([cCtnBlobId]) REFERENCES [dbo].[ta_CtnBlob] ([cCtnBlobId])
      );
      CREATE NONCLUSTERED INDEX [IX_ta_CtnManifestBlob_cCtnBlobId] ON [dbo].[ta_CtnManifestBlob] ([cCtnBlobId]);
   END;
GO
--endregion

--region ta_CtnUpload
IF OBJECT_ID(N'[dbo].[ta_CtnUpload]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnUpload]
      (
         [cCtnUploadId]   char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnImageId]    char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cRobotId]    char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         -- Offset: berapa byte yang sudah diterima. Berkas sementara di disk bernama sesuai Id.
         [cCtnUploadSize] bigint   NOT NULL,
         [ustamp]         datetime NOT NULL,
         [datestamp]      datetime NOT NULL,
         [json_object]    nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [PK_ta_CtnUpload] PRIMARY KEY CLUSTERED ([cCtnUploadId]),
         CONSTRAINT [FK_ta_CtnUpload_ta_CtnImage] FOREIGN KEY ([cCtnImageId]) REFERENCES [dbo].[ta_CtnImage] ([cCtnImageId]) ON DELETE CASCADE,
         CONSTRAINT [FK_ta_CtnUpload_ta_CtnRobot] FOREIGN KEY ([cRobotId]) REFERENCES [dbo].[ta_Robot] ([cRobotId])
      );
      CREATE NONCLUSTERED INDEX [IX_ta_CtnUpload_cCtnImageId] ON [dbo].[ta_CtnUpload] ([cCtnImageId]);
   END;
GO
--endregion

--region ta_CtnRootRobot
IF OBJECT_ID(N'[dbo].[ta_CtnRootRobot]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnRootRobot]
      (
         [cRobotId]         char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnRootId]          char(26) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         -- 'R' (pull) atau 'W' (push; mencakup pull).
         [cCtnRootRobotAccess] char(1)  COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
         [ustamp]              datetime NOT NULL,
         [datestamp]           datetime NOT NULL,
         CONSTRAINT [PK_ta_CtnRobotRoot] PRIMARY KEY CLUSTERED ([cRobotId], [cCtnRootId]),
         CONSTRAINT [CK_ta_CtnRobotRoot_Access] CHECK ([cCtnRootRobotAccess] IN ('R', 'W')),
         CONSTRAINT [FK_ta_CtnRobotRoot_ta_CtnRobot] FOREIGN KEY ([cRobotId]) REFERENCES [dbo].[ta_Robot] ([cRobotId]) ON DELETE CASCADE,
         CONSTRAINT [FK_ta_CtnRobotRoot_ta_CtnRoot] FOREIGN KEY ([cCtnRootId]) REFERENCES [dbo].[ta_CtnRoot] ([cCtnRootId])
      );
      CREATE NONCLUSTERED INDEX [IX_ta_CtnRobotRoot_cCtnRootId] ON [dbo].[ta_CtnRootRobot] ([cCtnRootId]);
   END;
GO
--endregion

--region ta_CtnDeploy
-- One deploy target per registry container: where and how a pushed image is pulled and the container recreated.
-- Credentials are AES-GCM encrypted by the API; the key lives in ta_Meta (Ctn.Deploy.Key).
IF OBJECT_ID(N'[dbo].[ta_CtnDeploy]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnDeploy]
      (
         [cCtnDeployId]             char(26)       COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnImageId]              char(26)       COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnDeployState]          int            NOT NULL,
         -- 1 = SSH, 2 = Portainer.
         [cCtnDeployKind]           int            NOT NULL,
         -- 1 = Stack (compose), 2 = standalone container.
         [cCtnDeployMode]           int            NOT NULL,
         -- Comma-separated tag patterns (* and ?); empty = every tag.
         [cCtnDeployTagFilter]      varchar(256)   COLLATE SQL_Latin1_General_CP1_CS_AS NULL,
         -- Registry address the Docker server pulls from (host[:port]).
         [cCtnDeployRegistryHost]   varchar(255)   COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnDeployRegistryUser]   varchar(128)   COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [cCtnDeployRegistrySecret] varbinary(max) NULL,
         -- SSH host name, or the base URL of Portainer.
         [cCtnDeployHost]           varchar(255)   COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnDeployPort]           int            NULL,
         [cCtnDeployUser]           varchar(128)   COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         -- 1 = SSH key, 2 = SSH password, 3 = Portainer access token.
         [cCtnDeployAuth]           int            NOT NULL,
         [cCtnDeploySecret]         varbinary(max) NULL,
         [cCtnDeployPassphrase]     varbinary(max) NULL,
         -- Pinned SSH host key or TLS certificate fingerprint.
         [cCtnDeployFingerprint]    varchar(128)   COLLATE SQL_Latin1_General_CP1_CS_AS NULL,
         [cCtnDeployEndpointId]     int            NULL,
         -- Compose folder (SSH) or stack name (Portainer).
         [cCtnDeployStack]          varchar(255)   COLLATE SQL_Latin1_General_CP1_CS_AS NULL,
         [cCtnDeployStackId]        int            NULL,
         [cCtnDeployService]        varchar(128)   COLLATE SQL_Latin1_General_CP1_CS_AS NULL,
         [cCtnDeployContainer]      varchar(128)   COLLATE SQL_Latin1_General_CP1_CS_AS NULL,
         [cCtnDeployImageVar]       varchar(128)   COLLATE SQL_Latin1_General_CP1_CS_AS NULL,
         [ustamp]                   datetime       NOT NULL,
         [datestamp]                datetime       NOT NULL,
         [json_object]              nvarchar(max)  COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [PK_ta_CtnDeploy] PRIMARY KEY CLUSTERED ([cCtnDeployId]),
         CONSTRAINT [UQ_ta_CtnDeploy_cCtnImageId] UNIQUE NONCLUSTERED ([cCtnImageId]),
         CONSTRAINT [FK_ta_CtnDeploy_ta_CtnImage] FOREIGN KEY ([cCtnImageId]) REFERENCES [dbo].[ta_CtnImage] ([cCtnImageId]) ON DELETE CASCADE
      );
   END;
GO
--endregion

--region ta_CtnDeployRun
-- Deploy history. Output never contains credentials.
IF OBJECT_ID(N'[dbo].[ta_CtnDeployRun]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_CtnDeployRun]
      (
         [cCtnDeployRunId]         char(26)      COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cCtnDeployId]            char(26)      COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         -- 1 = after push, 2 = manual, 3 = rollback.
         [cCtnDeployRunTrigger]    int           NOT NULL,
         [cCtnDeployRunTag]        varchar(128)  COLLATE SQL_Latin1_General_CP1_CS_AS NULL,
         [cCtnDeployRunDigest]     varchar(128)  COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
         [cCtnDeployRunPrevDigest] varchar(128)  COLLATE SQL_Latin1_General_CP1_CS_AS NULL,
         -- 0 = running, 1 = success, 2 = failed, 3 = skipped.
         [cCtnDeployRunResult]     int           NOT NULL,
         [cCtnDeployRunOutput]     nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         -- Compose/stack file content before the image line was rewritten to a variable.
         [cCtnDeployRunOldFile]    nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [cCtnDeployRunBy_cUserId] char(26)      COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [cCtnDeployRunStarted]    datetime      NOT NULL,
         [cCtnDeployRunFinished]   datetime      NULL,
         [ustamp]                  datetime      NOT NULL,
         [datestamp]               datetime      NOT NULL,
         CONSTRAINT [PK_ta_CtnDeployRun] PRIMARY KEY CLUSTERED ([cCtnDeployRunId]),
         CONSTRAINT [FK_ta_CtnDeployRun_ta_CtnDeploy] FOREIGN KEY ([cCtnDeployId]) REFERENCES [dbo].[ta_CtnDeploy] ([cCtnDeployId]) ON DELETE CASCADE,
         CONSTRAINT [FK_ta_CtnDeployRun_cCtnDeployRunBy_cUserId] FOREIGN KEY ([cCtnDeployRunBy_cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]) ON DELETE SET NULL
      );
      CREATE NONCLUSTERED INDEX [IX_ta_CtnDeployRun_cCtnDeployId] ON [dbo].[ta_CtnDeployRun] ([cCtnDeployId], [cCtnDeployRunStarted]);
   END;
GO
--endregion
