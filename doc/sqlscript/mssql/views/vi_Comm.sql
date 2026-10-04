DROP VIEW IF EXISTS vi_Comm;
GO
CREATE VIEW vi_Comm AS
SELECT
   a.cCommId
   , a.cContactId

   , a.cCommType
   , a.cCommState
   , a.cCommValue
   , a.cCommNote
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Comm AS a
GO

SELECT
   a.cCommId
   , a.cContactId
   , a.cCommType
   , a.cCommState
   , a.cCommValue
   , a.cCommNote
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM vi_Comm AS a