namespace Nop.Plugin.Payments.ICEPAY;

public sealed record PaymentMethodDefinition(string Name, string Label, string ApiType)
{
    public string SystemName => $"Payments.ICEPAY.{Name}";
}

public static class PaymentMethods
{
    public static readonly IReadOnlyList<PaymentMethodDefinition> All =
    [
        new("Ideal", "iDEAL | Wero", "ideal"),
        new("Card", "Card", "card"),
        new("Bancontact", "Bancontact", "bancontact"),
        new("PayPal", "PayPal", "paypal"),
        new("OnlineUberweisen", "Online Überweisen", "onlineueberweisen"),
        new("Eps", "EPS", "eps"),
        new("CarteBancaire", "Carte Bancaire", "card"),
        new("Dankort", "Dankort", "card"),
        new("PayByBank", "Pay by Bank", "paybybank")
    ];

    public static PaymentMethodDefinition Get(string name) => All.Single(x => x.Name == name);
}
