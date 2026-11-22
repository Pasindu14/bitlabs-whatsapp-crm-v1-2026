using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <summary>
    /// Plan 005 — brings the chatbot, calendar and clinic-CRM modules into the CRM.
    /// <para>
    /// Additive only: ten new tables, eleven new columns on Companies / Conversations / Messages, and their
    /// indexes. Nothing existing is dropped, renamed or retyped, and every new NOT NULL column carries a
    /// database default — so the previous API build keeps running against this schema, which is what makes
    /// "roll back by redeploying the old image" safe.
    /// </para>
    /// </summary>
    public partial class AddChatbotModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAutoReply",
                table: "Messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MetaCostUsd",
                table: "Messages",
                type: "numeric(10,6)",
                precision: 10,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BotAsksMade",
                table: "Conversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "BotConvertedAt",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BotFollowUpSentAt",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BotFollowUpStepsSent",
                table: "Conversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "BotPausedAt",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BotPausedByUserId",
                table: "Conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BotResumesAt",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BotState",
                table: "Conversations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // HAND-EDITED from "". BotState is an enum stored as text; an empty string on every existing
                // row fails to parse the moment the inbox loads a thread. Active is also the right meaning
                // for an existing conversation, and it is the default the previous API build relies on when
                // it inserts a conversation without knowing this column exists.
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.CreateTable(
                name: "AiReplyLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboundMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CacheReadTokens = table.Column<int>(type: "integer", nullable: false),
                    CacheWriteTokens = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    Iterations = table.Column<int>(type: "integer", nullable: false),
                    ToolCallsJson = table.Column<string>(type: "jsonb", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiReplyLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiReplyLogs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    SlotIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    BufferBeforeMinutes = table.Column<int>(type: "integer", nullable: false),
                    BufferAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    MinimumNoticeMinutes = table.Column<int>(type: "integer", nullable: false),
                    BookingHorizonDays = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalTreatmentId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentTypes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutoReplyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Terms = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    ReplyBody = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoReplyRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutoReplyRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BotFaqs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Answer = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotFaqs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BotFaqs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BotLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    WhenToShare = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BotLinks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BotProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BusinessName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BusinessDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ToneNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ContactNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CurrencyLabel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    BookingInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PreferredLanguage = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    DailyReplyLimit = table.Column<int>(type: "integer", nullable: false),
                    ResumeBotAfterIdleHours = table.Column<int>(type: "integer", nullable: false, defaultValue: 24),
                    HandoffAlertPhones = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    HandoffAlertTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    HandoffResumeMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IndustryRules = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BookingsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FollowUpSteps = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BotProfiles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessHours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartLocal = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndLocal = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessHours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessHours_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CalendarBlackouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarBlackouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarBlackouts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicCrmConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientSecret = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSuccessAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicCrmConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicCrmConnections_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BlockStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BlockEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    HoldExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReminderSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalBookingId = table.Column<int>(type: "integer", nullable: true),
                    ExternalCustomerId = table.Column<int>(type: "integer", nullable: true),
                    ExternalSyncStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExternalSyncError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExternalSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointments_AppointmentTypes_AppointmentTypeId",
                        column: x => x.AppointmentTypeId,
                        principalTable: "AppointmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_BotResumesAt",
                table: "Conversations",
                column: "BotResumesAt",
                filter: "\"BotResumesAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_PausedIdle",
                table: "Conversations",
                columns: new[] { "BotState", "LastMessageAt" },
                filter: "\"BotState\" = 'Paused'");

            migrationBuilder.CreateIndex(
                name: "IX_AiReplyLogs_CompanyId_CreatedAt",
                table: "AiReplyLogs",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiReplyLogs_ConversationId",
                table: "AiReplyLogs",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_AppointmentTypeId",
                table: "Appointments",
                column: "AppointmentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CompanyId_StartUtc",
                table: "Appointments",
                columns: new[] { "CompanyId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ContactId",
                table: "Appointments",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ConversationId",
                table: "Appointments",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_HoldExpiresAt",
                table: "Appointments",
                column: "HoldExpiresAt",
                filter: "\"HoldExpiresAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AppointmentTypes_Name",
                table: "AppointmentTypes",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_AutoReplyRules_CompanyId_Name",
                table: "AutoReplyRules",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutoReplyRules_CompanyId_Priority",
                table: "AutoReplyRules",
                columns: new[] { "CompanyId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_BotFaqs_CompanyId_SortOrder",
                table: "BotFaqs",
                columns: new[] { "CompanyId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BotLinks_CompanyId_SortOrder",
                table: "BotLinks",
                columns: new[] { "CompanyId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BotProfiles_CompanyId",
                table: "BotProfiles",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHours_CompanyId_DayOfWeek",
                table: "BusinessHours",
                columns: new[] { "CompanyId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarBlackouts_CompanyId_StartUtc",
                table: "CalendarBlackouts",
                columns: new[] { "CompanyId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ClinicCrmConnections_Company",
                table: "ClinicCrmConnections",
                column: "CompanyId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            // ── HAND-WRITTEN: the no-double-booking guarantee ────────────────────────────────────────────
            // An overlap check in application code is check-then-insert: two bookings for the same slot can
            // both pass the check before either writes. An exclusion constraint moves the decision into the
            // database, where the second writer is rejected outright. EF Core cannot express EXCLUDE.
            //
            // btree_gist supplies the equality operator class for uuid; gist alone cannot index it.
            // Available on Supabase and in stock Postgres contrib.
            migrationBuilder.Sql(@"CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // tstzrange defaults to '[)' (half-open), so an appointment ending exactly when the next begins
            // does not collide — matching AvailabilityCalculator's overlap test. Every status except
            // Cancelled occupies the slot. Expired holds are freed by being cancelled, not by a time
            // predicate: a constraint predicate must be immutable, and 'now' is not.
            migrationBuilder.Sql(@"
                ALTER TABLE ""Appointments""
                ADD CONSTRAINT ""EX_Appointments_NoOverlap""
                EXCLUDE USING gist (
                    ""CompanyId"" WITH =,
                    tstzrange(""BlockStartUtc"", ""BlockEndUtc"") WITH &&
                ) WHERE (""Status"" <> 'Cancelled');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // EX_Appointments_NoOverlap goes with the Appointments table. btree_gist is deliberately NOT
            // dropped: extensions are database-wide and something else may have come to rely on it.
            migrationBuilder.DropTable(
                name: "AiReplyLogs");

            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "AutoReplyRules");

            migrationBuilder.DropTable(
                name: "BotFaqs");

            migrationBuilder.DropTable(
                name: "BotLinks");

            migrationBuilder.DropTable(
                name: "BotProfiles");

            migrationBuilder.DropTable(
                name: "BusinessHours");

            migrationBuilder.DropTable(
                name: "CalendarBlackouts");

            migrationBuilder.DropTable(
                name: "ClinicCrmConnections");

            migrationBuilder.DropTable(
                name: "AppointmentTypes");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_BotResumesAt",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_PausedIdle",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "IsAutoReply",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MetaCostUsd",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "BotAsksMade",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BotConvertedAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BotFollowUpSentAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BotFollowUpStepsSent",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BotPausedAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BotPausedByUserId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BotResumesAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BotState",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Companies");
        }
    }
}
