using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Web.Framework.Extensions;

namespace Nop.Plugin.Payments.ICEPAY.Data;

[NopMigration("2026-10-19 20:15:00", "ICEPAY payment reference configuration", MigrationProcessType.NoMatter)]
public class CheckoutPresentationMigration : MigrationBase
{
    public override void Up() => this.AddOrUpdateLocaleResource(new Dictionary<string, string>
    {
        ["Plugins.Payments.ICEPAY.ReferenceTemplate"] = "Payment reference",
        ["Plugins.Payments.ICEPAY.ReferenceTemplate.Hint"] = "Reference shown in ICEPAY. {ORDER_ID} is replaced with the store's order number. Leave blank to use Order #{ORDER_ID}. Changes apply to new payment attempts."
    });

    public override void Down() { }
}
