BEGIN TRANSACTION;
GO

ALTER TABLE [Tenders] ADD [EstimatedValue] decimal(18,2) NULL;
GO

ALTER TABLE [Submissions] ADD [ReferenceNumber] nvarchar(20) NULL;
GO

ALTER TABLE [Companies] ADD [EnterpriseSize] nvarchar(32) NOT NULL DEFAULT N'Generic';
GO

UPDATE [Organisations] SET [AccentColour] = N'#1CA3EC', [LogoPath] = N'/img/orgs/rbidz-logo.png', [PrimaryColour] = N'#0F1B33'
WHERE [Id] = 1;
SELECT @@ROWCOUNT;

GO

CREATE UNIQUE INDEX [IX_Submissions_ReferenceNumber] ON [Submissions] ([ReferenceNumber]) WHERE [ReferenceNumber] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260928071333_AddDesignFields', N'8.0.31');
GO

COMMIT;
GO

