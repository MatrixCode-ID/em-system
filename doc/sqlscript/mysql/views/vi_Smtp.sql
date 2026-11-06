-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_Smtp` AS
SELECT `cSmtpId`, `cSmtpName`, `cSmtpState`, `cSmtpRevision`, `cSmtpNote`,
       `cSmtpDefault`, `cSmtpHost`, `cSmtpPort`, `cSmtpSecurity`,
       `cSmtpAuthenticate`, `cSmtpUsername`, `cSmtpFromAddress`, `cSmtpFromName`,
       `cSmtpTimeoutSeconds`, `ustamp`, `datestamp`, `json_object`,
       (CASE WHEN OCTET_LENGTH(`cSmtpPassword`) > 0 THEN 1 ELSE 0 END) AS `cvSmtpHasPassword`
FROM `ta_Smtp`;
