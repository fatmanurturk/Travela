import { useState } from "react";
import type { ReactNode } from "react";

export function Details({ title = "Detaylar", children }: { title?: string; children: ReactNode }) {
  return <details className="ui-details"><summary>{title}</summary><div className="ui-details-content">{children}</div></details>;
}
export function TestNotice() {
  return <p className="test-payment-badge test-environment-notice">Test ortamı — Gerçek ödeme veya iade yapılmaz</p>;
}
export function Identifier({ value }: { value: string }) {
  const [message, setMessage] = useState("");
  return <span className="short-identifier"><span>{value.length > 14 ? `${value.slice(0, 8)}…${value.slice(-4)}` : value}</span><button type="button" className="identifier-copy" aria-label="İşlem numarasını kopyala" onClick={async () => {
    try { await navigator.clipboard.writeText(value); setMessage("Kopyalandı"); }
    catch { setMessage(value); }
  }}>Kopyala</button><span role="status">{message}</span></span>;
}
