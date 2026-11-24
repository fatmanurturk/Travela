import { useEffect, useRef, useState } from "react";

export default function HoldCountdown({ serverTime, expiresAt, payment = false, onExpired }: {
  serverTime: string; expiresAt: string; payment?: boolean; onExpired?: () => void;
}) {
  const [seconds, setSeconds] = useState<number | null>(null);
  const expired = useRef(onExpired);
  useEffect(() => { expired.current = onExpired; }, [onExpired]);
  useEffect(() => {
    const remaining = Math.max(0, Date.parse(expiresAt) - Date.parse(serverTime));
    const end = performance.now() + remaining;
    let notified = false;
    const tick = () => {
      const value = Math.max(0, Math.ceil((end - performance.now()) / 1000)); setSeconds(value);
      if (value === 0 && !notified) { notified = true; expired.current?.(); }
    };
    tick(); const timer = setInterval(tick, 250); return () => clearInterval(timer);
  }, [serverTime, expiresAt]);
  if (seconds == null) return null;
  return <p className="hold-countdown" role="timer" aria-live="off">{payment ? "Ödeme için kalan süre" : "Koltuklarınız için kalan süre"}: <strong>{String(Math.floor(seconds / 60)).padStart(2, "0")}:{String(seconds % 60).padStart(2, "0")}</strong></p>;
}
