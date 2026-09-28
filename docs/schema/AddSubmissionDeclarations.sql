BEGIN TRANSACTION;
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Submissions]') AND [c].[name] = N'IsTaxCompliant');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Submissions] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [Submissions] ALTER COLUMN [IsTaxCompliant] bit NULL;
GO

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Submissions]') AND [c].[name] = N'IsCsdRegistered');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Submissions] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [Submissions] ALTER COLUMN [IsCsdRegistered] bit NULL;
GO

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Submissions]') AND [c].[name] = N'HasDeclaredInterest');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Submissions] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [Submissions] ALTER COLUMN [HasDeclaredInterest] bit NULL;
GO

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Submissions]') AND [c].[name] = N'ConfirmsNotRestricted');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Submissions] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [Submissions] ALTER COLUMN [ConfirmsNotRestricted] bit NULL;
GO

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Submissions]') AND [c].[name] = N'ConfirmsIndependentBid');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Submissions] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [Submissions] ALTER COLUMN [ConfirmsIndependentBid] bit NULL;
GO

ALTER TABLE [Submissions] ADD [DeclaredAtUtc] datetime2 NULL;
GO

ALTER TABLE [Submissions] ADD [InterestDetails] nvarchar(1000) NULL;
GO

ALTER TABLE [Submissions] ADD [RestrictionDetails] nvarchar(1000) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260928101524_AddSubmissionDeclarations', N'8.0.31');
GO

COMMIT;
GO

