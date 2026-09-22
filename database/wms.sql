SET XACT_ABORT ON;
BEGIN TRANSACTION;
CREATE TABLE dbo.SchemaVersion(Version int NOT NULL PRIMARY KEY);
INSERT dbo.SchemaVersion VALUES(1);
CREATE TABLE dbo.AcceptedOrder(
 SourceId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 OrderId uniqueidentifier NOT NULL,
 Payload nvarchar(max) NOT NULL CHECK(ISJSON(Payload)=1),
 CONSTRAINT PK_AcceptedOrder PRIMARY KEY(SourceId,OrderId));
CREATE TABLE dbo.AcceptanceReceipt(
 SourceId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 OrderId uniqueidentifier NOT NULL,
 Receipt nvarchar(max) NOT NULL CHECK(ISJSON(Receipt)=1),
 CONSTRAINT PK_Receipt PRIMARY KEY(SourceId,OrderId),
 CONSTRAINT FK_Receipt_Order FOREIGN KEY(SourceId,OrderId) REFERENCES dbo.AcceptedOrder(SourceId,OrderId));
COMMIT;
