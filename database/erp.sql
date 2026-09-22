SET XACT_ABORT ON;
BEGIN TRANSACTION;
CREATE TABLE dbo.SchemaVersion(Version int NOT NULL PRIMARY KEY);
INSERT dbo.SchemaVersion VALUES(1);
CREATE TABLE dbo.SourceOrder(
 SourceId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 OrderId uniqueidentifier NOT NULL,
 Payload nvarchar(max) NOT NULL CHECK(ISJSON(Payload)=1),
 CONSTRAINT PK_SourceOrder PRIMARY KEY(SourceId,OrderId));
CREATE TABLE dbo.OutboxMessage(
 SourceId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 OrderId uniqueidentifier NOT NULL,
 Status varchar(32) NOT NULL CHECK(Status IN ('Pending','Acknowledged','Rejected','RecoveryRequired')),
 CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 Receipt nvarchar(max) NULL,
 CONSTRAINT PK_Outbox PRIMARY KEY(SourceId,OrderId),
 CONSTRAINT FK_Outbox_Order FOREIGN KEY(SourceId,OrderId) REFERENCES dbo.SourceOrder(SourceId,OrderId),
 CONSTRAINT CK_Outbox_Receipt CHECK((Status='Acknowledged' AND Receipt IS NOT NULL AND ISJSON(Receipt)=1) OR (Status<>'Acknowledged' AND Receipt IS NULL)));
CREATE INDEX IX_Outbox_Pending ON dbo.OutboxMessage(Status,CreatedAt);
CREATE TABLE dbo.DeliveryAttempt(
 AttemptId bigint IDENTITY PRIMARY KEY,
 SourceId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 OrderId uniqueidentifier NOT NULL,
 AttemptedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 Outcome varchar(64) NOT NULL,
 FOREIGN KEY(SourceId,OrderId) REFERENCES dbo.OutboxMessage(SourceId,OrderId));
COMMIT;
