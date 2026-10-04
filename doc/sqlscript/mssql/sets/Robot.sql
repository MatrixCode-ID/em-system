-- Shared robot identities, independent of Container registry. Identity columns use cRobot*.
SET XACT_ABORT ON;
GO
--region ta_Robot
IF OBJECT_ID(N'[dbo].[ta_Robot]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_Robot]
      (
         [cRobotId]            char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cRobotName]          varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cRobotOwner_cUserId] char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [cRobotState]         int          NOT NULL,
         [cRobotDescription]   varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         -- SHA-256 hex dari token; tokennya sendiri tidak pernah disimpan.
         [cRobotTokenHash]     varchar(64)  COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
         [cRobotTokenPrefix]   varchar(20)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cRobotTokenExpiry]   datetime     NULL,
         [cRobotTokenLastUsed] datetime     NULL,
         [ustamp]                 datetime     NOT NULL,
         [datestamp]              datetime     NOT NULL,
         [json_object]            nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [FK_ta_Robot_Owner] FOREIGN KEY ([cRobotOwner_cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]) ON DELETE SET NULL,
         CONSTRAINT [PK_ta_CtnRobot] PRIMARY KEY CLUSTERED ([cRobotId]),
         CONSTRAINT [UQ_ta_CtnRobot_cRobotName] UNIQUE NONCLUSTERED ([cRobotName]),
         CONSTRAINT [UQ_ta_CtnRobot_cRobotTokenHash] UNIQUE NONCLUSTERED ([cRobotTokenHash])
      );
   END;
GO
--endregion
GO
