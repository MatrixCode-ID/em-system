/* Run on the new target database before creating tables. Identifier casing is
   fixed when the MySQL server data directory is initialized, not per database.
   Em System requires lower_case_table_names=2 to preserve names such as ta_Meta. */
DROP PROCEDURE IF EXISTS em_check_table_name_case;
DELIMITER $$
CREATE PROCEDURE em_check_table_name_case()
BEGIN
 IF @@lower_case_table_names <> 2 THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Em System requires lower_case_table_names=2; initialize a compatible MySQL instance with this setting';
 END IF;
END$$
DELIMITER ;
CALL em_check_table_name_case();
DROP PROCEDURE em_check_table_name_case;
