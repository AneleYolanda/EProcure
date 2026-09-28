BEGIN TRANSACTION;
GO

ALTER TABLE [Tenders] ADD [ApprovalRequestedAtUtc] datetime2 NULL;
GO

ALTER TABLE [Tenders] ADD [ApprovalRequestedByUserId] nvarchar(450) NULL;
GO

ALTER TABLE [Tenders] ADD [ApprovalReturnNote] nvarchar(1000) NULL;
GO

ALTER TABLE [Tenders] ADD [ApprovedAtUtc] datetime2 NULL;
GO

ALTER TABLE [Tenders] ADD [ApprovedByUserId] nvarchar(450) NULL;
GO

ALTER TABLE [Organisations] ADD [RequireTenderApproval] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

CREATE TABLE [SentNotifications] (
    [Id] bigint NOT NULL IDENTITY,
    [Key] nvarchar(200) NOT NULL,
    [SentAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_SentNotifications] PRIMARY KEY ([Id])
);
GO

UPDATE [Organisations] SET [RequireTenderApproval] = CAST(1 AS bit)
WHERE [Id] = 1;
SELECT @@ROWCOUNT;

GO

UPDATE [Organisations] SET [RequireTenderApproval] = CAST(0 AS bit)
WHERE [Id] = 2;
SELECT @@ROWCOUNT;

GO

CREATE INDEX [IX_Tenders_ApprovalRequestedByUserId] ON [Tenders] ([ApprovalRequestedByUserId]);
GO

CREATE INDEX [IX_Tenders_ApprovedByUserId] ON [Tenders] ([ApprovedByUserId]);
GO

CREATE UNIQUE INDEX [IX_SentNotifications_Key] ON [SentNotifications] ([Key]);
GO

ALTER TABLE [Tenders] ADD CONSTRAINT [FK_Tenders_Users_ApprovalRequestedByUserId] FOREIGN KEY ([ApprovalRequestedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
GO

ALTER TABLE [Tenders] ADD CONSTRAINT [FK_Tenders_Users_ApprovedByUserId] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260928154532_AddApprovalsAndReminders', N'8.0.31');
GO

COMMIT;
GO

