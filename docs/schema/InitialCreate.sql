IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLog] (
        [Id] bigint NOT NULL IDENTITY,
        [OccurredAtUtc] datetime2 NOT NULL,
        [UserId] nvarchar(450) NULL,
        [UserEmail] nvarchar(256) NULL,
        [OrganisationId] int NULL,
        [Action] nvarchar(64) NOT NULL,
        [EntityType] nvarchar(64) NOT NULL,
        [EntityId] nvarchar(64) NOT NULL,
        [Details] nvarchar(max) NULL,
        [IpAddress] nvarchar(45) NULL,
        CONSTRAINT [PK_AuditLog] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [Companies] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [RegistrationNumber] nvarchar(20) NOT NULL,
        [TaxPin] nvarchar(20) NOT NULL,
        [CsdNumber] nvarchar(20) NOT NULL,
        [BbbeeLevel] nvarchar(32) NOT NULL,
        [Sector] nvarchar(100) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Companies] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [Organisations] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [LogoPath] nvarchar(260) NULL,
        [PrimaryColour] nvarchar(7) NOT NULL,
        [AccentColour] nvarchar(7) NOT NULL,
        [DefaultPointSystem] nvarchar(32) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Organisations] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] nvarchar(450) NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [OrganisationId] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Users_Organisations_OrganisationId] FOREIGN KEY ([OrganisationId]) REFERENCES [Organisations] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [RoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_RoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RoleClaims_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [SupplierProfiles] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [CompanyId] int NULL,
        [JobTitle] nvarchar(100) NULL,
        [ContactNumber] nvarchar(20) NULL,
        [PopiaConsentAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SupplierProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupplierProfiles_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SupplierProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [Tenders] (
        [Id] int NOT NULL IDENTITY,
        [OrganisationId] int NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [ReferenceNumber] nvarchar(50) NOT NULL,
        [Category] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [ClosingDateUtc] datetime2 NOT NULL,
        [TenderFee] decimal(18,2) NOT NULL,
        [MinimumBbbeeLevel] nvarchar(32) NULL,
        [Status] nvarchar(32) NOT NULL,
        [CreatedByUserId] nvarchar(450) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [PublishedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_Tenders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Tenders_Organisations_OrganisationId] FOREIGN KEY ([OrganisationId]) REFERENCES [Organisations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Tenders_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [UserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_UserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserClaims_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [UserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_UserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_UserLogins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [UserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_UserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_UserTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [Submissions] (
        [Id] int NOT NULL IDENTITY,
        [TenderId] int NOT NULL,
        [CompanyId] int NOT NULL,
        [SubmittedByUserId] nvarchar(450) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [IsCsdRegistered] bit NOT NULL,
        [IsTaxCompliant] bit NOT NULL,
        [DeclaredBbbeeLevel] nvarchar(32) NOT NULL,
        [HasDeclaredInterest] bit NOT NULL,
        [ConfirmsNotRestricted] bit NOT NULL,
        [ConfirmsIndependentBid] bit NOT NULL,
        [PaymentStatus] nvarchar(32) NOT NULL,
        [AmountPaid] decimal(18,2) NOT NULL,
        [PaymentReference] nvarchar(100) NULL,
        [PaidAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [SubmittedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_Submissions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Submissions_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Submissions_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Submissions_Users_SubmittedByUserId] FOREIGN KEY ([SubmittedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [TenderRequirements] (
        [Id] int NOT NULL IDENTITY,
        [TenderId] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [IsMandatory] bit NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_TenderRequirements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenderRequirements_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [AwardRecords] (
        [Id] int NOT NULL IDENTITY,
        [TenderId] int NOT NULL,
        [SubmissionId] int NOT NULL,
        [DecisionDateUtc] datetime2 NOT NULL,
        [CommitteeReference] nvarchar(100) NULL,
        [Rationale] nvarchar(4000) NOT NULL,
        [AwardedAmount] decimal(18,2) NULL,
        [RecordedByUserId] nvarchar(450) NOT NULL,
        [RecordedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AwardRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AwardRecords_Submissions_SubmissionId] FOREIGN KEY ([SubmissionId]) REFERENCES [Submissions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AwardRecords_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AwardRecords_Users_RecordedByUserId] FOREIGN KEY ([RecordedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [SubmissionStatusHistory] (
        [Id] int NOT NULL IDENTITY,
        [SubmissionId] int NOT NULL,
        [FromStatus] nvarchar(32) NULL,
        [ToStatus] nvarchar(32) NOT NULL,
        [ChangedByUserId] nvarchar(450) NOT NULL,
        [ChangedAtUtc] datetime2 NOT NULL,
        [Note] nvarchar(1000) NULL,
        CONSTRAINT [PK_SubmissionStatusHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubmissionStatusHistory_Submissions_SubmissionId] FOREIGN KEY ([SubmissionId]) REFERENCES [Submissions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SubmissionStatusHistory_Users_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE TABLE [UploadedDocuments] (
        [Id] int NOT NULL IDENTITY,
        [SubmissionId] int NOT NULL,
        [TenderRequirementId] int NULL,
        [OriginalFileName] nvarchar(255) NOT NULL,
        [StorageKey] nvarchar(200) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Sha256] nchar(64) NOT NULL,
        [UploadedByUserId] nvarchar(450) NOT NULL,
        [UploadedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_UploadedDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadedDocuments_Submissions_SubmissionId] FOREIGN KEY ([SubmissionId]) REFERENCES [Submissions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UploadedDocuments_TenderRequirements_TenderRequirementId] FOREIGN KEY ([TenderRequirementId]) REFERENCES [TenderRequirements] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UploadedDocuments_Users_UploadedByUserId] FOREIGN KEY ([UploadedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AccentColour', N'Code', N'CreatedAtUtc', N'DefaultPointSystem', N'IsActive', N'LogoPath', N'Name', N'PrimaryColour') AND [object_id] = OBJECT_ID(N'[Organisations]'))
        SET IDENTITY_INSERT [Organisations] ON;
    EXEC(N'INSERT INTO [Organisations] ([Id], [AccentColour], [Code], [CreatedAtUtc], [DefaultPointSystem], [IsActive], [LogoPath], [Name], [PrimaryColour])
    VALUES (1, N''#C9A227'', N''RBIDZ'', ''2026-09-27T00:00:00.0000000Z'', N''NinetyTen'', CAST(1 AS bit), N''/img/orgs/rbidz.svg'', N''Richards Bay Industrial Development Zone'', N''#0B2545''),
    (2, N''#E4572E'', N''MVLM'', ''2026-09-27T00:00:00.0000000Z'', N''EightyTwenty'', CAST(1 AS bit), N''/img/orgs/mvlm.svg'', N''Mzansi Valley Local Municipality'', N''#00695C'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AccentColour', N'Code', N'CreatedAtUtc', N'DefaultPointSystem', N'IsActive', N'LogoPath', N'Name', N'PrimaryColour') AND [object_id] = OBJECT_ID(N'[Organisations]'))
        SET IDENTITY_INSERT [Organisations] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConcurrencyStamp', N'Name', N'NormalizedName') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] ON;
    EXEC(N'INSERT INTO [Roles] ([Id], [ConcurrencyStamp], [Name], [NormalizedName])
    VALUES (N''2c5e174e-3b0e-446f-86af-483d56fd7210'', N''2c5e174e-3b0e-446f-86af-483d56fd7210'', N''Supplier'', N''SUPPLIER''),
    (N''8e445865-a24d-4543-a6c6-9443d048cdb9'', N''8e445865-a24d-4543-a6c6-9443d048cdb9'', N''OrgAdmin'', N''ORGADMIN''),
    (N''b5c1a2d3-7f4e-4c1a-9d2b-3e6f7a8b9c0d'', N''b5c1a2d3-7f4e-4c1a-9d2b-3e6f7a8b9c0d'', N''Evaluator'', N''EVALUATOR'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConcurrencyStamp', N'Name', N'NormalizedName') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLog_EntityType_EntityId] ON [AuditLog] ([EntityType], [EntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLog_OrganisationId_OccurredAtUtc] ON [AuditLog] ([OrganisationId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AwardRecords_RecordedByUserId] ON [AwardRecords] ([RecordedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AwardRecords_SubmissionId] ON [AwardRecords] ([SubmissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AwardRecords_TenderId] ON [AwardRecords] ([TenderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Companies_CsdNumber] ON [Companies] ([CsdNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Companies_RegistrationNumber] ON [Companies] ([RegistrationNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Organisations_Code] ON [Organisations] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RoleClaims_RoleId] ON [RoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [Roles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Submissions_CompanyId] ON [Submissions] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Submissions_SubmittedByUserId] ON [Submissions] ([SubmittedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Submissions_TenderId_CompanyId] ON [Submissions] ([TenderId], [CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubmissionStatusHistory_ChangedByUserId] ON [SubmissionStatusHistory] ([ChangedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubmissionStatusHistory_SubmissionId] ON [SubmissionStatusHistory] ([SubmissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SupplierProfiles_CompanyId] ON [SupplierProfiles] ([CompanyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SupplierProfiles_UserId] ON [SupplierProfiles] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TenderRequirements_TenderId] ON [TenderRequirements] ([TenderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Tenders_CreatedByUserId] ON [Tenders] ([CreatedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenders_OrganisationId_ReferenceNumber] ON [Tenders] ([OrganisationId], [ReferenceNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Tenders_Status_ClosingDateUtc] ON [Tenders] ([Status], [ClosingDateUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UploadedDocuments_StorageKey] ON [UploadedDocuments] ([StorageKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UploadedDocuments_SubmissionId] ON [UploadedDocuments] ([SubmissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UploadedDocuments_TenderRequirementId] ON [UploadedDocuments] ([TenderRequirementId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UploadedDocuments_UploadedByUserId] ON [UploadedDocuments] ([UploadedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserClaims_UserId] ON [UserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLogins_UserId] ON [UserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [Users] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_OrganisationId] ON [Users] ([OrganisationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [Users] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260927102301_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260927102301_InitialCreate', N'8.0.31');
END;
GO

COMMIT;
GO

