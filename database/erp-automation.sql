SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.MessageTemplate') IS NULL
BEGIN
 CREATE TABLE dbo.MessageTemplate(TemplateId int PRIMARY KEY, Sku nvarchar(64) NOT NULL, Quantity int NOT NULL);
 ;WITH n AS (SELECT TOP (1000) ROW_NUMBER() OVER(ORDER BY a.object_id,b.object_id) n FROM sys.all_objects a CROSS JOIN sys.all_objects b)
 INSERT dbo.MessageTemplate SELECT n,CONCAT('AUTO-SKU-',RIGHT(CONCAT('0000',n),4)),1+(n*37)%1000 FROM n;
 CREATE TABLE dbo.AutomationRun(
  RunId uniqueidentifier PRIMARY KEY,SourceId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
  Seed int NOT NULL,ErrorPercent int NOT NULL,IntervalMs int NOT NULL,MessageCount int NOT NULL,
  State varchar(24) NOT NULL,CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),StartedAt datetime2 NULL,FinishedAt datetime2 NULL);
 CREATE TABLE dbo.AutomationMessage(
  RunId uniqueidentifier NOT NULL REFERENCES dbo.AutomationRun(RunId),Sequence int NOT NULL,
  TemplateId int NOT NULL REFERENCES dbo.MessageTemplate(TemplateId),OrderId uniqueidentifier NOT NULL,
  Scenario varchar(24) NOT NULL,Expected varchar(24) NOT NULL,Payload nvarchar(max) NOT NULL CHECK(ISJSON(Payload)=1),
  State varchar(32) NOT NULL DEFAULT 'Queued',SentAt datetime2 NULL,AnalyzedAt datetime2 NULL,CompletedAt datetime2 NULL,
  HttpStatus int NULL,Response nvarchar(max) NULL,Receipt nvarchar(max) NULL,Matched bit NULL,
  PRIMARY KEY(RunId,Sequence));
 CREATE INDEX IX_AutomationMessage_State ON dbo.AutomationMessage(RunId,State,Sequence);
 INSERT dbo.SchemaVersion VALUES(2);
END;
GRANT SELECT ON dbo.MessageTemplate TO CapstoneErpApp;
GRANT SELECT,INSERT,UPDATE ON dbo.AutomationRun TO CapstoneErpApp;
GRANT SELECT,INSERT,UPDATE ON dbo.AutomationMessage TO CapstoneErpApp;
COMMIT;
