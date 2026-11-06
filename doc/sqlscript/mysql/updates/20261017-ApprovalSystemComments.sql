/* Allow system identities to author comments without an associated user row. */
DROP PROCEDURE IF EXISTS mysql_upgrade_approval_comments;
DELIMITER $$
CREATE PROCEDURE mysql_upgrade_approval_comments()
BEGIN
 DECLARE done int DEFAULT 0;
 DECLARE fk varchar(64);
 DECLARE cur CURSOR FOR SELECT DISTINCT CONSTRAINT_NAME FROM information_schema.KEY_COLUMN_USAGE
  WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='ta_ApprovalRequestComment' AND COLUMN_NAME='cUserId' AND REFERENCED_TABLE_NAME='ta_User';
 DECLARE CONTINUE HANDLER FOR NOT FOUND SET done=1;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_ApprovalRequestComment') THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Install approval v2 first';
 END IF;
 OPEN cur;
 read_loop: LOOP
  FETCH cur INTO fk;
  IF done=1 THEN LEAVE read_loop; END IF;
  SET @mysql_upgrade_sql=CONCAT('ALTER TABLE `ta_ApprovalRequestComment` DROP FOREIGN KEY `',REPLACE(fk,'`','``'),'`');
  PREPARE stmt FROM @mysql_upgrade_sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
 END LOOP;
 CLOSE cur;
END$$
DELIMITER ;
CALL mysql_upgrade_approval_comments();
DROP PROCEDURE mysql_upgrade_approval_comments;
