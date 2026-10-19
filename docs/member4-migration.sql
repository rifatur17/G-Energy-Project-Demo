BEGIN TRANSACTION;
ALTER TABLE [ChargingSessions] ADD [PaymentReference] nvarchar(48) NULL;

CREATE TABLE [Wallets] (
    [Id] bigint NOT NULL IDENTITY,
    [UserId] bigint NOT NULL,
    [Balance] decimal(18,2) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Wallets] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Wallets_Balance] CHECK ([Balance] >= 0),
    CONSTRAINT [FK_Wallets_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [WalletTransactions] (
    [Id] bigint NOT NULL IDENTITY,
    [WalletId] bigint NOT NULL,
    [Reference] nvarchar(48) NOT NULL,
    [Type] varchar(24) NOT NULL,
    [Status] varchar(16) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [IdempotencyKey] nvarchar(120) NOT NULL,
    [RequestedOutcome] nvarchar(16) NOT NULL,
    [Description] nvarchar(200) NOT NULL,
    [ChargingSessionId] bigint NULL,
    [OriginalTransactionId] bigint NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CompletedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_WalletTransactions] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_WalletTransactions_Id_WalletId] UNIQUE ([Id], [WalletId]),
    CONSTRAINT [CK_WalletTransactions_Amount] CHECK ([Amount] > 0),
    CONSTRAINT [CK_WalletTransactions_State] CHECK (([Type] = 'TopUp' AND [Status] IN ('Completed','Failed') AND [ChargingSessionId] IS NULL AND [OriginalTransactionId] IS NULL) OR ([Type] = 'ChargingPayment' AND [Status] = 'Completed' AND [ChargingSessionId] IS NOT NULL AND [OriginalTransactionId] IS NULL) OR ([Type] = 'Refund' AND [Status] = 'Completed' AND [ChargingSessionId] IS NOT NULL AND [OriginalTransactionId] IS NOT NULL)),
    CONSTRAINT [CK_WalletTransactions_Time] CHECK ([CompletedAtUtc] >= [CreatedAtUtc]),
    CONSTRAINT [FK_WalletTransactions_ChargingSessions_ChargingSessionId] FOREIGN KEY ([ChargingSessionId]) REFERENCES [ChargingSessions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WalletTransactions_WalletTransactions_OriginalTransactionId_WalletId] FOREIGN KEY ([OriginalTransactionId], [WalletId]) REFERENCES [WalletTransactions] ([Id], [WalletId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WalletTransactions_Wallets_WalletId] FOREIGN KEY ([WalletId]) REFERENCES [Wallets] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_Wallets_UserId] ON [Wallets] ([UserId]);

CREATE INDEX [IX_WalletTransactions_OriginalTransactionId_WalletId] ON [WalletTransactions] ([OriginalTransactionId], [WalletId]);

CREATE UNIQUE INDEX [IX_WalletTransactions_Reference] ON [WalletTransactions] ([Reference]);

CREATE INDEX [IX_WalletTransactions_WalletId_Id] ON [WalletTransactions] ([WalletId], [Id]);

CREATE UNIQUE INDEX [IX_WalletTransactions_WalletId_IdempotencyKey] ON [WalletTransactions] ([WalletId], [IdempotencyKey]);

CREATE UNIQUE INDEX [UX_WalletTransactions_Refund] ON [WalletTransactions] ([OriginalTransactionId]) WHERE [OriginalTransactionId] IS NOT NULL;

CREATE UNIQUE INDEX [UX_WalletTransactions_SessionPayment] ON [WalletTransactions] ([ChargingSessionId]) WHERE [Type] = 'ChargingPayment' AND [Status] = 'Completed';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261019044347_AddWalletAndPayments', N'10.0.13');

COMMIT;
GO
