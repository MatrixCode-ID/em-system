/* Add optional robot ownership; grants remain independent. */
DROP PROCEDURE IF EXISTS mysql_upgrade_robot_owner;
DELIMITER $$
CREATE PROCEDURE mysql_upgrade_robot_owner()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_Robot' AND COLUMN_NAME='cRobotOwner_cUserId') THEN
  ALTER TABLE ta_Robot ADD cRobotOwner_cUserId char(26) NULL;
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='ta_Robot' AND CONSTRAINT_NAME='FK_ta_Robot_Owner') THEN
  ALTER TABLE ta_Robot ADD CONSTRAINT FK_ta_Robot_Owner FOREIGN KEY(cRobotOwner_cUserId) REFERENCES ta_User(cUserId) ON DELETE SET NULL;
 END IF;
END$$
DELIMITER ;
CALL mysql_upgrade_robot_owner();
DROP PROCEDURE mysql_upgrade_robot_owner;
