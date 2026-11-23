import { useEffect, useRef } from "react";
import { createRoot } from "react-dom/client";
import "./components/TripCrew.css";

type Conflict = { staffId: number; fullName: string; duty: "Driver" | "Attendant"; tripId: number; batchIndex?: number; departureCity: string; arrivalCity: string; departureTime: string; arrivalTime: string };
type Warning = { code: string; message: string; conflicts: Conflict[]; batchIndex?: number };
const time = (value: string) => new Date(value).toLocaleString("tr-TR", { timeZone: "Europe/Istanbul" });

function CrewConflictDialog({ warning, finish, signal }: { warning: Warning; finish: () => void; signal?: AbortSignal | null }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    dialog.current?.showModal();
    const abort = () => finish();
    signal?.addEventListener("abort", abort, { once: true });
    if (signal?.aborted) abort();
    return () => { signal?.removeEventListener("abort", abort); dialog.current?.close(); };
  }, [finish, signal]);
  return <dialog ref={dialog} className="crew-conflict-dialog" aria-labelledby="crew-warning-title" aria-describedby="crew-conflict-message"
    onCancel={event => { event.preventDefault(); finish(); }}>
    <h3 id="crew-warning-title">Personel atanamıyor</h3>
    <p id="crew-conflict-message">Seçtiğiniz personelin başka bir seferde görevi var. Sefer saatleri çakıştığı için atama yapılamaz. Lütfen farklı personel seçin veya sefer saatlerini düzenleyin.</p>
    <ul>{warning.conflicts.map((conflict, index) => <li key={index}><strong>{conflict.fullName} · {conflict.duty === "Driver" ? "Şoför" : "Muavin"}</strong>
      <span>{conflict.tripId ? `Sefer #${conflict.tripId}` : `Toplu sefer ${conflict.batchIndex}`} · {conflict.departureCity} → {conflict.arrivalCity}</span>
      <span>{time(conflict.departureTime)} – {time(conflict.arrivalTime)} (Türkiye saati)</span></li>)}</ul>
    <div className="detail-actions"><button type="button" className="primary-button" autoFocus onClick={finish}>Atamayı düzenle</button></div>
  </dialog>;
}

function showConflict(warning: Warning, returnTarget: HTMLElement | null, signal?: AbortSignal | null): Promise<void> {
  return new Promise(resolve => {
    const host = document.createElement("div"); document.body.appendChild(host);
    const root = createRoot(host); let finished = false;
    const finish = () => {
      if (finished) return; finished = true;
      root.unmount(); host.remove();
      resolve();
      // The calling form releases its busy state after this promise resolves.
      requestAnimationFrame(() => { if (returnTarget?.isConnected) returnTarget.focus({ preventScroll: true }); });
    };
    root.render(<CrewConflictDialog warning={warning} finish={finish} signal={signal} />);
  });
}

// A conflict only displays details. This helper never retries or alters a rejected write.
export async function fetchWithCrewConflict(url: string, init: RequestInit,
  send: (url: string, init: RequestInit) => Promise<Response> = fetch): Promise<Response> {
  const active = document.activeElement;
  const form = active instanceof HTMLElement ? active.closest("form") : null;
  const returnTarget = form?.querySelector<HTMLElement>("input:not([type=hidden]), select, textarea")
    ?? (active instanceof HTMLElement ? active : null);
  const response = await send(url, init);
  if (response.status === 409) {
    const warning = await response.clone().json().catch(() => null) as Warning | null;
    if (warning?.code === "crew_conflict" && Array.isArray(warning.conflicts)) await showConflict(warning, returnTarget, init.signal);
  }
  return response;
}
