# ICEPAY for nopCommerce

Payment integration for **nopCommerce 5.00 / .NET 10**, built on the official
[`ICEPAY.Checkout` 1.0.2 SDK](https://github.com/ICEPAY/checkout-dotnet).
The development host is pinned to **5.00.0-beta** (`0608f65d203470e5afc2dda12abdfaca734fbc8d`).
Compatibility with the eventual stable 5.00 release must be verified again.

## Payment methods

Nine native checkout entries:
iDEAL | Wero, Card, Bancontact, PayPal, Online Überweisen, EPS, Carte Bancaire,
Dankort and Pay by Bank. Carte Bancaire and Dankort are branded `card` aliases,
as in the reference integration; the API request does not restrict card brands.
Only methods returned by the merchant's ICEPAY account are offered.

The original artwork and its license/provenance are retained in `assets/payment-logos`.

The distribution contains a shared core and nine small payment plugins.
All share one configuration page and backend. There is no custom refund dashboard.

Use **ICEPAY** in branding, project names and integration identifiers. Types provided
by the upstream SDK retain their published names. nopCommerce normalizes stored
setting and localization keys to lowercase.

## Build and test

For an automatic edit–build–restart loop, see **Local development** below.

Install a .NET 10 SDK and Python 3.12 or newer. Put `dotnet` on PATH.

```sh
python3 scripts/bootstrap.py
dotnet restore ICEPAY.slnx --packages .deps/packages
dotnet build ICEPAY.slnx -c Release --no-restore --disable-build-servers -m:1 /p:UseSharedCompilation=false
python3 scripts/package.py
dotnet test tests/ICEPAY.Tests/ICEPAY.Tests.csproj -c Release --no-build --no-restore
```

Source dependencies and NuGet packages live in ignored `.deps/`; build outputs
and the installable ZIP are ignored. The bootstrap script pins both upstream
source revisions and refuses to replace an existing different checkout.
Tests use fake HTTP responses through the real ICEPAY SDK and SQLite for locking;
they do not create merchant transactions.

## Local development

Run the .NET store locally and keep a dedicated MariaDB 11.4 database in Docker.
Start Docker Desktop, then run these commands from the repository root:

```sh
docker compose -f compose.dev.yml up -d --wait mariadb phpmyadmin
python3 scripts/dev.py
```

phpMyAdmin is available at [http://localhost:8081](http://localhost:8081).
Log in with username `icepay` and password `icepay-local-only`, then select the
`icepay_dev` database. It connects to MariaDB through the Docker network.

The runner finds `dotnet` on PATH or in `~/.dotnet`, prepares the local nopCommerce
source, builds Debug assemblies, stages the ten ICEPAY plugins and nopCommerce's
manual shipping provider, and serves the store at `http://localhost:5000`.
The first build restores the web host's dependencies.

Complete nopCommerce's installation screen once. Select **MySQL** (the provider used
for MariaDB), enable sample data, choose your own admin email/password, and use this
existing database connection:

```text
Server=127.0.0.1;Port=33069;Database=icepay_dev;User ID=icepay;Password=icepay-local-only
```

Choose the connection-string option and leave **Create database** unchecked: Docker
has already created it. Then install the shared ICEPAY core and desired method plugins
under **Configuration → Local plugins**. No ZIP upload is needed in this workflow.
Activate methods under **Configuration → Payment methods** and configure test credentials.

For checkout tests with shippable products, install **Manual (Fixed or By Weight and
By Total)** under **Configuration → Local plugins**, then restart when prompted.
Activate it under **Configuration → Shipping → Shipping providers** and click
**Configure**. Use **Manage shipping methods** to add a method named **Test shipping**,
then return to the provider's **Fixed rate** view and set its rate to `0`.
Ensure that shipping method restrictions allow your test address's country.

With the runner left open, save files under `src/` in Rider. It rebuilds, copies the
plugins into nopCommerce's upload staging folder and restarts the store. Refresh the
browser after the restart. A failed build leaves the previous store running. Plugin
installation restarts requested from the admin UI are handled automatically too.

C# changes use an automatic rebuild/restart rather than promising Hot Reload for
dynamically loaded assemblies. Views and plugin manifests are also watched. Database
contents and `App_Data` configuration persist. New settings/localization resources or
schema changes still need an appropriate update/migration; restarting does not rerun
`InstallAsync`. Keep the database volume and `.deps/nopCommerce` to preserve the store.

For breakpoints, attach Rider's .NET debugger to the process running `Nop.Web.dll`;
Debug symbols are staged with the plugins. Reattach after an automatic restart.
Alternatively, stop the watcher, run `python3 scripts/dev.py prepare`, and configure
Rider to launch `Nop.Web.dll` from the host's `bin/Debug/net10.0` directory, using the
`Nop.Web` project directory as working directory and `ASPNETCORE_ENVIRONMENT=Development`.

To test ICEPAY's actual redirects and callbacks, expose port 5000 through an HTTPS
tunnel. For example, after installing `cloudflared`:

```sh
cloudflared tunnel --url http://localhost:5000
```

Use the generated HTTPS address as the nopCommerce store URL and browse the store
through that address. Keep the tunnel running through the full payment test. Quick
tunnel hostnames change when the tunnel is restarted; update the store URL before
creating new payment attempts. Set the merchant to test mode in the ICEPAY Portal
when testing without real funds. The webhook route must be reachable without an
interactive tunnel login.

For a named Cloudflare tunnel, point its service URL to `http://localhost:5000`.
Choose a hostname covered by the domain's edge certificate: a certificate for
`*.example.com` covers `nop-dev.example.com`, but not `nop.dev.example.com`.
In nopCommerce's **Configuration → Stores**, set the store URL to the public HTTPS
address and enable **SSL enabled**. In the development host's
`.deps/nopCommerce/src/Presentation/Nop.Web/App_Data/appsettings.json`, set
`HostingConfig.UseProxy` to `true` and `HostingConfig.KnownProxies` to
`127.0.0.1,::1,::ffff:127.0.0.1` when the tunnel connector runs on the same machine.
Restart the development runner so it loads these settings, then browse through
the public HTTPS address. To return to HTTP-only local browsing, restore the store
URL to `http://localhost:5000/`, disable **SSL enabled**, and restart the runner.

Stop the runner with Ctrl+C. Stop MariaDB and phpMyAdmin with
`docker compose -f compose.dev.yml stop`.
The compose credentials are for this loopback-only development database.

## Install

1. Upload `artifacts/ICEPAY-nopCommerce-5.00-1.0.0.zip` in **Configuration → Local plugins**.
2. Install **ICEPAY — shared configuration** first and restart when nopCommerce requests it.
3. Install the desired method plugins and restart. Activate them under **Configuration → Payment methods**.
4. Open the shared configuration page. Enter merchant ID and secret. Select a store
   using nopCommerce's store selector to override its credentials.
   **Payment reference** accepts a template such as `Order #{ORDER_ID}` (the default).
   `{ORDER_ID}` expands to the order's custom order number, falling back to its numeric
   ID. The reference has its own per-store override. A blank template uses the default.
   New attempts use the configured reference; existing attempts retain their saved
   reference and payment key when retried or updated by a webhook.
5. Set the merchant's test/live mode in the ICEPAY Portal.
6. Configure the store's public HTTPS URL. Callback and return URLs are supplied with
   each payment. Ensure `POST /Plugins/ICEPAY/Webhook/{attemptId}` is reachable by ICEPAY.

English labels are nopCommerce localization resources under `Plugins.Payments.ICEPAY`.
Merchant secrets are encrypted with nopCommerce's encryption service. Back up its
encryption key alongside the database. Existing attempts retain encrypted credential
snapshots so store configuration changes do not silently move old payments between merchants.

New checkout requests send the order's billing email and address to ICEPAY through
the .NET SDK's documented `meta.customer` object. Address fields include country,
city, postal code and the combined address lines as `streetName`; nopCommerce has
no dedicated house-number field. Empty fields are omitted. The integration does
not send shipping addresses, phone numbers or IP addresses. The request also includes
`meta.integration` with type `nopcommerce`, the plugin version, and developer `ICEPAY`.

## Payment and refund behavior

- Orders are created pending, then shoppers are redirected to the selected method's
  ICEPAY direct payment link or hosted checkout. Retrying a pending attempt reuses its link.
- Signed callbacks are checked against the stored reference, merchant, amount,
  and currency. Current payment state is retrieved from ICEPAY before updating
  an order. Browser return parameters never prove payment.
- **ICEPAY `completed` marks an eligible order paid, even if funds have not cleared.**
  This policy applies to Pay by Bank, EPS and Online Überweisen as well.
- Expired/cancelled attempts leave the order pending. Late payments and extra successful
  attempts are retained. Cancelled, changed or already-paid orders are flagged in
  private order notes and the system log for merchant review; no automatic refund occurs.
  Review and explicitly refund unexpected extra payments in the ICEPAY Portal.
- Standard nopCommerce **Refund** and **Partial refund** buttons call ICEPAY.
  **A successful refund-creation API response is immediately reported as success,
  regardless of the refund status in that response.** nopCommerce then updates its
  refunded amount/status and sends its usual notifications. No later completion is required,
  and later gateway refund updates do not reverse that bookkeeping.
- Native nopCommerce refund eligibility checks still apply. Amounts cannot exceed
  the remaining balance. Refund requests retain the original currency and conversion;
  the last refund consumes any minor-unit rounding remainder.
- A network failure or ambiguous server response can mean a request was accepted.
  Its reference is recorded in private order notes. No automatic retry is made for
  payment/refund creation; reconcile that reference in ICEPAY before resubmitting.
- A scheduled task refreshes known unresolved payments every two minutes in batches
  of 50. This supplements callbacks; it does not submit new payment/refund requests.
- Uninstall preserves configuration, payment attempts and refund request records.
  Remove the method plugins before the shared core. Do not delete this history while
  payments or refunds still need reconciliation.

Supported checkout currencies are EUR, GBP, USD, PLN, SEK, NOK, DKK and CZK.
The merchant and chosen method must also support the currency. Payment totals use the
order's customer-currency/rate snapshot. Subscription billing, authorization/capture,
voids and payment surcharges are outside this release.

## Validation before a live release

Automated build/tests do not replace a full storefront test with merchant test credentials.
Verify installation, per-store credentials, each enabled method's redirect and return,
public webhook delivery, an abandoned browser session, duplicate callbacks, and full/partial
refunds against an ICEPAY test account. No real ICEPAY transaction has been performed here.

## License

Plugin code is MIT. The distributed ICEPAY SDK retains its own MIT license. nopCommerce is a build
and runtime dependency under its own license; nopCommerce assemblies are not bundled.
