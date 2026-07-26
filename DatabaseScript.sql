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
    WHERE [MigrationId] = N'20260624073328_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260624073328_InitialCreate', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624073422_AddRBACPermissions'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [Category] nvarchar(100) NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624073422_AddRBACPermissions'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RoleId] nvarchar(450) NOT NULL,
        [PermissionName] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionName]),
        CONSTRAINT [FK_RolePermissions_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624073422_AddRBACPermissions'
)
BEGIN
    CREATE TABLE [UserPermissions] (
        [UserId] nvarchar(450) NOT NULL,
        [PermissionName] nvarchar(100) NOT NULL,
        [IsGranted] bit NOT NULL,
        CONSTRAINT [PK_UserPermissions] PRIMARY KEY ([UserId], [PermissionName]),
        CONSTRAINT [FK_UserPermissions_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624073422_AddRBACPermissions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Name] ON [Permissions] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624073422_AddRBACPermissions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260624073422_AddRBACPermissions', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE TABLE [ShiftGroups] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(10) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Color] nvarchar(20) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_ShiftGroups] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE TABLE [ShiftSchedules] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [StartRotationDay] int NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [CreatedByUserId] nvarchar(450) NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [PublishedDate] datetime2 NULL,
        [Notes] nvarchar(max) NULL,
        CONSTRAINT [PK_ShiftSchedules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ShiftSchedules_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE TABLE [UserGroupMemberships] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ShiftGroupId] int NOT NULL,
        [EffectiveFrom] datetime2 NOT NULL,
        [EffectiveTo] datetime2 NULL,
        [Notes] nvarchar(max) NULL,
        CONSTRAINT [PK_UserGroupMemberships] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserGroupMemberships_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserGroupMemberships_ShiftGroups_ShiftGroupId] FOREIGN KEY ([ShiftGroupId]) REFERENCES [ShiftGroups] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE TABLE [ShiftAssignments] (
        [Id] int NOT NULL IDENTITY,
        [ShiftScheduleId] int NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [ShiftGroupId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [ShiftType] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_ShiftAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ShiftAssignments_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ShiftAssignments_ShiftGroups_ShiftGroupId] FOREIGN KEY ([ShiftGroupId]) REFERENCES [ShiftGroups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ShiftAssignments_ShiftSchedules_ShiftScheduleId] FOREIGN KEY ([ShiftScheduleId]) REFERENCES [ShiftSchedules] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE TABLE [ShiftOverrides] (
        [Id] int NOT NULL IDENTITY,
        [ShiftScheduleId] int NOT NULL,
        [ShiftGroupId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [OriginalShiftType] nvarchar(max) NOT NULL,
        [NewShiftType] nvarchar(max) NOT NULL,
        [Reason] nvarchar(max) NOT NULL,
        [CreatedByUserId] nvarchar(450) NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_ShiftOverrides] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ShiftOverrides_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ShiftOverrides_ShiftGroups_ShiftGroupId] FOREIGN KEY ([ShiftGroupId]) REFERENCES [ShiftGroups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ShiftOverrides_ShiftSchedules_ShiftScheduleId] FOREIGN KEY ([ShiftScheduleId]) REFERENCES [ShiftSchedules] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE INDEX [IX_ShiftAssignments_ShiftGroupId] ON [ShiftAssignments] ([ShiftGroupId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ShiftAssignments_ShiftScheduleId_UserId_Date] ON [ShiftAssignments] ([ShiftScheduleId], [UserId], [Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE INDEX [IX_ShiftAssignments_UserId] ON [ShiftAssignments] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ShiftGroups_Name] ON [ShiftGroups] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE INDEX [IX_ShiftOverrides_CreatedByUserId] ON [ShiftOverrides] ([CreatedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE INDEX [IX_ShiftOverrides_ShiftGroupId] ON [ShiftOverrides] ([ShiftGroupId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ShiftOverrides_ShiftScheduleId_ShiftGroupId_Date] ON [ShiftOverrides] ([ShiftScheduleId], [ShiftGroupId], [Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE INDEX [IX_ShiftSchedules_CreatedByUserId] ON [ShiftSchedules] ([CreatedByUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE INDEX [IX_UserGroupMemberships_ShiftGroupId] ON [UserGroupMemberships] ([ShiftGroupId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    CREATE INDEX [IX_UserGroupMemberships_UserId] ON [UserGroupMemberships] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624120402_AddShiftMaker'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260624120402_AddShiftMaker', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624121349_AddDailyGroupShift'
)
BEGIN
    CREATE TABLE [DailyGroupShifts] (
        [Id] int NOT NULL IDENTITY,
        [ShiftScheduleId] int NOT NULL,
        [ShiftGroupId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [ShiftType] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_DailyGroupShifts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DailyGroupShifts_ShiftGroups_ShiftGroupId] FOREIGN KEY ([ShiftGroupId]) REFERENCES [ShiftGroups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DailyGroupShifts_ShiftSchedules_ShiftScheduleId] FOREIGN KEY ([ShiftScheduleId]) REFERENCES [ShiftSchedules] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624121349_AddDailyGroupShift'
)
BEGIN
    CREATE INDEX [IX_DailyGroupShifts_ShiftGroupId] ON [DailyGroupShifts] ([ShiftGroupId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624121349_AddDailyGroupShift'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DailyGroupShifts_ShiftScheduleId_ShiftGroupId_Date] ON [DailyGroupShifts] ([ShiftScheduleId], [ShiftGroupId], [Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624121349_AddDailyGroupShift'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260624121349_AddDailyGroupShift', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624123420_AddRotationPatternJson'
)
BEGIN
    ALTER TABLE [ShiftSchedules] ADD [RotationPatternJson] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624123420_AddRotationPatternJson'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260624123420_AddRotationPatternJson', N'8.0.0');
END;
GO

COMMIT;
GO

