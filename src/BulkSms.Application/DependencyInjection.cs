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
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AuthUserOptions>(configuration.GetSection(AuthUserOptions.SectionName));

        services.AddSingleton<IMobileNumberValidator, MobileNumberValidator>();
        services.AddSingleton<ISmsSegmentCalculator, SmsSegmentCalculator>();
        services.AddScoped<IRecipientFileParser, RecipientFileParser>();
        services.AddScoped<IBulkSmsService, BulkSmsService>();

        var providerName = configuration.GetSection(SmsProviderOptions.SectionName)["Provider"] ?? "Mock";

        if (string.Equals(providerName, "Rest", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ISmsProvider, RestSmsProvider>((sp, client) =>
            {
                var opts = configuration.GetSection(SmsProviderOptions.SectionName).Get<SmsProviderOptions>()
                           ?? new SmsProviderOptions();
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.RequestTimeoutSeconds));
            });
        }
        else
        {
            services.AddSingleton<ISmsProvider, MockSmsProvider>();
        }

        return services;
    }
}
