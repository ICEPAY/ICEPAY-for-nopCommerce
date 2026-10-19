using FluentMigrator;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Data.Migrations;
using Nop.Plugin.Payments.ICEPAY.Domain;

namespace Nop.Plugin.Payments.ICEPAY.Data;

[NopMigration("2026-10-19 12:00:00", "ICEPAY payment attempts and request history", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<ICEPAYPaymentAttempt>();
        this.CreateTableIfNotExists<ICEPAYRefundRequest>();
        this.CreateTableIfNotExists<ICEPAYOrderLock>();
        if (!Schema.Table(nameof(ICEPAYOrderLock)).Index("IX_ICEPAYOrderLock_OrderId").Exists())
            Create.Index("IX_ICEPAYOrderLock_OrderId").OnTable(nameof(ICEPAYOrderLock)).OnColumn(nameof(ICEPAYOrderLock.OrderId)).Ascending().WithOptions().Unique();
        if (!Schema.Table(nameof(ICEPAYPaymentAttempt)).Index("IX_ICEPAYAttempt_OrderId").Exists())
            Create.Index("IX_ICEPAYAttempt_OrderId").OnTable(nameof(ICEPAYPaymentAttempt)).OnColumn(nameof(ICEPAYPaymentAttempt.OrderId)).Ascending();
        if (!Schema.Table(nameof(ICEPAYRefundRequest)).Index("IX_ICEPAYRefund_OrderId").Exists())
            Create.Index("IX_ICEPAYRefund_OrderId").OnTable(nameof(ICEPAYRefundRequest)).OnColumn(nameof(ICEPAYRefundRequest.OrderId)).Ascending();
    }

    // Retain payment history and encrypted credential snapshots on uninstall.
    public override void Down() { }
}

public class PaymentAttemptBuilder : NopEntityBuilder<ICEPAYPaymentAttempt>
{
    public override void MapEntity(CreateTableExpressionBuilder table) => table
        .WithColumn(nameof(ICEPAYPaymentAttempt.EncryptedSecret)).AsString(2048).NotNullable()
        .WithColumn(nameof(ICEPAYPaymentAttempt.CheckoutUrl)).AsString(2048).NotNullable();
}
