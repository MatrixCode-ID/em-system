/* MySQL 8.0+: ULID generation and binary-to-text conversion.
   Run in the target database with a client that supports DELIMITER.
   Stored functions cannot be used in MySQL column default expressions; callers
   must supply the ID explicitly or use a separately designed insert trigger.
   Extraction date functions return UTC DATETIME(3), without an offset. */
DROP FUNCTION IF EXISTS NewUlidString;
DROP FUNCTION IF EXISTS NewUlidStringFromBytes;
DROP FUNCTION IF EXISTS NewUlid;
DELIMITER $$
CREATE FUNCTION NewUlid() RETURNS BINARY(16)
NOT DETERMINISTIC NO SQL
BEGIN
    DECLARE ms BIGINT UNSIGNED;
    SET ms = TIMESTAMPDIFF(MICROSECOND, '1970-01-01 00:00:00', UTC_TIMESTAMP(3)) DIV 1000;
    RETURN CONCAT(UNHEX(LPAD(HEX(ms), 12, '0')), RANDOM_BYTES(10));
END$$
CREATE FUNCTION NewUlidStringFromBytes(value VARBINARY(16)) RETURNS CHAR(26) CHARACTER SET ascii
DETERMINISTIC NO SQL
BEGIN
    DECLARE alphabet CHAR(32) CHARACTER SET ascii DEFAULT '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
    DECLARE output VARCHAR(26) CHARACTER SET ascii DEFAULT '';
    DECLARE accumulator INT UNSIGNED DEFAULT 0;
    DECLARE bits INT DEFAULT 2;
    DECLARE pos INT DEFAULT 1;
    IF value IS NULL OR OCTET_LENGTH(value) <> 16 THEN RETURN NULL; END IF;
    WHILE pos <= 16 DO
        SET accumulator = (accumulator << 8) | ORD(SUBSTRING(value, pos, 1));
        SET bits = bits + 8;
        WHILE bits >= 5 DO
            SET bits = bits - 5;
            SET output = CONCAT(output, SUBSTRING(alphabet, ((accumulator >> bits) & 31) + 1, 1));
            SET accumulator = accumulator & ((1 << bits) - 1);
        END WHILE;
        SET pos = pos + 1;
    END WHILE;
    RETURN output;
END$$
CREATE FUNCTION NewUlidString() RETURNS CHAR(26) CHARACTER SET ascii
NOT DETERMINISTIC NO SQL
BEGIN
    RETURN NewUlidStringFromBytes(NewUlid());
END$$
DELIMITER ;

/* Extraction helpers return UTC DATETIME(3); MySQL DATETIME has no offset. */
DROP FUNCTION IF EXISTS GetUlidDatePartString;
DROP FUNCTION IF EXISTS GetUlidRandomPartString;
DROP FUNCTION IF EXISTS GetUlidDatePart;
DROP FUNCTION IF EXISTS GetUlidRandomPart;
DROP FUNCTION IF EXISTS UlidBytesFromString;
DELIMITER $$
CREATE FUNCTION UlidBytesFromString(value VARCHAR(26) CHARACTER SET ascii) RETURNS VARBINARY(16)
DETERMINISTIC NO SQL
BEGIN
    DECLARE alphabet CHAR(32) CHARACTER SET ascii DEFAULT '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
    DECLARE result VARBINARY(16) DEFAULT '';
    DECLARE accumulator INT UNSIGNED DEFAULT 0;
    DECLARE bits INT DEFAULT 0;
    DECLARE digit INT;
    DECLARE pos INT DEFAULT 1;
    IF value IS NULL OR CHAR_LENGTH(value) <> 26 THEN RETURN NULL; END IF;
    WHILE pos <= 26 DO
        SET digit = LOCATE(UPPER(SUBSTRING(value,pos,1)) COLLATE ascii_bin, alphabet COLLATE ascii_bin) - 1;
        IF digit < 0 OR (pos = 1 AND digit > 7) THEN RETURN NULL; END IF;
        IF pos = 1 THEN SET bits = 3; SET accumulator = digit;
        ELSE SET bits = bits + 5; SET accumulator = (accumulator << 5) | digit; END IF;
        IF bits >= 8 THEN
            SET bits = bits - 8;
            SET result = CONCAT(result, UNHEX(LPAD(HEX((accumulator >> bits) & 255), 2, '0')));
            SET accumulator = accumulator & ((1 << bits) - 1);
        END IF;
        SET pos = pos + 1;
    END WHILE;
    RETURN result;
END$$
CREATE FUNCTION GetUlidRandomPart(value VARBINARY(16)) RETURNS VARBINARY(10)
DETERMINISTIC NO SQL
BEGIN
    IF value IS NULL OR OCTET_LENGTH(value) <> 16 THEN RETURN NULL; END IF;
    RETURN SUBSTRING(value,7,10);
END$$
CREATE FUNCTION GetUlidRandomPartString(value VARCHAR(26) CHARACTER SET ascii) RETURNS VARBINARY(10)
DETERMINISTIC NO SQL
BEGIN
    RETURN GetUlidRandomPart(UlidBytesFromString(value));
END$$
CREATE FUNCTION GetUlidDatePart(value VARBINARY(16)) RETURNS DATETIME(3)
DETERMINISTIC NO SQL
BEGIN
    DECLARE ms BIGINT UNSIGNED;
    IF value IS NULL OR OCTET_LENGTH(value) <> 16 THEN RETURN NULL; END IF;
    SET ms = CAST(CONV(HEX(SUBSTRING(value,1,6)),16,10) AS UNSIGNED);
    IF ms > 253402300799999 THEN RETURN NULL; END IF;
    RETURN TIMESTAMPADD(MICROSECOND, (ms MOD 86400000) * 1000,
        TIMESTAMPADD(DAY, ms DIV 86400000, CAST('1970-01-01 00:00:00' AS DATETIME(3))));
END$$
CREATE FUNCTION GetUlidDatePartString(value VARCHAR(26) CHARACTER SET ascii) RETURNS DATETIME(3)
DETERMINISTIC NO SQL
BEGIN
    RETURN GetUlidDatePart(UlidBytesFromString(value));
END$$
DELIMITER ;
