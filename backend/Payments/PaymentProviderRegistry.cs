namespace Travela.Api.Payments;

public sealed class PaymentProviderRegistry(IServiceProvider services, IHostEnvironment environment)
{
    public IPaymentProvider Get(string name)
    {
        var testEnvironment = PaymentStartupConfiguration.AllowsTestProviders(environment);
        return name switch
        {
            "LocalTest" when testEnvironment => services.GetRequiredService<LocalTestPaymentProvider>(),
            "IyzicoSandbox" when testEnvironment => services.GetRequiredService<IyzicoSandboxProvider>(),
            "Iyzico" when !testEnvironment => services.GetRequiredService<IyzicoProvider>(),
            _ => throw new PaymentFlowException("Siparişin sağlayıcısı bu ortamda kullanılamıyor.", 503)
        };
    }
}
