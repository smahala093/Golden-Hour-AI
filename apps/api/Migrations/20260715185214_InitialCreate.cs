using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GoldenHour.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "golden_hour");

            migrationBuilder.CreateTable(
                name: "AiOperations",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencySessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationType = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    LatencyMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferredLanguage = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ResourceType = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyProfiles",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    BloodGroup = table.Column<string>(type: "text", nullable: true),
                    PreferredLanguage = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ResponseMode = table.Column<string>(type: "text", nullable: false),
                    InsuranceDetails = table.Column<string>(type: "text", nullable: true),
                    DoctorContact = table.Column<string>(type: "text", nullable: true),
                    AllergyStatusCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    MedicationStatusCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    LocationPermissionReviewed = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmergencySessions",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmergencyProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    PatientRelationship = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SelectedCategory = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalInput = table.Column<string>(type: "text", nullable: true),
                    OriginalLanguage = table.Column<string>(type: "text", nullable: true),
                    NormalizedTranscript = table.Column<string>(type: "text", nullable: true),
                    IncidentFactsJson = table.Column<string>(type: "text", nullable: true),
                    ProfileSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    ProtocolId = table.Column<string>(type: "text", nullable: true),
                    ProtocolVersion = table.Column<string>(type: "text", nullable: true),
                    AiConfidence = table.Column<decimal>(type: "numeric", nullable: true),
                    InterpretationConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencySessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProviderConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookReceipts",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    DeliveryId = table.Column<string>(type: "text", nullable: false),
                    PayloadDigest = table.Column<string>(type: "text", nullable: false),
                    ProviderTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "golden_hour",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "golden_hour",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "golden_hour",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "golden_hour",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "golden_hour",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "golden_hour",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "golden_hour",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "golden_hour",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "golden_hour",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Allergies",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsSelfReported = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allergies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Allergies_EmergencyProfiles_EmergencyProfileId",
                        column: x => x.EmergencyProfileId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyContacts",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Relationship = table.Column<string>(type: "text", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyContacts_EmergencyProfiles_EmergencyProfileId",
                        column: x => x.EmergencyProfileId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MedicalConditions",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsSelfReported = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalConditions_EmergencyProfiles_EmergencyProfileId",
                        column: x => x.EmergencyProfileId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MedicalProcedures",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    IsSelfReported = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalProcedures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalProcedures_EmergencyProfiles_EmergencyProfileId",
                        column: x => x.EmergencyProfileId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Medications",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsSelfReported = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Medications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Medications_EmergencyProfiles_EmergencyProfileId",
                        column: x => x.EmergencyProfileId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreferredHospitals",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreferredHospitals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreferredHospitals_EmergencyProfiles_EmergencyProfileId",
                        column: x => x.EmergencyProfileId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SharingPreferences",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareName = table.Column<bool>(type: "boolean", nullable: false),
                    ShareApproximateAge = table.Column<bool>(type: "boolean", nullable: false),
                    ShareAllergies = table.Column<bool>(type: "boolean", nullable: false),
                    ShareConditions = table.Column<bool>(type: "boolean", nullable: false),
                    ShareMedications = table.Column<bool>(type: "boolean", nullable: false),
                    ShareEmergencyContact = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharingPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharingPreferences_EmergencyProfiles_EmergencyProfileId",
                        column: x => x.EmergencyProfileId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyLocations",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric", nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ConsentProvided = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyLocations_EmergencySessions_EmergencySessionId",
                        column: x => x.EmergencySessionId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyObservations",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyObservations_EmergencySessions_EmergencySessionId",
                        column: x => x.EmergencySessionId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyParticipants",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyParticipants_EmergencySessions_EmergencySessionId",
                        column: x => x.EmergencySessionId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmergencySummaries",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    ProtocolVersion = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencySummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencySummaries_EmergencySessions_EmergencySessionId",
                        column: x => x.EmergencySessionId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyTasks",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    IsCritical = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedParticipantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyTasks_EmergencySessions_EmergencySessionId",
                        column: x => x.EmergencySessionId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyTimelineEvents",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyTimelineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyTimelineEvents_EmergencySessions_EmergencySessionId",
                        column: x => x.EmergencySessionId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyShareTokens",
                schema: "golden_hour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyParticipantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAccessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyShareTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyShareTokens_EmergencyParticipants_EmergencyPartici~",
                        column: x => x.EmergencyParticipantId,
                        principalSchema: "golden_hour",
                        principalTable: "EmergencyParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiOperations_EmergencySessionId_CreatedAtUtc",
                schema: "golden_hour",
                table: "AiOperations",
                columns: new[] { "EmergencySessionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Allergies_EmergencyProfileId",
                schema: "golden_hour",
                table: "Allergies",
                column: "EmergencyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "golden_hour",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "golden_hour",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "golden_hour",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "golden_hour",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "golden_hour",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "golden_hour",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "golden_hour",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ResourceType_ResourceId_CreatedAtUtc",
                schema: "golden_hour",
                table: "AuditEvents",
                columns: new[] { "ResourceType", "ResourceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyContacts_EmergencyProfileId_PhoneNumber",
                schema: "golden_hour",
                table: "EmergencyContacts",
                columns: new[] { "EmergencyProfileId", "PhoneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyLocations_EmergencySessionId",
                schema: "golden_hour",
                table: "EmergencyLocations",
                column: "EmergencySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyObservations_EmergencySessionId",
                schema: "golden_hour",
                table: "EmergencyObservations",
                column: "EmergencySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyParticipants_EmergencySessionId_UserId",
                schema: "golden_hour",
                table: "EmergencyParticipants",
                columns: new[] { "EmergencySessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyProfiles_OwnerId",
                schema: "golden_hour",
                table: "EmergencyProfiles",
                column: "OwnerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencySessions_OwnerId_Status_UpdatedAtUtc",
                schema: "golden_hour",
                table: "EmergencySessions",
                columns: new[] { "OwnerId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyShareTokens_EmergencyParticipantId",
                schema: "golden_hour",
                table: "EmergencyShareTokens",
                column: "EmergencyParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyShareTokens_EmergencySessionId_ExpiresAtUtc",
                schema: "golden_hour",
                table: "EmergencyShareTokens",
                columns: new[] { "EmergencySessionId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyShareTokens_TokenHash",
                schema: "golden_hour",
                table: "EmergencyShareTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencySummaries_EmergencySessionId_Kind_CreatedAtUtc",
                schema: "golden_hour",
                table: "EmergencySummaries",
                columns: new[] { "EmergencySessionId", "Kind", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyTasks_EmergencySessionId_Status",
                schema: "golden_hour",
                table: "EmergencyTasks",
                columns: new[] { "EmergencySessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyTasks_EmergencySessionId_TaskCode",
                schema: "golden_hour",
                table: "EmergencyTasks",
                columns: new[] { "EmergencySessionId", "TaskCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyTimelineEvents_EmergencySessionId_IdempotencyKey",
                schema: "golden_hour",
                table: "EmergencyTimelineEvents",
                columns: new[] { "EmergencySessionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyTimelineEvents_EmergencySessionId_Sequence",
                schema: "golden_hour",
                table: "EmergencyTimelineEvents",
                columns: new[] { "EmergencySessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicalConditions_EmergencyProfileId",
                schema: "golden_hour",
                table: "MedicalConditions",
                column: "EmergencyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalProcedures_EmergencyProfileId",
                schema: "golden_hour",
                table: "MedicalProcedures",
                column: "EmergencyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Medications_EmergencyProfileId",
                schema: "golden_hour",
                table: "Medications",
                column: "EmergencyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Provider_ProviderMessageId",
                schema: "golden_hour",
                table: "NotificationDeliveries",
                columns: new[] { "Provider", "ProviderMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAtUtc_NextAttemptAtUtc",
                schema: "golden_hour",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAtUtc", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PreferredHospitals_EmergencyProfileId",
                schema: "golden_hour",
                table: "PreferredHospitals",
                column: "EmergencyProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                schema: "golden_hour",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_FamilyId_ExpiresAtUtc",
                schema: "golden_hour",
                table: "RefreshTokens",
                columns: new[] { "UserId", "FamilyId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SharingPreferences_EmergencyProfileId",
                schema: "golden_hour",
                table: "SharingPreferences",
                column: "EmergencyProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookReceipts_Provider_DeliveryId",
                schema: "golden_hour",
                table: "WebhookReceipts",
                columns: new[] { "Provider", "DeliveryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiOperations",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "Allergies",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencyContacts",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencyLocations",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencyObservations",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencyShareTokens",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencySummaries",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencyTasks",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencyTimelineEvents",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "MedicalConditions",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "MedicalProcedures",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "Medications",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "PreferredHospitals",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "SharingPreferences",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "WebhookReceipts",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencyParticipants",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencyProfiles",
                schema: "golden_hour");

            migrationBuilder.DropTable(
                name: "EmergencySessions",
                schema: "golden_hour");
        }
    }
}
