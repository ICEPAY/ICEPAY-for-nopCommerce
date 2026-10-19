# ICEPAY for nopCommerce

Accept payments through ICEPAY in your nopCommerce store.

**Version:** 1.0.0 (in development)  
**nopCommerce:** 5.00  
**Runtime:** .NET 10  
**License:** MIT for plugin code; GPL-3.0 for copied payment-method artwork.

## About

ICEPAY for nopCommerce adds separate payment choices to your checkout, with shared
ICEPAY account settings. Customers pay through ICEPAY, and payment updates are
processed automatically. Full and partial refunds are available from the standard
nopCommerce order administration.

## Requirements

- nopCommerce 5.00 running on .NET 10. Development currently targets 5.00.0-beta.
- An ICEPAY merchant account with the desired payment methods enabled.
- A public HTTPS store URL that ICEPAY can reach for payment notifications.

## How to install

1. Sign in to your nopCommerce administration.
2. Open **Configuration → Local plugins** and upload
   `ICEPAY-nopCommerce-5.00-1.0.0.zip`.
3. Install **ICEPAY — shared configuration** and restart the application when prompted.
4. Install the ICEPAY payment-method plugins you want to offer, then restart when prompted.

The ZIP contains the shared configuration plugin and all nine payment-method plugins.

## Setup

1. Open **Configure** for **ICEPAY — shared configuration**.
2. Enter your **Merchant ID** and **Merchant secret** from ICEPAY.
3. Optionally adjust **Payment reference**. The default is `Order #{ORDER_ID}`;
   `{ORDER_ID}` is replaced with your store's order number.
4. Click **Save**. For multiple stores, use the store selector to configure separate
   credentials or payment references.
5. Go to **Configuration → Payment methods** and activate the methods you installed.
6. Under **Configuration → Stores**, set your public HTTPS store URL and enable
   **SSL enabled**.

Only payment methods available to your ICEPAY merchant are offered at checkout.
Return and webhook URLs are supplied automatically with each payment. Choose test
or live processing for your merchant in the ICEPAY Portal.

## Supported payment methods

- iDEAL | Wero
- Card
- Bancontact
- PayPal
- Online Überweisen
- EPS
- Carte Bancaire
- Dankort
- Pay by Bank

Carte Bancaire and Dankort use ICEPAY's card payment type.

## Support

Contact [info@icepay.com](mailto:info@icepay.com) for ICEPAY support.
Account and integration guidance is available in the
[ICEPAY documentation](https://docs.icepay.com/).

## External services

The plugin uses the ICEPAY Checkout API to create payments, retrieve payment status,
and request refunds. Payment requests include the order reference, amount, currency,
selected payment method, and return and webhook URLs. They also include the customer's
billing email and address, plus the integration name, version, and developer.

See ICEPAY's [Terms & Conditions](https://icepay.com/terms-and-conditions/) and
[Privacy Policy](https://www.icepay.nl/privacy-policy/) for details.

## License

The plugin code and ICEPAY .NET SDK are MIT licensed. The distribution
includes the original artwork, license text, and third-party notices.
