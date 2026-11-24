import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { apiUrl } from "./api";
import type { Gender } from "./seatCompatibility";

export type SeatSession = { id: string; token: string };
type Selection = { id: string; phase: "Selection" | "Expired" | "Payment"; serverTime: string; expiresAt: string | null;
  purchaseGroupId: string | null; canStartNewSelection?: boolean; paymentStatus?: string | null; seats: { seatNumber: number; gender: Gender }[] };
const sessions = new Map<string, Promise<SeatSession | null>>();
const registered = new Set<string>();
const reloaded = performance.getEntriesByType("navigation").some(entry => ["reload", "back_forward"].includes((entry as PerformanceNavigationTiming).type));
async function claim(slot: string, create: boolean): Promise<SeatSession | null> {
  const key = `travela_seat_session:${slot}`;
  let candidate: SeatSession | null = null;
  if (reloaded) { try { candidate = JSON.parse(sessionStorage.getItem(key) ?? "null"); } catch { /* Invalid tab data is discarded. */ } }
  if (candidate && (!/^[a-f0-9-]{36}$/i.test(candidate.id) || !/^[a-f0-9]{64}$/i.test(candidate.token))) candidate = null;
  if (candidate && navigator.locks) {
    const own = await new Promise<boolean>(resolve => {
      void navigator.locks.request(`travela-seat-tab:${candidate!.id}`, { ifAvailable: true }, lock => {
        resolve(!!lock); return lock ? new Promise<void>(() => {}) : undefined;
      });
    });
    if (!own) candidate = null; // A duplicated tab cannot acquire the original tab's capability.
  }
  if (!candidate && create) {
    candidate = { id: crypto.randomUUID(), token: Array.from(crypto.getRandomValues(new Uint8Array(32)), value => value.toString(16).padStart(2, "0")).join("") };
    if (navigator.locks) void navigator.locks.request(`travela-seat-tab:${candidate.id}`, () => new Promise<void>(() => {}));
  }
  if (candidate) sessionStorage.setItem(key, JSON.stringify(candidate));
  return candidate;
}
async function session(slot: string, create: boolean) {
  if (!sessions.has(slot)) sessions.set(slot, claim(slot, create));
  let result = await sessions.get(slot)!;
  if (!result && create) { sessions.set(slot, claim(slot, true)); result = await sessions.get(slot)!; }
  return result;
}
export async function selectionHeaders(capability: SeatSession) {
  const token = sessionStorage.getItem("travela_token");
  return { "Content-Type": "application/json", "X-Seat-Token": capability.token, ...(token ? { Authorization: `Bearer ${token}` } : {}) };
}
export function useSeatSelection(tripId: number, boardingStopOrder: number | null, alightingStopOrder: number | null, onError: (error: string) => void, onChanged: () => void) {
  const slot = `${tripId}:${boardingStopOrder}:${alightingStopOrder}`;
  const [state, setState] = useState<Selection | null>(null); const [busy, setBusy] = useState(false);
  const [capability, setCapability] = useState<SeatSession | null>(null);
  const active = useRef(false); const stateRef = useRef(state);
  const changed = useRef(onChanged); const failed = useRef(onError);
  const generation = useRef(0); const reads = useRef(0); const unverified = useRef(false);
  useLayoutEffect(() => { stateRef.current = state; changed.current = onChanged; failed.current = onError; }, [state, onChanged, onError]);
  async function read(response: Response): Promise<Selection> {
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new Error(data?.message ?? "Koltuk seçimi doğrulanamadı. Planı yenileyin.");
    if (!Array.isArray(data?.seats) || !data.serverTime || !data.phase) throw new Error("Koltuk ayırma yanıtı alınamadı.");
    return data;
  }
  const apply = useCallback((data: Selection) => {
    setState(data);
    if (data.phase === "Expired") failed.current("Koltuk ayırma süreniz doldu. Lütfen koltuklarınızı yeniden seçin. Girdiğiniz bilgiler aynı koltuk için korunur.");
  }, []);
  const sync = useCallback(async () => {
    if (active.current) return;
    const version = generation.current; const readId = ++reads.current;
    const cap = await session(slot, false); if (!cap) return;
    try {
      const data = await read(await fetch(apiUrl(`/api/seat-selections/${cap.id}`), { headers: await selectionHeaders(cap), cache: "no-store" }));
      if (active.current || version !== generation.current || readId !== reads.current) return;
      registered.add(cap.id);
      if (unverified.current && data.phase !== "Expired") { failed.current(""); unverified.current = false; }
      setCapability(cap); apply(data);
    } catch { unverified.current = true; failed.current("Koltuklarınız sunucudan doğrulanamadı. Bağlantı geri geldiğinde yeniden kontrol edilecek."); }
  }, [slot, apply]);
  useEffect(() => {
    queueMicrotask(() => void sync()); const timer = setInterval(() => void sync(), 5000);
    const resume = () => { if (!document.hidden) { void sync(); changed.current(); } };
    window.addEventListener("online", resume); window.addEventListener("focus", resume); document.addEventListener("visibilitychange", resume);
    return () => { clearInterval(timer); window.removeEventListener("online", resume); window.removeEventListener("focus", resume); document.removeEventListener("visibilitychange", resume); };
  }, [sync]);
  const mutate = async (seat: number, gender?: Gender) => {
    if (active.current) return false;
    active.current = true; generation.current++; setBusy(true);
    try {
      if (stateRef.current?.phase === "Expired") {
        sessions.delete(slot); sessionStorage.removeItem(`travela_seat_session:${slot}`);
      }
      const cap = await session(slot, !!gender); if (!cap) return false;
      setCapability(cap);
      const headers = await selectionHeaders(cap);
      if (gender && !registered.has(cap.id)) {
        await read(await fetch(apiUrl("/api/seat-selections"), { method: "POST", headers,
          body: JSON.stringify({ ...cap, tripId, boardingStopOrder, alightingStopOrder }) }));
        registered.add(cap.id);
      }
      const data = await read(await fetch(apiUrl(`/api/seat-selections/${cap.id}/seats${gender ? "" : `/${seat}`}`), {
        method: gender ? "PUT" : "DELETE", headers, ...(gender ? { body: JSON.stringify({ seatNumber: seat, gender }) } : {}) }));
      apply(data); failed.current(""); changed.current(); return true;
    } catch (cause) {
      if (cause instanceof TypeError) unverified.current = true;
      failed.current(cause instanceof TypeError ? "Sunucu yanıtı alınamadı. Koltuk seçiminizi yeniden doğrulayın." : (cause as Error).message);
      changed.current(); return false;
    } finally { active.current = false; setBusy(false); void sync(); }
  };
  const expire = useCallback(() => {
    if (stateRef.current?.phase !== "Selection") return;
    apply({ ...stateRef.current, phase: "Expired", seats: [] }); changed.current(); void sync();
  }, [apply, sync]);
  const startNew = () => {
    if (!stateRef.current?.canStartNewSelection || active.current) return;
    sessions.delete(slot); sessionStorage.removeItem(`travela_seat_session:${slot}`); generation.current++;
    setState(null); setCapability(null); changed.current();
  };
  return { state, capability, busy, select: (seat: number, gender: Gender) => mutate(seat, gender), remove: (seat: number) => mutate(seat), expire, sync, startNew };
}
