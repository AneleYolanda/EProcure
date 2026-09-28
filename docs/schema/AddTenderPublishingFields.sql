BEGIN TRANSACTION;
GO

ALTER TABLE [Tenders] ADD [CancellationReason] nvarchar(1000) NULL;
GO

ALTER TABLE [Tenders] ADD [CancelledAtUtc] datetime2 NULL;
GO

ALTER TABLE [Tenders] ADD [PointSystem] nvarchar(32) NOT NULL DEFAULT N'EightyTwenty';
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260928075829_AddTenderPublishingFields', N'8.0.31');
GO

COMMIT;
GO

