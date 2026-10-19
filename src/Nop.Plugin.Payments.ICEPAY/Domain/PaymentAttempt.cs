using Nop.Core;

namespace Nop.Plugin.Payments.ICEPAY.Domain;

public class ICEPAYPaymentAttempt : BaseEntity
{
    public int OrderId { get; set; }
    public int StoreId { get; set; }
    public string Reference { get; set; } = "";
    public string PaymentKey { get; set; } = "";
    public string Method { get; set; } = "";
    public string MerchantId { get; set; } = "";
    public string EncryptedSecret { get; set; } = "";
    public string Currency { get; set; } = "";
    public int MinorAmount { get; set; }
    public decimal OrderTotal { get; set; }
    public string CheckoutUrl { get; set; } = "";
    public string CreationState { get; set; } = "Creating";
    public string PaymentStatus { get; set; } = "";
    public bool ReviewRequired { get; set; }
    public bool Applied { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastCheckedOnUtc { get; set; } = DateTime.UnixEpoch;
}

public class ICEPAYRefundRequest : BaseEntity
{
    public int OrderId { get; set; }
    public int AttemptId { get; set; }
    public string Reference { get; set; } = "";
    public string RefundKey { get; set; } = "";
    public decimal BaseAmount { get; set; }
    public int MinorAmount { get; set; }
    public string State { get; set; } = "Submitting";
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
}

public class ICEPAYOrderLock : BaseEntity
{
    public int OrderId { get; set; }
    public string Owner { get; set; } = "";
    public DateTime ExpiresOnUtc { get; set; } = DateTime.UnixEpoch;
}
