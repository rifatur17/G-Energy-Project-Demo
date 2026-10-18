BEGIN TRANSACTION;
CREATE TABLE [Stations] (
    [Id] bigint NOT NULL IDENTITY,
    [Name] nvarchar(120) NOT NULL,
    [Address] nvarchar(250) NOT NULL,
    [Latitude] decimal(9,6) NOT NULL,
    [Longitude] decimal(9,6) NOT NULL,
    [IsActive] bit NOT NULL,
    [IsDemo] bit NOT NULL,
    CONSTRAINT [PK_Stations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Stations_Coordinates] CHECK ([Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)
);

CREATE TABLE [Vehicles] (
    [Id] bigint NOT NULL IDENTITY,
    [OwnerUserId] bigint NOT NULL,
    [RegistrationNo] nvarchar(32) NOT NULL,
    [Model] nvarchar(100) NOT NULL,
    [ConnectorType] varchar(20) NOT NULL,
    [BatteryCapacityKWh] decimal(8,2) NOT NULL,
    [BatteryPercent] decimal(5,2) NOT NULL,
    [ConsumptionKWhPerKm] decimal(8,4) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Vehicles] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Vehicles_Battery] CHECK ([BatteryPercent] >= 0 AND [BatteryPercent] <= 100 AND [BatteryCapacityKWh] > 0 AND [ConsumptionKWhPerKm] > 0),
    CONSTRAINT [CK_Vehicles_Connector] CHECK ([ConnectorType] IN ('CCS2','Type2','CHAdeMO')),
    CONSTRAINT [FK_Vehicles_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Chargers] (
    [Id] bigint NOT NULL IDENTITY,
    [StationId] bigint NOT NULL,
    [ChargerCode] nvarchar(30) NOT NULL,
    [ConnectorType] varchar(20) NOT NULL,
    [PowerKW] decimal(8,2) NOT NULL,
    [PricePerKWh] decimal(10,2) NOT NULL,
    [Status] varchar(16) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Chargers] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Chargers_Connector] CHECK ([ConnectorType] IN ('CCS2','Type2','CHAdeMO')),
    CONSTRAINT [CK_Chargers_Positive] CHECK ([PowerKW] > 0 AND [PricePerKWh] > 0),
    CONSTRAINT [CK_Chargers_Status] CHECK ([Status] IN ('Available','Reserved','Charging','Offline')),
    CONSTRAINT [FK_Chargers_Stations_StationId] FOREIGN KEY ([StationId]) REFERENCES [Stations] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [VehicleDrivers] (
    [VehicleId] bigint NOT NULL,
    [DriverUserId] bigint NOT NULL,
    [CanCharge] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_VehicleDrivers] PRIMARY KEY ([VehicleId], [DriverUserId]),
    CONSTRAINT [FK_VehicleDrivers_Users_DriverUserId] FOREIGN KEY ([DriverUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VehicleDrivers_Vehicles_VehicleId] FOREIGN KEY ([VehicleId]) REFERENCES [Vehicles] ([Id]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_Chargers_StationId_ChargerCode] ON [Chargers] ([StationId], [ChargerCode]);

CREATE INDEX [IX_VehicleDrivers_DriverUserId] ON [VehicleDrivers] ([DriverUserId]);

CREATE INDEX [IX_Vehicles_OwnerUserId] ON [Vehicles] ([OwnerUserId]);

CREATE UNIQUE INDEX [IX_Vehicles_RegistrationNo] ON [Vehicles] ([RegistrationNo]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261018192412_AddVehiclesAndStationDiscovery', N'10.0.13');

COMMIT;
GO
