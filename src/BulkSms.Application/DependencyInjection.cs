using BulkSms.Application.Interfaces;
using BulkSms.Application.Providers;
using BulkSms.Application.Services;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BulkSms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBulkSmsApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmsProviderOptions>(configuration.GetSection(SmsProviderOptions.SectionName));
        services.Configure<SmsGatewayOptions>(configuration.GetSection(SmsGatewayOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AuthUserOptions>(configuration.GetSection(AuthUserOptions.SectionName));

        services.AddSingleton<IMobileNumberValidator, MobileNumberValidator>();
        services.AddSingleton<ISmsSegmentCalculator, SmsSegmentCalculator>();
        services.AddScoped<IRecipientFileParser, RecipientFileParser>();
        services.AddScoped<IBulkSmsService, BulkSmsService>();

        services.AddHttpClient<IAndroidSmsGateway, AndroidSmsGatewayService>((_, client) =>
            {
                var gateway = configuration.GetSection(SmsGatewayOptions.SectionName).Get<SmsGatewayOptions>()
                              ?? new SmsGatewayOptions();
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, gateway.TimeoutSeconds));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(5)
            });

        var smsProviderName = configuration.GetSection(SmsProviderOptions.SectionName)["Provider"] ?? "Mock";
        var gatewayProviderName = configuration.GetSection(SmsGatewayOptions.SectionName)["Provider"];
        var useAndroid =
            string.Equals(smsProviderName, "Android", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(gatewayProviderName, "Android", StringComparison.OrdinalIgnoreCase);

        if (!useAndroid && string.Equals(smsProviderName, "Rest", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ISmsProvider, RestSmsProvider>((sp, client) =>
            {
                var opts = configuration.GetSection(SmsProviderOptions.SectionName).Get<SmsProviderOptions>()
                           ?? new SmsProviderOptions();
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.RequestTimeoutSeconds));
            });
        }
        else if (useAndroid)
        {
            services.AddTransient<ISmsProvider>(sp => (ISmsProvider)sp.GetRequiredService<IAndroidSmsGateway>());
        }
        else
        {
            services.AddSingleton<ISmsProvider, MockSmsProvider>();
        }

        return services;
    }
}
