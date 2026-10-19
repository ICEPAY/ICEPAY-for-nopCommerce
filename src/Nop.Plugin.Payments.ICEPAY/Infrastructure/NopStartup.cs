using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Payments.ICEPAY.Services;

namespace Nop.Plugin.Payments.ICEPAY.Infrastructure;

public class NopStartup : INopStartup
{
    public int Order => 3000;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient("ICEPAY", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IICEPAYStore, ICEPAYStore>();
        services.AddScoped<ICEPAYGateway>();
        services.AddScoped<ICEPAYPaymentService>();
    }
    public void Configure(IApplicationBuilder application) { }
}
