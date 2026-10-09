/*
    SMTP manager list and editor metadata without password ciphertext.
    Requires tables/050-smtp.sql. Password presence is exposed as a boolean.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DROP VIEW IF EXISTS [dbo].[vi_Smtp];
GO

CREATE VIEW [dbo].[vi_Smtp] AS
SELECT [cSmtpId], [cSmtpName], [cSmtpState], [cSmtpRevision], [cSmtpNote],
       [cSmtpDefault], [cSmtpHost], [cSmtpPort], [cSmtpSecurity],
       [cSmtpAuthenticate], [cSmtpUsername], [cSmtpFromAddress], [cSmtpFromName],
       [cSmtpTimeoutSeconds], [ustamp], [datestamp], [json_object],
       CAST(CASE WHEN DATALENGTH([cSmtpPassword]) > 0 THEN 1 ELSE 0 END AS bit) AS [cvSmtpHasPassword]
FROM [dbo].[ta_Smtp];
GO

SELECT TOP 100 * FROM [dbo].[vi_Smtp] ORDER BY [cSmtpName];
GO
