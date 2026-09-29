BEGIN TRANSACTION;
GO

ALTER TABLE [Tenders] ADD [FunctionalityThreshold] int NULL;
GO

ALTER TABLE [BidEvaluations] ADD [FunctionalityScore] decimal(5,2) NULL;
GO

CREATE TABLE [CompanyDocuments] (
    [Id] int NOT NULL IDENTITY,
    [CompanyId] int NOT NULL,
    [Kind] nvarchar(32) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [ClientName] nvarchar(200) NULL,
    [YearCompleted] int NULL,
    [ContractValue] decimal(18,2) NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [StorageKey] nvarchar(200) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [Sha256] nchar(64) NOT NULL,
    [UploadedByUserId] nvarchar(450) NOT NULL,
    [UploadedAtUtc] datetime2 NOT NULL,
    [RemovedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_CompanyDocuments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CompanyDocuments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CompanyDocuments_Users_UploadedByUserId] FOREIGN KEY ([UploadedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [TenderFunctionalityCriteria] (
    [Id] int NOT NULL IDENTITY,
    [TenderId] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Weight] int NOT NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_TenderFunctionalityCriteria] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TenderFunctionalityCriteria_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [FunctionalityRatings] (
    [Id] int NOT NULL IDENTITY,
    [BidEvaluationId] int NOT NULL,
    [TenderFunctionalityCriterionId] int NOT NULL,
    [Rating] int NOT NULL,
    CONSTRAINT [PK_FunctionalityRatings] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FunctionalityRatings_BidEvaluations_BidEvaluationId] FOREIGN KEY ([BidEvaluationId]) REFERENCES [BidEvaluations] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_FunctionalityRatings_TenderFunctionalityCriteria_TenderFunctionalityCriterionId] FOREIGN KEY ([TenderFunctionalityCriterionId]) REFERENCES [TenderFunctionalityCriteria] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_CompanyDocuments_CompanyId] ON [CompanyDocuments] ([CompanyId]);
GO

CREATE UNIQUE INDEX [IX_CompanyDocuments_StorageKey] ON [CompanyDocuments] ([StorageKey]);
GO

CREATE INDEX [IX_CompanyDocuments_UploadedByUserId] ON [CompanyDocuments] ([UploadedByUserId]);
GO

CREATE UNIQUE INDEX [IX_FunctionalityRatings_BidEvaluationId_TenderFunctionalityCriterionId] ON [FunctionalityRatings] ([BidEvaluationId], [TenderFunctionalityCriterionId]);
GO

CREATE INDEX [IX_FunctionalityRatings_TenderFunctionalityCriterionId] ON [FunctionalityRatings] ([TenderFunctionalityCriterionId]);
GO

CREATE INDEX [IX_TenderFunctionalityCriteria_TenderId] ON [TenderFunctionalityCriteria] ([TenderId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260929053927_AddTrackRecordAndFunctionality', N'8.0.31');
GO

COMMIT;
GO

