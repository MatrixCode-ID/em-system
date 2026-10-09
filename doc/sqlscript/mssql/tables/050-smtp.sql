/*
    050 - Named SMTP configurations for the multi-profile SMTP manager.

    Run on the target core database after tables/010-core.sql. This script can also
    be applied to an existing installation and is safe to run again.
    Run views/vi_Smtp.sql after this file. The application supplies ULIDs and stamps.

    Apply before running the multi-profile API. On first use the service migrates
    Em.Smtp.Settings transactionally and records Em.Smtp.Profiles.Revision in ta_Meta.
    Passwords hold the existing AES-GCM envelope; Em.Smtp.Key stays in ta_Meta.
    No profile or default is seeded. State: 0 = disabled, 1 = enabled.
    Security: 0 = plaintext, 1 = required STARTTLS, 2 = TLS on connect.
*/

SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[ta_Smtp]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ta_Smtp]
    (
        [cSmtpId]             char(26)      COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        [cSmtpName]           varchar(255)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        [cSmtpState]          int           NOT NULL CONSTRAINT [DF_ta_Smtp_State] DEFAULT (0),
        [cSmtpRevision]       int           NOT NULL CONSTRAINT [DF_ta_Smtp_Revision] DEFAULT (1),
        [cSmtpNote]           varchar(500)  COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        [cSmtpDefault]        bit           NOT NULL CONSTRAINT [DF_ta_Smtp_Default] DEFAULT (0),
        [cSmtpHost]           varchar(255)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        [cSmtpPort]           int           NOT NULL CONSTRAINT [DF_ta_Smtp_Port] DEFAULT (587),
        [cSmtpSecurity]       int           NOT NULL CONSTRAINT [DF_ta_Smtp_Security] DEFAULT (1),
        [cSmtpAuthenticate]   bit           NOT NULL CONSTRAINT [DF_ta_Smtp_Authenticate] DEFAULT (1),
        [cSmtpUsername]       nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        [cSmtpPassword]       varbinary(max) NULL,
        [cSmtpFromAddress]    varchar(320)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        [cSmtpFromName]       nvarchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        [cSmtpTimeoutSeconds] int           NOT NULL CONSTRAINT [DF_ta_Smtp_Timeout] DEFAULT (30),
        [ustamp]              datetime      NOT NULL,
        [datestamp]           datetime      NOT NULL,
        [json_object]         nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        CONSTRAINT [PK_ta_Smtp] PRIMARY KEY CLUSTERED ([cSmtpId]),
        CONSTRAINT [UQ_ta_Smtp_Name] UNIQUE NONCLUSTERED ([cSmtpName]),
        CONSTRAINT [CK_ta_Smtp_Name] CHECK (LEN(LTRIM(RTRIM([cSmtpName]))) > 0),
        CONSTRAINT [CK_ta_Smtp_State] CHECK ([cSmtpState] IN (0, 1)),
        CONSTRAINT [CK_ta_Smtp_Revision] CHECK ([cSmtpRevision] >= 1),
        CONSTRAINT [CK_ta_Smtp_Port] CHECK ([cSmtpPort] BETWEEN 1 AND 65535),
        CONSTRAINT [CK_ta_Smtp_Security] CHECK ([cSmtpSecurity] IN (0, 1, 2)),
        CONSTRAINT [CK_ta_Smtp_Timeout] CHECK ([cSmtpTimeoutSeconds] BETWEEN 5 AND 120),
        CONSTRAINT [CK_ta_Smtp_Json] CHECK ([json_object] IS NULL OR ISJSON([json_object]) = 1)
    );

    -- Only true rows participate: any number of non-default profiles is allowed.
    CREATE UNIQUE NONCLUSTERED INDEX [UX_ta_Smtp_Default]
        ON [dbo].[ta_Smtp] ([cSmtpDefault]) WHERE [cSmtpDefault] = 1;
END;

COMMIT TRANSACTION;
GO
