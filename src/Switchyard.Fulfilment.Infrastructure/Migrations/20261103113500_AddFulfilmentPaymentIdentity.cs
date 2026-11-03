using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Switchyard.Fulfilment.Infrastructure.Persistence;

#nullable disable

namespace Switchyard.Fulfilment.Infrastructure.Migrations;

[DbContext(typeof(FulfilmentDbContext))]
[Migration("20261103113500_AddFulfilmentPaymentIdentity")]
public partial class AddFulfilmentPaymentIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "payment_id",
            schema: "fulfilment",
            table: "requests",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "payment_id",
            schema: "fulfilment",
            table: "requests");
    }
}
