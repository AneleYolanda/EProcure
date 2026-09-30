BEGIN TRANSACTION;
GO

CREATE TABLE [ComplianceDocuments] (
    [Id] int NOT NULL IDENTITY,
    [CompanyId] int NOT NULL,
    [Type] nvarchar(32) NOT NULL,
    [IssuedOn] date NOT NULL,
    [ExpiresOn] date NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [StorageKey] nvarchar(200) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [Sha256] nchar(64) NOT NULL,
    [UploadedByUserId] nvarchar(450) NOT NULL,
    [UploadedAtUtc] datetime2 NOT NULL,
    [ArchivedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_ComplianceDocuments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ComplianceDocuments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ComplianceDocuments_Users_UploadedByUserId] FOREIGN KEY ([UploadedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_ComplianceDocuments_CompanyId_Type_ArchivedAtUtc] ON [ComplianceDocuments] ([CompanyId], [Type], [ArchivedAtUtc]);
GO

CREATE INDEX [IX_ComplianceDocuments_ExpiresOn] ON [ComplianceDocuments] ([ExpiresOn]);
GO

CREATE UNIQUE INDEX [IX_ComplianceDocuments_StorageKey] ON [ComplianceDocuments] ([StorageKey]);
GO

CREATE INDEX [IX_ComplianceDocuments_UploadedByUserId] ON [ComplianceDocuments] ([UploadedByUserId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260929231119_AddComplianceDocuments', N'8.0.31');
GO

COMMIT;
GO

