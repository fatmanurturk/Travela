// Hukuki inceleme gerektirir. Keep published versioned document files unchanged.
import catalog from "./pages/legal/documents.v1.json" with { type: "json" };

export const LEGAL_VERSION = catalog.version;
export const REFUND_NOTICE =
  "Kalkışa en az 2 saat varsa (tam 2 saat dahil) ilgili biletin ödenen tutarı tamamen iade edilir. " +
  "2 saatten az kaldığında normal yolcu iptali ve iadesi yapılamaz. " +
  "Sefer iptali ve geçerli sefer değişikliği hakları saklıdır.";

export function legalAcceptances(...types: string[]) {
  return types.map(documentType => ({
    documentType,
    version: LEGAL_VERSION,
    accepted: true,
  }));
}
