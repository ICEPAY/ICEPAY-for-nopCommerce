using System.Globalization;
using System.Text.Json.Nodes;
using ICEPAY.Checkout;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Orders;

namespace Nop.Plugin.Payments.ICEPAY.Services;

public static class ICEPAYCheckoutData
{
    public static string Reference(string? template, Order order)
    {
        var format = string.IsNullOrWhiteSpace(template) ? ICEPAYSettings.DefaultReferenceTemplate : template.Trim();
        var number = string.IsNullOrWhiteSpace(order.CustomOrderNumber)
            ? order.Id.ToString(CultureInfo.InvariantCulture) : order.CustomOrderNumber;
        return format.Replace("{ORDER_ID}", number, StringComparison.Ordinal);
    }

    public static Metadata Metadata(Address? billing, string? countryCode)
    {
        var version = typeof(ICEPAYCorePlugin).Assembly.GetName().Version!.ToString(3);
        var metadata = new Metadata().WithIntegrationInformation("nopcommerce", version, "ICEPAY");
        if (billing is null) return metadata;

        // The .NET SDK documents billing information under meta.customer.
        var address = new JsonObject();
        Add(address, "country", countryCode?.ToUpperInvariant(), 2);
        Add(address, "city", billing.City, 254);
        Add(address, "postalCode", billing.ZipPostalCode, 31);
        // nopCommerce stores free-form address lines, not a separate house number.
        Add(address, "streetName", string.Join(", ", new[] { billing.Address1, billing.Address2 }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())), 128);
        var customer = new JsonObject();
        Add(customer, "email", billing.Email, 128);
        if (address.Count > 0) customer["address"] = address;
        if (customer.Count > 0) metadata.With("customer", customer);
        return metadata;
    }

    private static void Add(JsonObject target, string key, string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        // Truncate on Unicode scalar boundaries, never in the middle of a surrogate pair.
        target[key] = string.Concat(value.Trim().EnumerateRunes().Take(limit).Select(rune => rune.ToString()));
    }
}
