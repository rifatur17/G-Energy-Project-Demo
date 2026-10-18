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
CREATE SEQUENCE [UserCodeNumbers] START WITH 1001 INCREMENT BY 1 NO CYCLE;

CREATE TABLE [Users] (
    [Id] bigint NOT NULL IDENTITY,
    [UserCode] nvarchar(24) NOT NULL,
    [FullName] nvarchar(100) NOT NULL,
    [Email] nvarchar(254) NOT NULL,
    [NormalizedEmail] nvarchar(254) NOT NULL,
    [Phone] nvarchar(20) NOT NULL,
    [PasswordHash] nvarchar(512) NOT NULL,
    [Role] varchar(16) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Users_Role] CHECK ([Role] IN ('Admin','CarOwner','CarDriver'))
);

CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);

CREATE UNIQUE INDEX [IX_Users_UserCode] ON [Users] ([UserCode]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261018185818_InitialIdentity', N'10.0.13');

COMMIT;
GO

