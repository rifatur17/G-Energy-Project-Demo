BEGIN TRANSACTION;
CREATE TABLE [WelcomeEmails] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] bigint NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [NextAttemptAtUtc] datetime2 NOT NULL,
    [SentAtUtc] datetime2 NULL,
    [Attempts] int NOT NULL,
    [LeaseToken] uniqueidentifier NOT NULL,
    [LeaseUntilUtc] datetime2 NULL,
    CONSTRAINT [PK_WelcomeEmails] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_WelcomeEmails_Attempts] CHECK ([Attempts] >= 0 AND [Attempts] <= 10),
    CONSTRAINT [FK_WelcomeEmails_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_WelcomeEmails_SentAtUtc_NextAttemptAtUtc] ON [WelcomeEmails] ([SentAtUtc], [NextAttemptAtUtc]);

CREATE UNIQUE INDEX [IX_WelcomeEmails_UserId] ON [WelcomeEmails] ([UserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261022094203_AddWelcomeEmailOutbox', N'10.0.13');

COMMIT;
GO
