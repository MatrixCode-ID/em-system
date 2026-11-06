/* Add the optional approval sub-summary; run views/vi_ApprovalRequestStatus.sql afterward. */
DROP PROCEDURE IF EXISTS mysql_upgrade_approval_subsummary;
DELIMITER $$
CREATE PROCEDURE mysql_upgrade_approval_subsummary()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_ApprovalRequest') THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Install approval v2 first';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_ApprovalRequest' AND COLUMN_NAME='cSubSummary') THEN
  ALTER TABLE ta_ApprovalRequest ADD cSubSummary varchar(400) NULL;
 END IF;
END$$
DELIMITER ;
CALL mysql_upgrade_approval_subsummary();
DROP PROCEDURE mysql_upgrade_approval_subsummary;
