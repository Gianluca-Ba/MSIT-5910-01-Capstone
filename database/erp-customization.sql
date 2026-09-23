SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.CustomMessageDefinition') IS NULL
CREATE TABLE dbo.CustomMessageDefinition (
    SourceId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Name nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Version int NOT NULL,
    Definition nvarchar(max) NOT NULL CHECK(ISJSON(Definition)=1),
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_CustomMessageDefinition PRIMARY KEY(SourceId,Name,Version)
);
IF OBJECT_ID('dbo.CustomValidationBatch') IS NULL
CREATE TABLE dbo.CustomValidationBatch (
    BatchId uniqueidentifier NOT NULL PRIMARY KEY,
    SourceId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Name nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Version int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    Evidence nvarchar(max) NOT NULL CHECK(ISJSON(Evidence)=1),
    CONSTRAINT FK_CustomValidationDefinition FOREIGN KEY(SourceId,Name,Version)
      REFERENCES dbo.CustomMessageDefinition(SourceId,Name,Version)
);
GRANT SELECT,INSERT ON dbo.CustomMessageDefinition TO CapstoneErpApp;
GRANT SELECT,INSERT ON dbo.CustomValidationBatch TO CapstoneErpApp;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersion WHERE Version=3) INSERT dbo.SchemaVersion(Version) VALUES(3);
COMMIT;
