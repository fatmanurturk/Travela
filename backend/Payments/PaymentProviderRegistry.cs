namespace Travela.Api.Payments;

public sealed class PaymentProviderRegistry(LocalTestPaymentProvider local, IyzicoSandboxProvider iyzico)
{
    public IPaymentProvider Get(string name) => name switch { "LocalTest" => local, "IyzicoSandbox" => iyzico, _ => throw new PaymentFlowException("Siparişin sağlayıcısı tanımlı değil.", 503) };
}
