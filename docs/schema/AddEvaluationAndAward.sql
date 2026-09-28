BEGIN TRANSACTION;
GO

ALTER TABLE [Tenders] ADD [BacReturnNote] nvarchar(2000) NULL;
GO

ALTER TABLE [Tenders] ADD [EvaluationSubmittedAtUtc] datetime2 NULL;
GO

ALTER TABLE [Tenders] ADD [EvaluationSubmittedByUserId] nvarchar(450) NULL;
GO

ALTER TABLE [Tenders] ADD [RecommendationReason] nvarchar(2000) NULL;
GO

CREATE TABLE [BidEvaluations] (
    [Id] int NOT NULL IDENTITY,
    [SubmissionId] int NOT NULL,
    [IsResponsive] bit NOT NULL,
    [NonResponsiveReason] nvarchar(1000) NULL,
    [BidPrice] decimal(18,2) NULL,
    [Notes] nvarchar(2000) NULL,
    [EvaluatedByUserId] nvarchar(450) NOT NULL,
    [EvaluatedAtUtc] datetime2 NOT NULL,
    [PricePoints] decimal(6,2) NULL,
    [PreferencePoints] decimal(6,2) NULL,
    [TotalPoints] decimal(6,2) NULL,
    [Rank] int NULL,
    [IsRecommended] bit NOT NULL,
    CONSTRAINT [PK_BidEvaluations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BidEvaluations_Submissions_SubmissionId] FOREIGN KEY ([SubmissionId]) REFERENCES [Submissions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_BidEvaluations_Users_EvaluatedByUserId] FOREIGN KEY ([EvaluatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_Tenders_EvaluationSubmittedByUserId] ON [Tenders] ([EvaluationSubmittedByUserId]);
GO

CREATE INDEX [IX_BidEvaluations_EvaluatedByUserId] ON [BidEvaluations] ([EvaluatedByUserId]);
GO

CREATE UNIQUE INDEX [IX_BidEvaluations_SubmissionId] ON [BidEvaluations] ([SubmissionId]);
GO

ALTER TABLE [Tenders] ADD CONSTRAINT [FK_Tenders_Users_EvaluationSubmittedByUserId] FOREIGN KEY ([EvaluationSubmittedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260928121728_AddEvaluationAndAward', N'8.0.31');
GO

COMMIT;
GO

