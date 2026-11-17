-- BoAdmin denetim tablosu.
-- Uygulama ilk açılışta tabloyu kendisi oluşturmayı dener. Prod'da uygulama havuzu
-- kimliğinin CREATE TABLE yetkisi yoksa bu script DBA tarafından bir kez çalıştırılır.
-- Sonrasında uygulama kimliğine yalnızca SELECT + INSERT yetkisi yeterlidir.

IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLog
    (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
        TimestampUtc  DATETIME2(3)     NOT NULL,
        UserName      NVARCHAR(256)    NOT NULL,
        DisplayName   NVARCHAR(256)    NULL,
        Module        NVARCHAR(50)     NOT NULL,
        Operation     NVARCHAR(50)     NOT NULL,
        ObjectId      INT              NULL,
        ObjectName    NVARCHAR(512)    NULL,
        ObjectKind    NVARCHAR(100)    NULL,
        FolderPath    NVARCHAR(2000)   NULL,
        TargetPath    NVARCHAR(2000)   NULL,
        Success       BIT              NOT NULL,
        Message       NVARCHAR(4000)   NULL,
        InstanceCount INT              NULL,
        BatchId       UNIQUEIDENTIFIER NULL
    );

    CREATE INDEX IX_AuditLog_TimestampUtc ON dbo.AuditLog (TimestampUtc);
    CREATE INDEX IX_AuditLog_UserName     ON dbo.AuditLog (UserName);
    CREATE INDEX IX_AuditLog_ObjectId     ON dbo.AuditLog (ObjectId);
    CREATE INDEX IX_AuditLog_BatchId      ON dbo.AuditLog (BatchId);
END
GO

-- Örnek yetki (IIS uygulama havuzu kimliği):
-- CREATE USER [DOMAIN\SUNUCUADI$] FOR LOGIN [DOMAIN\SUNUCUADI$];
-- GRANT SELECT, INSERT ON dbo.AuditLog TO [DOMAIN\SUNUCUADI$];
