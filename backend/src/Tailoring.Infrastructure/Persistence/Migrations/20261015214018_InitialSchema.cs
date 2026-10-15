using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tailoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
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
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                name: "Businesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Businesses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToAddress = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TextBody = table.Column<string>(type: "text", nullable: false),
                    HtmlBody = table.Column<string>(type: "text", nullable: true),
                    TemplateKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BodyPurgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOutbox", x => x.Id);
                    table.CheckConstraint("CK_EmailOutbox_Attempts", "\"Attempts\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
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
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
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
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
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
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
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
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
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
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CurrentTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IdleExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AbsoluteExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessHolidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessHolidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessHolidays_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessHours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessHours", x => x.Id);
                    table.CheckConstraint("CK_BusinessHours_Day", "\"DayOfWeek\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_BusinessHours_Times", "(\"IsOpen\" = FALSE AND \"OpensAt\" IS NULL AND \"ClosesAt\" IS NULL) OR (\"IsOpen\" = TRUE AND \"OpensAt\" IS NOT NULL AND \"ClosesAt\" IS NOT NULL AND \"OpensAt\" < \"ClosesAt\")");
                    table.ForeignKey(
                        name: "FK_BusinessHours_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessRoles", x => x.Id);
                    table.UniqueConstraint("AK_BusinessRoles_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_BusinessRoles_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    NormalizedPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AddressLine = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    GarmentPreferences = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MergedIntoCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.UniqueConstraint("AK_Customers_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_Customers_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_Customers_MergedIntoCustomerId_BusinessId",
                        columns: x => new { x.MergedIntoCustomerId, x.BusinessId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DesignCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignCategories", x => x.Id);
                    table.UniqueConstraint("AK_DesignCategories_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_DesignCategories_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DesignCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignCollections", x => x.Id);
                    table.UniqueConstraint("AK_DesignCollections_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_DesignCollections_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentCounters",
                columns: table => new
                {
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCounters", x => new { x.BusinessId, x.Kind, x.Year });
                    table.ForeignKey(
                        name: "FK_DocumentCounters_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EntitlementOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Value = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntitlementOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntitlementOverrides_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                    table.UniqueConstraint("AK_ExpenseCategories_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_ExpenseCategories_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    LocationIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invitations_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.UniqueConstraint("AK_Locations_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_Locations_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementTemplates", x => x.Id);
                    table.UniqueConstraint("AK_MeasurementTemplates_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_MeasurementTemplates_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.UniqueConstraint("AK_MediaAssets_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_MediaAssets_Dimensions", "\"Width\" > 0 AND \"Height\" > 0");
                    table.CheckConstraint("CK_MediaAssets_Size", "\"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_MediaAssets_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsOwner = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DeactivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                    table.UniqueConstraint("AK_Memberships_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_Memberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Memberships_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderCounters",
                columns: table => new
                {
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCounters", x => new { x.BusinessId, x.Year });
                    table.ForeignKey(
                        name: "FK_OrderCounters_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PricingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultHourlyRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OverheadPercent = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetPercent = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    TaxPercent = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    RoundingStep = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingProfiles", x => x.Id);
                    table.CheckConstraint("CK_PricingProfiles_Ranges", "\"DefaultHourlyRate\" >= 0 AND \"OverheadPercent\" BETWEEN 0 AND 200 AND \"TaxPercent\" BETWEEN 0 AND 100 AND \"RoundingStep\" >= 0");
                    table.CheckConstraint("CK_PricingProfiles_Target", "(\"Method\" = 'Margin' AND \"TargetPercent\" BETWEEN 0 AND 95) OR (\"Method\" = 'Markup' AND \"TargetPercent\" BETWEEN 0 AND 1000)");
                    table.ForeignKey(
                        name: "FK_PricingProfiles_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SectionPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SectionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Variant = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PropsJson = table.Column<string>(type: "jsonb", nullable: false),
                    StyleJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SectionPresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SectionPresets_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Storefronts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DraftJson = table.Column<string>(type: "text", nullable: false),
                    DraftRevision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublishedVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    OpeningHours = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    WorkflowInquiry = table.Column<bool>(type: "boolean", nullable: false),
                    WorkflowBooking = table.Column<bool>(type: "boolean", nullable: false),
                    WorkflowCustomOrder = table.Column<bool>(type: "boolean", nullable: false),
                    WorkflowContact = table.Column<bool>(type: "boolean", nullable: false),
                    AllowSearchIndexing = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Storefronts", x => x.Id);
                    table.UniqueConstraint("AK_Storefronts_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_Storefronts_PublishedNeedsVersion", "\"Status\" <> 'Published' OR \"PublishedVersionId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Storefronts_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UsageCounters",
                columns: table => new
                {
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageCounters", x => new { x.BusinessId, x.Key, x.PeriodStartUtc });
                    table.CheckConstraint("CK_UsageCounters_NonNegative", "\"Count\" >= 0");
                    table.ForeignKey(
                        name: "FK_UsageCounters_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    GarmentCategories = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workflows", x => x.Id);
                    table.UniqueConstraint("AK_Workflows_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_Workflows_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanEntitlements",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Value = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanEntitlements", x => new { x.PlanId, x.Key });
                    table.ForeignKey(
                        name: "FK_PlanEntitlements_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_RolePermissions_BusinessRoles_RoleId_BusinessId",
                        columns: x => new { x.RoleId, x.BusinessId },
                        principalTable: "BusinessRoles",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCommunications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCommunications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCommunications_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCommunications_Customers_CustomerId_BusinessId",
                        columns: x => new { x.CustomerId, x.BusinessId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Designs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    GarmentType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    Fabrics = table.Column<List<string>>(type: "text[]", nullable: false),
                    Colours = table.Column<List<string>>(type: "text[]", nullable: false),
                    Sizes = table.Column<List<string>>(type: "text[]", nullable: false),
                    BasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ProductionDays = table.Column<int>(type: "integer", nullable: true),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    Visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ParentDesignId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designs", x => x.Id);
                    table.UniqueConstraint("AK_Designs_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_Designs_ArchivedIsPrivate", "\"Status\" <> 'Archived' OR \"Visibility\" = 'Private'");
                    table.CheckConstraint("CK_Designs_PriceHasCurrency", "\"BasePrice\" IS NULL OR \"CurrencyCode\" IS NOT NULL");
                    table.CheckConstraint("CK_Designs_PriceNonNegative", "\"BasePrice\" IS NULL OR \"BasePrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_Designs_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Designs_DesignCategories_CategoryId_BusinessId",
                        columns: x => new { x.CategoryId, x.BusinessId },
                        principalTable: "DesignCategories",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Designs_Designs_ParentDesignId_BusinessId",
                        columns: x => new { x.ParentDesignId, x.BusinessId },
                        principalTable: "Designs",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Vendor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.CheckConstraint("CK_Expenses_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_Expenses_VoidHasReason", "\"VoidedAtUtc\" IS NULL OR \"VoidReason\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Expenses_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_ExpenseCategories_CategoryId_BusinessId",
                        columns: x => new { x.CategoryId, x.BusinessId },
                        principalTable: "ExpenseCategories",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PromisedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveryMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeliveryInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    QuotedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadyAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.UniqueConstraint("AK_Orders_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_Orders_Money", "\"Subtotal\" >= 0 AND \"Discount\" >= 0 AND \"Discount\" <= \"Subtotal\" AND \"Total\" = \"Subtotal\" - \"Discount\"");
                    table.ForeignKey(
                        name: "FK_Orders_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId_BusinessId",
                        columns: x => new { x.CustomerId, x.BusinessId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Locations_LocationId_BusinessId",
                        columns: x => new { x.LocationId, x.BusinessId },
                        principalTable: "Locations",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CurrentRevision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementSets", x => x.Id);
                    table.UniqueConstraint("AK_MeasurementSets_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_MeasurementSets_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementSets_Customers_CustomerId_BusinessId",
                        columns: x => new { x.CustomerId, x.BusinessId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementSets_MeasurementTemplates_TemplateId_BusinessId",
                        columns: x => new { x.TemplateId, x.BusinessId },
                        principalTable: "MeasurementTemplates",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementTemplateFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    MinValue = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    MaxValue = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementTemplateFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeasurementTemplateFields_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementTemplateFields_MeasurementTemplates_TemplateId_B~",
                        columns: x => new { x.TemplateId, x.BusinessId },
                        principalTable: "MeasurementTemplates",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MembershipLocations",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipLocations", x => new { x.MembershipId, x.LocationId });
                    table.ForeignKey(
                        name: "FK_MembershipLocations_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MembershipLocations_Locations_LocationId_BusinessId",
                        columns: x => new { x.LocationId, x.BusinessId },
                        principalTable: "Locations",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MembershipLocations_Memberships_MembershipId_BusinessId",
                        columns: x => new { x.MembershipId, x.BusinessId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MembershipRoles",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipRoles", x => new { x.MembershipId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_MembershipRoles_BusinessRoles_RoleId_BusinessId",
                        columns: x => new { x.RoleId, x.BusinessId },
                        principalTable: "BusinessRoles",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MembershipRoles_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MembershipRoles_Memberships_MembershipId_BusinessId",
                        columns: x => new { x.MembershipId, x.BusinessId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkerSkills",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Skill = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerSkills", x => new { x.MembershipId, x.Skill });
                    table.ForeignKey(
                        name: "FK_WorkerSkills_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSkills_Memberships_MembershipId_BusinessId",
                        columns: x => new { x.MembershipId, x.BusinessId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontDomains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorefrontId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    VerificationToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorefrontDomains_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontDomains_Storefronts_StorefrontId_BusinessId",
                        columns: x => new { x.StorefrontId, x.BusinessId },
                        principalTable: "Storefronts",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorefrontId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DocumentJson = table.Column<string>(type: "jsonb", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RolledBackFromVersion = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontVersions", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontVersions_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_StorefrontVersions_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontVersions_Storefronts_StorefrontId_BusinessId",
                        columns: x => new { x.StorefrontId, x.BusinessId },
                        principalTable: "Storefronts",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RequiredSkill = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    DefaultMinutes = table.Column<int>(type: "integer", nullable: true),
                    IsConditional = table.Column<bool>(type: "boolean", nullable: false),
                    CompletionChecks = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stages", x => x.Id);
                    table.UniqueConstraint("AK_Stages_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_Stages_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stages_Workflows_WorkflowId_BusinessId",
                        columns: x => new { x.WorkflowId, x.BusinessId },
                        principalTable: "Workflows",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DesignCollectionItems",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DesignId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignCollectionItems", x => new { x.CollectionId, x.DesignId });
                    table.ForeignKey(
                        name: "FK_DesignCollectionItems_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DesignCollectionItems_DesignCollections_CollectionId_Busine~",
                        columns: x => new { x.CollectionId, x.BusinessId },
                        principalTable: "DesignCollections",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DesignCollectionItems_Designs_DesignId_BusinessId",
                        columns: x => new { x.DesignId, x.BusinessId },
                        principalTable: "Designs",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DesignImages",
                columns: table => new
                {
                    DesignId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    AltText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignImages", x => new { x.DesignId, x.MediaAssetId });
                    table.ForeignKey(
                        name: "FK_DesignImages_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DesignImages_Designs_DesignId_BusinessId",
                        columns: x => new { x.DesignId, x.BusinessId },
                        principalTable: "Designs",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DesignImages_MediaAssets_MediaAssetId_BusinessId",
                        columns: x => new { x.MediaAssetId, x.BusinessId },
                        principalTable: "MediaAssets",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Inquiries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorefrontId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DesignId = table.Column<Guid>(type: "uuid", nullable: true),
                    DesignNameSnapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    GarmentType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Budget = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PreferredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ConvertedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    InternalNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    HandledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    HandledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inquiries", x => x.Id);
                    table.CheckConstraint("CK_Inquiries_Contactable", "\"Email\" IS NOT NULL OR \"Phone\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Inquiries_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Inquiries_Designs_DesignId_BusinessId",
                        columns: x => new { x.DesignId, x.BusinessId },
                        principalTable: "Designs",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Inquiries_Storefronts_StorefrontId_BusinessId",
                        columns: x => new { x.StorefrontId, x.BusinessId },
                        principalTable: "Storefronts",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScanStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.UniqueConstraint("AK_Documents_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_Documents_Size", "\"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_Documents_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Customers_CustomerId_BusinessId",
                        columns: x => new { x.CustomerId, x.BusinessId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Orders_OrderId_BusinessId",
                        columns: x => new { x.OrderId, x.BusinessId },
                        principalTable: "Orders",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Fittings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fittings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fittings_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fittings_Customers_CustomerId_BusinessId",
                        columns: x => new { x.CustomerId, x.BusinessId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fittings_Locations_LocationId_BusinessId",
                        columns: x => new { x.LocationId, x.BusinessId },
                        principalTable: "Locations",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fittings_Orders_OrderId_BusinessId",
                        columns: x => new { x.OrderId, x.BusinessId },
                        principalTable: "Orders",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Terms = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.UniqueConstraint("AK_Invoices_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_Invoices_Amounts", "\"Subtotal\" >= 0 AND \"TaxTotal\" >= 0 AND \"Total\" = \"Subtotal\" + \"TaxTotal\"");
                    table.CheckConstraint("CK_Invoices_NumberWhenIssued", "\"Status\" = 'Draft' OR \"Number\" IS NOT NULL");
                    table.CheckConstraint("CK_Invoices_VoidHasReason", "\"Status\" <> 'Void' OR \"VoidReason\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Invoices_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Customers_CustomerId_BusinessId",
                        columns: x => new { x.CustomerId, x.BusinessId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Orders_OrderId_BusinessId",
                        columns: x => new { x.OrderId, x.BusinessId },
                        principalTable: "Orders",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    SetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ValuesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeasurementRevisions_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementRevisions_MeasurementSets_SetId_BusinessId",
                        columns: x => new { x.SetId, x.BusinessId },
                        principalTable: "MeasurementSets",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderGarments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Fabric = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Colour = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    StyleInstructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AlterationNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DesignId = table.Column<Guid>(type: "uuid", nullable: true),
                    DesignNameSnapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DesignPriceSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PricingSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    PriceOverrideReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MeasurementSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    MeasurementRevision = table.Column<int>(type: "integer", nullable: true),
                    MeasurementSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderGarments", x => x.Id);
                    table.UniqueConstraint("AK_OrderGarments_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_OrderGarments_Qty", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_OrderGarments_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderGarments_Designs_DesignId_BusinessId",
                        columns: x => new { x.DesignId, x.BusinessId },
                        principalTable: "Designs",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderGarments_MeasurementSets_MeasurementSetId_BusinessId",
                        columns: x => new { x.MeasurementSetId, x.BusinessId },
                        principalTable: "MeasurementSets",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderGarments_Orders_OrderId_BusinessId",
                        columns: x => new { x.OrderId, x.BusinessId },
                        principalTable: "Orders",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontVersionMedia",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontVersionMedia", x => new { x.VersionId, x.MediaAssetId });
                    table.ForeignKey(
                        name: "FK_StorefrontVersionMedia_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontVersionMedia_MediaAssets_MediaAssetId_BusinessId",
                        columns: x => new { x.MediaAssetId, x.BusinessId },
                        principalTable: "MediaAssets",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontVersionMedia_StorefrontVersions_VersionId_Busines~",
                        columns: x => new { x.VersionId, x.BusinessId },
                        principalTable: "StorefrontVersions",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageDependencies",
                columns: table => new
                {
                    StageId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependsOnStageId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageDependencies", x => new { x.StageId, x.DependsOnStageId });
                    table.CheckConstraint("CK_StageDependencies_NotSelf", "\"StageId\" <> \"DependsOnStageId\"");
                    table.ForeignKey(
                        name: "FK_StageDependencies_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StageDependencies_Stages_DependsOnStageId_BusinessId",
                        columns: x => new { x.DependsOnStageId, x.BusinessId },
                        principalTable: "Stages",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageDependencies_Stages_StageId_BusinessId",
                        columns: x => new { x.StageId, x.BusinessId },
                        principalTable: "Stages",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.CheckConstraint("CK_CreditNotes_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CreditNotes_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Invoices_InvoiceId_BusinessId",
                        columns: x => new { x.InvoiceId, x.BusinessId },
                        principalTable: "Invoices",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_InvoiceLines_Amounts", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"Discount\" >= 0 AND \"NetAmount\" >= 0 AND \"LineTotal\" = \"NetAmount\" + \"TaxAmount\"");
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId_BusinessId",
                        columns: x => new { x.InvoiceId, x.BusinessId },
                        principalTable: "Invoices",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.UniqueConstraint("AK_Payments_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_Payments_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_Payments_ReasonWhenClosed", "\"Status\" NOT IN ('Rejected','Reversed') OR \"StatusReason\" IS NOT NULL");
                    table.CheckConstraint("CK_Payments_VerifiedHasVerifier", "\"Status\" <> 'Verified' OR (\"VerifiedAtUtc\" IS NOT NULL AND \"VerifiedByUserId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Payments_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Invoices_InvoiceId_BusinessId",
                        columns: x => new { x.InvoiceId, x.BusinessId },
                        principalTable: "Invoices",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderGarmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageId = table.Column<Guid>(type: "uuid", nullable: true),
                    StageName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StageOrder = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    AssignedMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequiredSkill = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: true),
                    PlannedStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlannedEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StatusBeforeBlock = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BlockedType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BlockedReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletionNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequiredChecks = table.Column<List<string>>(type: "text[]", nullable: false),
                    CompletedChecks = table.Column<List<string>>(type: "text[]", nullable: false),
                    RequiredMaterialsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsConditional = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionTasks", x => x.Id);
                    table.UniqueConstraint("AK_ProductionTasks_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_ProductionTasks_Dates", "\"CompletedAtUtc\" IS NULL OR \"Status\" = 'Completed'");
                    table.CheckConstraint("CK_ProductionTasks_Qty", "\"Quantity\" > 0 AND (\"EstimatedMinutes\" IS NULL OR \"EstimatedMinutes\" > 0)");
                    table.ForeignKey(
                        name: "FK_ProductionTasks_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionTasks_Memberships_AssignedMembershipId_BusinessId",
                        columns: x => new { x.AssignedMembershipId, x.BusinessId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionTasks_OrderGarments_OrderGarmentId_BusinessId",
                        columns: x => new { x.OrderGarmentId, x.BusinessId },
                        principalTable: "OrderGarments",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionTasks_Orders_OrderId_BusinessId",
                        columns: x => new { x.OrderId, x.BusinessId },
                        principalTable: "Orders",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.UniqueConstraint("AK_Receipts_Id_BusinessId", x => new { x.Id, x.BusinessId });
                    table.CheckConstraint("CK_Receipts_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_Receipts_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Payments_PaymentId_BusinessId",
                        columns: x => new { x.PaymentId, x.BusinessId },
                        principalTable: "Payments",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskDependencies",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependsOnTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskDependencies", x => new { x.TaskId, x.DependsOnTaskId });
                    table.CheckConstraint("CK_TaskDependencies_NotSelf", "\"TaskId\" <> \"DependsOnTaskId\"");
                    table.ForeignKey(
                        name: "FK_TaskDependencies_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskDependencies_ProductionTasks_DependsOnTaskId_BusinessId",
                        columns: x => new { x.DependsOnTaskId, x.BusinessId },
                        principalTable: "ProductionTasks",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskDependencies_ProductionTasks_TaskId_BusinessId",
                        columns: x => new { x.TaskId, x.BusinessId },
                        principalTable: "ProductionTasks",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskEvents_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskEvents_ProductionTasks_TaskId_BusinessId",
                        columns: x => new { x.TaskId, x.BusinessId },
                        principalTable: "ProductionTasks",
                        principalColumns: new[] { "Id", "BusinessId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_BusinessId",
                table: "AuditLogs",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_BusinessId_CreatedAtUtc",
                table: "AuditLogs",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHolidays_BusinessId",
                table: "BusinessHolidays",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHolidays_BusinessId_Date",
                table: "BusinessHolidays",
                columns: new[] { "BusinessId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHours_BusinessId",
                table: "BusinessHours",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHours_BusinessId_DayOfWeek",
                table: "BusinessHours",
                columns: new[] { "BusinessId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessRoles_BusinessId",
                table: "BusinessRoles",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessRoles_BusinessId_Name",
                table: "BusinessRoles",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_BusinessId",
                table: "CreditNotes",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_BusinessId_Number",
                table: "CreditNotes",
                columns: new[] { "BusinessId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceId_BusinessId",
                table: "CreditNotes",
                columns: new[] { "InvoiceId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCommunications_BusinessId",
                table: "CustomerCommunications",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCommunications_CustomerId_BusinessId",
                table: "CustomerCommunications",
                columns: new[] { "CustomerId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCommunications_CustomerId_CreatedAtUtc",
                table: "CustomerCommunications",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId",
                table: "Customers",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_FullName",
                table: "Customers",
                columns: new[] { "BusinessId", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_NormalizedEmail",
                table: "Customers",
                columns: new[] { "BusinessId", "NormalizedEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_NormalizedPhone",
                table: "Customers",
                columns: new[] { "BusinessId", "NormalizedPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_MergedIntoCustomerId_BusinessId",
                table: "Customers",
                columns: new[] { "MergedIntoCustomerId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignCategories_BusinessId",
                table: "DesignCategories",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_DesignCategories_BusinessId_Name",
                table: "DesignCategories",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesignCollectionItems_BusinessId",
                table: "DesignCollectionItems",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_DesignCollectionItems_CollectionId_BusinessId",
                table: "DesignCollectionItems",
                columns: new[] { "CollectionId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignCollectionItems_CollectionId_SortOrder",
                table: "DesignCollectionItems",
                columns: new[] { "CollectionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignCollectionItems_DesignId",
                table: "DesignCollectionItems",
                column: "DesignId");

            migrationBuilder.CreateIndex(
                name: "IX_DesignCollectionItems_DesignId_BusinessId",
                table: "DesignCollectionItems",
                columns: new[] { "DesignId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignCollections_BusinessId",
                table: "DesignCollections",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_DesignCollections_BusinessId_Slug",
                table: "DesignCollections",
                columns: new[] { "BusinessId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesignImages_BusinessId",
                table: "DesignImages",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_DesignImages_DesignId_BusinessId",
                table: "DesignImages",
                columns: new[] { "DesignId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignImages_DesignId_SortOrder",
                table: "DesignImages",
                columns: new[] { "DesignId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignImages_MediaAssetId",
                table: "DesignImages",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DesignImages_MediaAssetId_BusinessId",
                table: "DesignImages",
                columns: new[] { "MediaAssetId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Designs_BusinessId",
                table: "Designs",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Designs_BusinessId_Slug",
                table: "Designs",
                columns: new[] { "BusinessId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Designs_BusinessId_Status_Visibility",
                table: "Designs",
                columns: new[] { "BusinessId", "Status", "Visibility" });

            migrationBuilder.CreateIndex(
                name: "IX_Designs_CategoryId_BusinessId",
                table: "Designs",
                columns: new[] { "CategoryId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Designs_ParentDesignId_BusinessId",
                table: "Designs",
                columns: new[] { "ParentDesignId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCounters_BusinessId",
                table: "DocumentCounters",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BusinessId",
                table: "Documents",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BusinessId_CreatedAtUtc",
                table: "Documents",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BusinessId_CustomerId",
                table: "Documents",
                columns: new[] { "BusinessId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BusinessId_OrderId",
                table: "Documents",
                columns: new[] { "BusinessId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CustomerId_BusinessId",
                table: "Documents",
                columns: new[] { "CustomerId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrderId_BusinessId",
                table: "Documents",
                columns: new[] { "OrderId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_BusinessId",
                table: "EmailOutbox",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_NextAttemptUtc",
                table: "EmailOutbox",
                column: "NextAttemptUtc",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementOverrides_BusinessId",
                table: "EntitlementOverrides",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementOverrides_BusinessId_Key",
                table: "EntitlementOverrides",
                columns: new[] { "BusinessId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_BusinessId",
                table: "ExpenseCategories",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_BusinessId_Name",
                table: "ExpenseCategories",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId",
                table: "Expenses",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_ExpenseDate",
                table: "Expenses",
                columns: new[] { "BusinessId", "ExpenseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryId_BusinessId",
                table: "Expenses",
                columns: new[] { "CategoryId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Fittings_BusinessId",
                table: "Fittings",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Fittings_BusinessId_ScheduledAtUtc",
                table: "Fittings",
                columns: new[] { "BusinessId", "ScheduledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Fittings_CustomerId_BusinessId",
                table: "Fittings",
                columns: new[] { "CustomerId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Fittings_LocationId_BusinessId",
                table: "Fittings",
                columns: new[] { "LocationId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Fittings_OrderId_BusinessId",
                table: "Fittings",
                columns: new[] { "OrderId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Inquiries_BusinessId",
                table: "Inquiries",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Inquiries_BusinessId_Status_CreatedAtUtc",
                table: "Inquiries",
                columns: new[] { "BusinessId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Inquiries_DesignId_BusinessId",
                table: "Inquiries",
                columns: new[] { "DesignId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Inquiries_StorefrontId_BusinessId",
                table: "Inquiries",
                columns: new[] { "StorefrontId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Inquiries_StorefrontId_IpHash_CreatedAtUtc",
                table: "Inquiries",
                columns: new[] { "StorefrontId", "IpHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_BusinessId",
                table: "Invitations",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_BusinessId_Email",
                table: "Invitations",
                columns: new[] { "BusinessId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TokenHash",
                table: "Invitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_BusinessId",
                table: "InvoiceLines",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId_BusinessId",
                table: "InvoiceLines",
                columns: new[] { "InvoiceId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId_LineNumber",
                table: "InvoiceLines",
                columns: new[] { "InvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BusinessId",
                table: "Invoices",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BusinessId_IdempotencyKey",
                table: "Invoices",
                columns: new[] { "BusinessId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BusinessId_Number",
                table: "Invoices",
                columns: new[] { "BusinessId", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BusinessId_Status_DueDate",
                table: "Invoices",
                columns: new[] { "BusinessId", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId",
                table: "Invoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId_BusinessId",
                table: "Invoices",
                columns: new[] { "CustomerId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrderId",
                table: "Invoices",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrderId_BusinessId",
                table: "Invoices",
                columns: new[] { "OrderId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_BusinessId",
                table: "Locations",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_BusinessId_Name",
                table: "Locations",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementRevisions_BusinessId",
                table: "MeasurementRevisions",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementRevisions_SetId_BusinessId",
                table: "MeasurementRevisions",
                columns: new[] { "SetId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementRevisions_SetId_RevisionNumber",
                table: "MeasurementRevisions",
                columns: new[] { "SetId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSets_BusinessId",
                table: "MeasurementSets",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSets_BusinessId_CustomerId_Label",
                table: "MeasurementSets",
                columns: new[] { "BusinessId", "CustomerId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSets_CustomerId_BusinessId",
                table: "MeasurementSets",
                columns: new[] { "CustomerId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementSets_TemplateId_BusinessId",
                table: "MeasurementSets",
                columns: new[] { "TemplateId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementTemplateFields_BusinessId",
                table: "MeasurementTemplateFields",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementTemplateFields_TemplateId_BusinessId",
                table: "MeasurementTemplateFields",
                columns: new[] { "TemplateId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementTemplateFields_TemplateId_Key",
                table: "MeasurementTemplateFields",
                columns: new[] { "TemplateId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementTemplates_BusinessId",
                table: "MeasurementTemplates",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementTemplates_BusinessId_Name",
                table: "MeasurementTemplates",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_BusinessId",
                table: "MediaAssets",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_BusinessId_CreatedAtUtc",
                table: "MediaAssets",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipLocations_BusinessId",
                table: "MembershipLocations",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipLocations_LocationId_BusinessId",
                table: "MembershipLocations",
                columns: new[] { "LocationId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipLocations_MembershipId_BusinessId",
                table: "MembershipLocations",
                columns: new[] { "MembershipId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRoles_BusinessId",
                table: "MembershipRoles",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRoles_MembershipId_BusinessId",
                table: "MembershipRoles",
                columns: new[] { "MembershipId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRoles_RoleId_BusinessId",
                table: "MembershipRoles",
                columns: new[] { "RoleId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_BusinessId",
                table: "Memberships",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_BusinessId_UserId",
                table: "Memberships",
                columns: new[] { "BusinessId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId",
                table: "Memberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderCounters_BusinessId",
                table: "OrderCounters",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderGarments_BusinessId",
                table: "OrderGarments",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderGarments_DesignId",
                table: "OrderGarments",
                column: "DesignId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderGarments_DesignId_BusinessId",
                table: "OrderGarments",
                columns: new[] { "DesignId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderGarments_MeasurementSetId_BusinessId",
                table: "OrderGarments",
                columns: new[] { "MeasurementSetId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderGarments_OrderId_BusinessId",
                table: "OrderGarments",
                columns: new[] { "OrderId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderGarments_OrderId_LineNumber",
                table: "OrderGarments",
                columns: new[] { "OrderId", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusinessId",
                table: "Orders",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusinessId_CustomerId",
                table: "Orders",
                columns: new[] { "BusinessId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusinessId_IdempotencyKey",
                table: "Orders",
                columns: new[] { "BusinessId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusinessId_OrderNumber",
                table: "Orders",
                columns: new[] { "BusinessId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusinessId_Status_PromisedDate",
                table: "Orders",
                columns: new[] { "BusinessId", "Status", "PromisedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_BusinessId",
                table: "Orders",
                columns: new[] { "CustomerId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_LocationId_BusinessId",
                table: "Orders",
                columns: new[] { "LocationId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BusinessId",
                table: "Payments",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BusinessId_IdempotencyKey",
                table: "Payments",
                columns: new[] { "BusinessId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CustomerId",
                table: "Payments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId_BusinessId",
                table: "Payments",
                columns: new[] { "InvoiceId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId_Status",
                table: "Payments",
                columns: new[] { "InvoiceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code",
                table: "Plans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PricingProfiles_BusinessId",
                table: "PricingProfiles",
                column: "BusinessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTasks_AssignedMembershipId_BusinessId",
                table: "ProductionTasks",
                columns: new[] { "AssignedMembershipId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTasks_BusinessId",
                table: "ProductionTasks",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTasks_BusinessId_AssignedMembershipId_Status",
                table: "ProductionTasks",
                columns: new[] { "BusinessId", "AssignedMembershipId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTasks_BusinessId_Status_DeadlineUtc",
                table: "ProductionTasks",
                columns: new[] { "BusinessId", "Status", "DeadlineUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTasks_OrderGarmentId",
                table: "ProductionTasks",
                column: "OrderGarmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTasks_OrderGarmentId_BusinessId",
                table: "ProductionTasks",
                columns: new[] { "OrderGarmentId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTasks_OrderId",
                table: "ProductionTasks",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTasks_OrderId_BusinessId",
                table: "ProductionTasks",
                columns: new[] { "OrderId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_BusinessId",
                table: "Receipts",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_BusinessId_Number",
                table: "Receipts",
                columns: new[] { "BusinessId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_PaymentId",
                table: "Receipts",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_PaymentId_BusinessId",
                table: "Receipts",
                columns: new[] { "PaymentId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_BusinessId",
                table: "RolePermissions",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_BusinessId",
                table: "RolePermissions",
                columns: new[] { "RoleId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_SectionPresets_BusinessId",
                table: "SectionPresets",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_SectionPresets_BusinessId_Name",
                table: "SectionPresets",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageDependencies_BusinessId",
                table: "StageDependencies",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_StageDependencies_DependsOnStageId_BusinessId",
                table: "StageDependencies",
                columns: new[] { "DependsOnStageId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_StageDependencies_StageId_BusinessId",
                table: "StageDependencies",
                columns: new[] { "StageId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Stages_BusinessId",
                table: "Stages",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Stages_WorkflowId_BusinessId",
                table: "Stages",
                columns: new[] { "WorkflowId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Stages_WorkflowId_Key",
                table: "Stages",
                columns: new[] { "WorkflowId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontDomains_BusinessId",
                table: "StorefrontDomains",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontDomains_Hostname",
                table: "StorefrontDomains",
                column: "Hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontDomains_StorefrontId_BusinessId",
                table: "StorefrontDomains",
                columns: new[] { "StorefrontId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Storefronts_BusinessId",
                table: "Storefronts",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Storefronts_Slug",
                table: "Storefronts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVersionMedia_BusinessId",
                table: "StorefrontVersionMedia",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVersionMedia_MediaAssetId",
                table: "StorefrontVersionMedia",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVersionMedia_MediaAssetId_BusinessId",
                table: "StorefrontVersionMedia",
                columns: new[] { "MediaAssetId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVersionMedia_VersionId_BusinessId",
                table: "StorefrontVersionMedia",
                columns: new[] { "VersionId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVersions_BusinessId",
                table: "StorefrontVersions",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVersions_StorefrontId_BusinessId",
                table: "StorefrontVersions",
                columns: new[] { "StorefrontId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontVersions_StorefrontId_VersionNumber",
                table: "StorefrontVersions",
                columns: new[] { "StorefrontId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_BusinessId",
                table: "Subscriptions",
                column: "BusinessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanId",
                table: "Subscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_BusinessId",
                table: "TaskDependencies",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_DependsOnTaskId",
                table: "TaskDependencies",
                column: "DependsOnTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_DependsOnTaskId_BusinessId",
                table: "TaskDependencies",
                columns: new[] { "DependsOnTaskId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_TaskId_BusinessId",
                table: "TaskDependencies",
                columns: new[] { "TaskId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskEvents_BusinessId",
                table: "TaskEvents",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskEvents_TaskId_BusinessId",
                table: "TaskEvents",
                columns: new[] { "TaskId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskEvents_TaskId_CreatedAtUtc",
                table: "TaskEvents",
                columns: new[] { "TaskId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageCounters_BusinessId",
                table: "UsageCounters",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                table: "UserSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSkills_BusinessId",
                table: "WorkerSkills",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSkills_MembershipId_BusinessId",
                table: "WorkerSkills",
                columns: new[] { "MembershipId", "BusinessId" });

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_BusinessId_Name",
                table: "Workflows",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Workflows_OneDefault",
                table: "Workflows",
                column: "BusinessId",
                unique: true,
                filter: "\"IsDefault\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BusinessHolidays");

            migrationBuilder.DropTable(
                name: "BusinessHours");

            migrationBuilder.DropTable(
                name: "CreditNotes");

            migrationBuilder.DropTable(
                name: "CustomerCommunications");

            migrationBuilder.DropTable(
                name: "DesignCollectionItems");

            migrationBuilder.DropTable(
                name: "DesignImages");

            migrationBuilder.DropTable(
                name: "DocumentCounters");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "EmailOutbox");

            migrationBuilder.DropTable(
                name: "EntitlementOverrides");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "Fittings");

            migrationBuilder.DropTable(
                name: "Inquiries");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "MeasurementRevisions");

            migrationBuilder.DropTable(
                name: "MeasurementTemplateFields");

            migrationBuilder.DropTable(
                name: "MembershipLocations");

            migrationBuilder.DropTable(
                name: "MembershipRoles");

            migrationBuilder.DropTable(
                name: "OrderCounters");

            migrationBuilder.DropTable(
                name: "PlanEntitlements");

            migrationBuilder.DropTable(
                name: "PricingProfiles");

            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "SectionPresets");

            migrationBuilder.DropTable(
                name: "StageDependencies");

            migrationBuilder.DropTable(
                name: "StorefrontDomains");

            migrationBuilder.DropTable(
                name: "StorefrontVersionMedia");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "TaskDependencies");

            migrationBuilder.DropTable(
                name: "TaskEvents");

            migrationBuilder.DropTable(
                name: "UsageCounters");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "WorkerSkills");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "DesignCollections");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "BusinessRoles");

            migrationBuilder.DropTable(
                name: "Stages");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "StorefrontVersions");

            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropTable(
                name: "ProductionTasks");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "Workflows");

            migrationBuilder.DropTable(
                name: "Storefronts");

            migrationBuilder.DropTable(
                name: "Memberships");

            migrationBuilder.DropTable(
                name: "OrderGarments");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Designs");

            migrationBuilder.DropTable(
                name: "MeasurementSets");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "DesignCategories");

            migrationBuilder.DropTable(
                name: "MeasurementTemplates");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Businesses");
        }
    }
}
