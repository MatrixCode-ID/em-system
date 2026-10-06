/*
    Migration 2026-10-07: container deploy targets and deploy history (registry deploy to Docker servers).

    Run on the core database of an existing installation (USE [DatabaseName]) after
    tables/010-core.sql and tables/030-registry.sql. New installations get the same tables from
    tables/030-registry.sql. Safe to run again: each table is created only when missing.

    Required before starting an API build that includes registry deploy: the registry startup
    check reads both tables and refuses to start without them.
*/

SET XACT_ABORT ON;
GO

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
