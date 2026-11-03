import { useEffect, useRef, useState } from "react";
import type { AnimationItem } from "lottie-web";
import animationUrl from "../assets/travel-is-fun.json?url";
import "./TravelAnimation.css";

export default function TravelAnimation() {
  const container = useRef<HTMLDivElement>(null);
  const animation = useRef<AnimationItem | null>(null);
  const [ready, setReady] = useState(false);
  const [paused, setPaused] = useState(false);
  const [reducedMotion, setReducedMotion] = useState(() => window.matchMedia("(prefers-reduced-motion: reduce)").matches);

  useEffect(() => {
    const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
    const update = () => setReducedMotion(preference.matches);
    preference.addEventListener("change", update);
    return () => preference.removeEventListener("change", update);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    let disposed = false;
    void Promise.all([
      import("lottie-web/build/player/lottie_light.js"),
      fetch(animationUrl, { signal: controller.signal }).then(response => {
        if (!response.ok) throw new Error("Animation unavailable");
        return response.json();
      }),
    ]).then(([{ default: lottie }, data]) => {
      if (disposed || !container.current) return;
      const item = lottie.loadAnimation({
        container: container.current, renderer: "svg", loop: true, autoplay: false,
        animationData: data, rendererSettings: { preserveAspectRatio: "xMidYMid meet" },
      });
      animation.current = item;
      item.addEventListener("DOMLoaded", () => { if (!disposed) setReady(true); });
    }).catch(() => {
      // Decorative artwork must never prevent searching for trips.
    });
    return () => { disposed = true; controller.abort(); animation.current?.destroy(); animation.current = null; };
  }, []);

  useEffect(() => {
    if (!ready || !animation.current) return;
    if (reducedMotion) animation.current.goToAndStop(60, true);
    else if (paused) animation.current.pause();
    else animation.current.play();
  }, [ready, paused, reducedMotion]);

  return <div className="hero-animation">
    <div className="hero-animation-art" ref={container} aria-hidden="true" />
    {ready && !reducedMotion && <button className="hero-animation-control" type="button" aria-pressed={paused} onClick={() => setPaused(value => !value)}>
      <span aria-hidden="true">{paused ? "▶" : "Ⅱ"}</span> {paused ? "Animasyonu oynat" : "Animasyonu duraklat"}
    </button>}
  </div>;
}
