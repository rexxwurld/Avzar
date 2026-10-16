using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tailoring.Infrastructure.Persistence;
#nullable disable
namespace Tailoring.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261017110000_AddSubscriptionPayments")]
public partial class AddSubscriptionPayments : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("SubscriptionPayments", table => new
        {
            Id = table.Column<Guid>(type:"uuid",nullable:false), BusinessId = table.Column<Guid>(type:"uuid",nullable:false), PlanId = table.Column<Guid>(type:"uuid",nullable:false),
            BillingPeriod = table.Column<string>(type:"character varying(20)",maxLength:20,nullable:false), Amount = table.Column<decimal>(type:"numeric(18,2)",precision:18,scale:2,nullable:false),
            CurrencyCode = table.Column<string>(type:"character varying(3)",maxLength:3,nullable:false), Provider = table.Column<string>(type:"character varying(30)",maxLength:30,nullable:false),
            ProviderReference = table.Column<string>(type:"character varying(120)",maxLength:120,nullable:false), Status = table.Column<string>(type:"character varying(20)",maxLength:20,nullable:false), PaidAtUtc = table.Column<DateTime>(type:"timestamp with time zone",nullable:true),
            CreatedAtUtc = table.Column<DateTime>(type:"timestamp with time zone",nullable:false), UpdatedAtUtc = table.Column<DateTime>(type:"timestamp with time zone",nullable:true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SubscriptionPayments",x=>x.Id);
            table.ForeignKey("FK_SubscriptionPayments_Businesses_BusinessId",x=>x.BusinessId,"Businesses","Id",onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_SubscriptionPayments_Plans_PlanId",x=>x.PlanId,"Plans","Id",onDelete:ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_SubscriptionPayments_BusinessId","SubscriptionPayments","BusinessId");
        m.CreateIndex("IX_SubscriptionPayments_BusinessId_Status_CreatedAtUtc","SubscriptionPayments",new[]{"BusinessId","Status","CreatedAtUtc"});
        m.CreateIndex("IX_SubscriptionPayments_PlanId","SubscriptionPayments","PlanId");
        m.CreateIndex("IX_SubscriptionPayments_ProviderReference","SubscriptionPayments","ProviderReference",unique:true);
    }
    protected override void Down(MigrationBuilder m)=>m.DropTable("SubscriptionPayments");
}
