-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_PublicHoliday AS
SELECT h.* FROM ta_PublicHoliday h;
