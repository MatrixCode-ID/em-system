/* Add optional deploy image-reference mode; existing rows retain NULL. */
DROP PROCEDURE IF EXISTS mysql_upgrade_deploy_image_ref;
DELIMITER $$
CREATE PROCEDURE mysql_upgrade_deploy_image_ref()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_CtnDeploy') THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Install registry deploy tables first';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_CtnDeploy' AND COLUMN_NAME='cCtnDeployImageRef') THEN
  ALTER TABLE ta_CtnDeploy ADD cCtnDeployImageRef int NULL;
 END IF;
END$$
DELIMITER ;
CALL mysql_upgrade_deploy_image_ref();
DROP PROCEDURE mysql_upgrade_deploy_image_ref;
