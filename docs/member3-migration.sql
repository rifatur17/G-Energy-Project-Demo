BEGIN TRANSACTION;
CREATE TABLE [Bookings] (
    [Id] bigint NOT NULL IDENTITY,
    [BookingCode] nvarchar(48) NOT NULL,
    [BookedByUserId] bigint NOT NULL,
    [VehicleId] bigint NOT NULL,
    [ChargerId] bigint NOT NULL,
    [IdempotencyKey] nvarchar(100) NOT NULL,
    [Status] varchar(16) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ExpiresAtUtc] datetime2 NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Bookings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Bookings_Expiry] CHECK ([ExpiresAtUtc] > [CreatedAtUtc]),
    CONSTRAINT [CK_Bookings_Status] CHECK ([Status] IN ('Reserved','Expired','Cancelled','Consumed')),
    CONSTRAINT [FK_Bookings_Chargers_ChargerId] FOREIGN KEY ([ChargerId]) REFERENCES [Chargers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Bookings_Users_BookedByUserId] FOREIGN KEY ([BookedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Bookings_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [Vehicles] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ChargingSessions] (
    [Id] bigint NOT NULL IDENTITY,
    [BookingId] bigint NOT NULL,
    [VehicleId] bigint NOT NULL,
    [ChargerId] bigint NOT NULL,
    [StartedByUserId] bigint NOT NULL,
    [PayerOwnerUserId] bigint NOT NULL,
    [Status] varchar(16) NOT NULL,
    [PaymentStatus] varchar(16) NOT NULL,
    [SettlementNote] nvarchar(200) NOT NULL,
    [StartedAtUtc] datetime2 NOT NULL,
    [EndedAtUtc] datetime2 NULL,
    [CapacityKWh] decimal(8,2) NOT NULL,
    [InitialBatteryPercent] decimal(5,2) NOT NULL,
    [BatteryPercent] decimal(5,2) NOT NULL,
    [ConsumptionKWhPerKm] decimal(8,4) NOT NULL,
    [PowerKW] decimal(8,2) NOT NULL,
    [TariffPerKWh] decimal(10,2) NOT NULL,
    [SimulationSpeed] decimal(8,2) NOT NULL,
    [EnergyKWh] decimal(18,6) NOT NULL,
    [Cost] decimal(18,2) NOT NULL,
    [ElapsedSeconds] decimal(18,3) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ChargingSessions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Sessions_Numbers] CHECK ([CapacityKWh] > 0 AND [PowerKW] > 0 AND [TariffPerKWh] > 0 AND [SimulationSpeed] > 0 AND [ConsumptionKWhPerKm] > 0 AND [InitialBatteryPercent] BETWEEN 0 AND 100 AND [BatteryPercent] BETWEEN 0 AND 100 AND [EnergyKWh] >= 0 AND [EnergyKWh] <= [CapacityKWh] AND [Cost] >= 0 AND [ElapsedSeconds] >= 0),
    CONSTRAINT [CK_Sessions_Status] CHECK ([Status] IN ('Active','Completed') AND [PaymentStatus] IN ('Unpaid','Paid') AND (([Status] = 'Active' AND [EndedAtUtc] IS NULL) OR ([Status] = 'Completed' AND [EndedAtUtc] IS NOT NULL AND [EndedAtUtc] >= [StartedAtUtc]))),
    CONSTRAINT [FK_ChargingSessions_Bookings_BookingId] FOREIGN KEY ([BookingId]) REFERENCES [Bookings] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ChargingSessions_Chargers_ChargerId] FOREIGN KEY ([ChargerId]) REFERENCES [Chargers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ChargingSessions_Users_PayerOwnerUserId] FOREIGN KEY ([PayerOwnerUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ChargingSessions_Users_StartedByUserId] FOREIGN KEY ([StartedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ChargingSessions_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [Vehicles] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_Bookings_BookedByUserId_CreatedAtUtc] ON [Bookings] ([BookedByUserId], [CreatedAtUtc]);

CREATE UNIQUE INDEX [IX_Bookings_BookedByUserId_IdempotencyKey] ON [Bookings] ([BookedByUserId], [IdempotencyKey]);

CREATE UNIQUE INDEX [IX_Bookings_BookingCode] ON [Bookings] ([BookingCode]);

CREATE INDEX [IX_Bookings_Status_ExpiresAtUtc] ON [Bookings] ([Status], [ExpiresAtUtc]);

CREATE UNIQUE INDEX [UX_Bookings_ReservedCharger] ON [Bookings] ([ChargerId]) WHERE [Status] = 'Reserved';

CREATE UNIQUE INDEX [UX_Bookings_ReservedVehicle] ON [Bookings] ([VehicleId]) WHERE [Status] = 'Reserved';

CREATE UNIQUE INDEX [IX_ChargingSessions_BookingId] ON [ChargingSessions] ([BookingId]);

CREATE INDEX [IX_ChargingSessions_PayerOwnerUserId] ON [ChargingSessions] ([PayerOwnerUserId]);

CREATE INDEX [IX_ChargingSessions_StartedByUserId_StartedAtUtc] ON [ChargingSessions] ([StartedByUserId], [StartedAtUtc]);

CREATE UNIQUE INDEX [UX_Sessions_ActiveCharger] ON [ChargingSessions] ([ChargerId]) WHERE [Status] = 'Active';

CREATE UNIQUE INDEX [UX_Sessions_ActiveVehicle] ON [ChargingSessions] ([VehicleId]) WHERE [Status] = 'Active';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261019041722_AddBookingAndCharging', N'10.0.13');

COMMIT;
GO

