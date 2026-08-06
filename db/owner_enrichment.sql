-- =====================================================================
-- Milestone 2: owner-enrichment integration schema
-- Idempotent. Run on local ScoutixDb first, then prod Azure SQL via SSMS.
-- =====================================================================

-- 1. Leads: owner + email-status columns -----------------------------------
IF COL_LENGTH('dbo.Leads', 'OwnerName') IS NULL
    ALTER TABLE dbo.Leads ADD OwnerName NVARCHAR(300) NULL;
GO
IF COL_LENGTH('dbo.Leads', 'OwnerVerified') IS NULL
    ALTER TABLE dbo.Leads ADD OwnerVerified BIT NOT NULL CONSTRAINT DF_Leads_OwnerVerified DEFAULT 0;
GO
IF COL_LENGTH('dbo.Leads', 'OwnerConfidence') IS NULL
    ALTER TABLE dbo.Leads ADD OwnerConfidence FLOAT NOT NULL CONSTRAINT DF_Leads_OwnerConfidence DEFAULT 0;
GO
IF COL_LENGTH('dbo.Leads', 'OwnerSources') IS NULL
    ALTER TABLE dbo.Leads ADD OwnerSources NVARCHAR(300) NULL;
GO
IF COL_LENGTH('dbo.Leads', 'EmailStatus') IS NULL
    ALTER TABLE dbo.Leads ADD EmailStatus INT NOT NULL CONSTRAINT DF_Leads_EmailStatus DEFAULT 0;
GO

-- 2. Engine tables (provenance + cache; mirror EnrichmentDbContext) ---------
IF OBJECT_ID('dbo.Listings', 'U') IS NULL
BEGIN
    CREATE TABLE [Listings] (
        [Id] int NOT NULL IDENTITY,
        [SourceKey] nvarchar(200) NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [Phone] nvarchar(max) NULL,
        [Email] nvarchar(max) NULL,
        [Website] nvarchar(max) NULL,
        [Address] nvarchar(max) NULL,
        [Vertical] nvarchar(80) NOT NULL,
        [City] nvarchar(120) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Listings] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_Listings_SourceKey] ON [Listings] ([SourceKey]);
END
GO

IF OBJECT_ID('dbo.CachedPages', 'U') IS NULL
BEGIN
    CREATE TABLE [CachedPages] (
        [Id] int NOT NULL IDENTITY,
        [Url] nvarchar(800) NOT NULL,
        [StatusCode] int NULL,
        [Content] nvarchar(max) NULL,
        [FetchedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CachedPages] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_CachedPages_Url] ON [CachedPages] ([Url]);
END
GO

IF OBJECT_ID('dbo.FieldCandidates', 'U') IS NULL
BEGIN
    CREATE TABLE [FieldCandidates] (
        [Id] int NOT NULL IDENTITY,
        [ListingId] int NOT NULL,
        [Field] nvarchar(40) NOT NULL,
        [Value] nvarchar(500) NOT NULL,
        [Source] nvarchar(60) NOT NULL,
        [Confidence] float NOT NULL,
        [Detail] nvarchar(1000) NULL,
        [ObservedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_FieldCandidates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FieldCandidates_Listings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [Listings] ([Id]) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX [IX_FieldCandidates_ListingId_Field_Source] ON [FieldCandidates] ([ListingId], [Field], [Source]);
END
GO

IF OBJECT_ID('dbo.ListingEnrichments', 'U') IS NULL
BEGIN
    CREATE TABLE [ListingEnrichments] (
        [ListingId] int NOT NULL,
        [OwnerName] nvarchar(300) NULL,
        [OwnerNameVerified] bit NOT NULL,
        [OwnerNameConfidence] float NOT NULL,
        [OwnerNameSources] nvarchar(300) NULL,
        [Email] nvarchar(320) NULL,
        [EmailStatus] nvarchar(20) NOT NULL,
        [EmailConfidence] float NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [FailureReason] nvarchar(500) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ListingEnrichments] PRIMARY KEY ([ListingId]),
        CONSTRAINT [FK_ListingEnrichments_Listings_ListingId] FOREIGN KEY ([ListingId]) REFERENCES [Listings] ([Id]) ON DELETE CASCADE
    );
END
GO
