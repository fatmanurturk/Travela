import { useState } from "react";
import { apiUrl } from "../api";
import { legalAcceptances, REFUND_NOTICE } from "../legal";
import SalesConsent from "./SalesConsent";

type Props = {
  endpoint: string;
  accepted: boolean;
  onSaved: () => void;
};

export default function PendingLegalAcceptance({ endpoint, accepted, onSaved }: Props) {
  const [checked, setChecked] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  if (accepted) {
    return <p className="legal-refund-note">Sözleşme ve ön bilgilendirme onayınız kaydedildi. {REFUND_NOTICE}</p>;
  }

  async function save() {
    if (!checked || busy) return;
    setBusy(true);
    setError("");
    try {
      const response = await fetch(apiUrl(endpoint), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`,
        },
        body: JSON.stringify({ legalAcceptances: legalAcceptances("DistanceSales") }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.message ?? "Onay kaydedilemedi. Lütfen tekrar deneyin.");
      onSaved();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Onay kaydedilemedi.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-label="Ödeme öncesi hukuki onay">
      <SalesConsent checked={checked} onChange={setChecked} disabled={busy} />
      {error && <p role="alert">{error}</p>}
      <button
        type="button"
        className="site-button primary"
        disabled={!checked || busy}
        onClick={() => void save()}
      >
        {busy ? "Onay kaydediliyor..." : "Onayla ve ödemeye devam et"}
      </button>
    </section>
  );
}
