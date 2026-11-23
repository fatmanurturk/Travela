import { useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { suggestCities } from "../citySearch";
import type { CityOption } from "../citySearch";
import "./CityCombobox.css";

type Props = {
  label: string;
  placeholder: string;
  cities: CityOption[];
  text: string;
  selectedId?: number;
  onTextChange: (text: string) => void;
  onSelect: (city: CityOption) => void;
  error?: string;
  loading: boolean;
};

export default function CityCombobox({ label, placeholder, cities, text, selectedId, onTextChange, onSelect, error, loading }: Props) {
  const id = useId();
  const input = useRef<HTMLInputElement>(null);
  const popup = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const [position, setPosition] = useState({ left: 0, top: 0, width: 0, maxHeight: 280 });
  const suggestions = useMemo(() => suggestCities(cities, text), [cities, text]);
  const activeSuggestion = suggestions[active];
  const optionId = (cityId: number) => `${id}-option-${cityId}`;
  const close = () => { setOpen(false); setActive(-1); };
  const select = (city: CityOption) => {
    onSelect(city);
    input.current?.focus({ preventScroll: true });
    close();
  };

  useLayoutEffect(() => {
    if (!open) return;
    const place = () => {
      const rect = input.current?.getBoundingClientRect();
      if (!rect) return;
      const viewport = window.visualViewport;
      const bottom = viewport ? viewport.offsetTop + viewport.height : window.innerHeight;
      setPosition({ left: rect.left, top: rect.bottom + 4, width: rect.width,
        maxHeight: Math.max(0, Math.min(280, bottom - rect.bottom - 12)) });
    };
    const outside = (event: PointerEvent) => {
      if (!input.current?.contains(event.target as Node) && !popup.current?.contains(event.target as Node)) {
        setOpen(false); setActive(-1);
      }
    };
    place();
    window.addEventListener("resize", place);
    window.addEventListener("scroll", place, true);
    window.visualViewport?.addEventListener("resize", place);
    window.visualViewport?.addEventListener("scroll", place);
    document.addEventListener("pointerdown", outside);
    return () => {
      window.removeEventListener("resize", place);
      window.removeEventListener("scroll", place, true);
      window.visualViewport?.removeEventListener("resize", place);
      window.visualViewport?.removeEventListener("scroll", place);
      document.removeEventListener("pointerdown", outside);
    };
  }, [open]);

  useLayoutEffect(() => {
    if (open && activeSuggestion) document.getElementById(optionId(activeSuggestion.city.id))?.scrollIntoView({ block: "nearest" });
  }, [open, activeSuggestion, id]);

  return <div className="trip-search-field city-combobox">
    <label htmlFor={id}>{label}</label>
    <input ref={input} id={id} role="combobox" aria-autocomplete="list" aria-haspopup="listbox"
      aria-expanded={open} aria-controls={open ? `${id}-list` : undefined}
      aria-activedescendant={open && activeSuggestion ? optionId(activeSuggestion.city.id) : undefined}
      aria-invalid={Boolean(error)} aria-describedby={`${id}-help${error ? ` ${id}-error` : ""}`}
      autoComplete="off" spellCheck={false} value={text} placeholder={placeholder}
      onFocus={() => { setOpen(true); setActive(-1); }}
      onClick={() => setOpen(true)}
      onBlur={event => { if (!popup.current?.contains(event.relatedTarget as Node)) close(); }}
      onChange={event => { onTextChange(event.target.value); setActive(-1); setOpen(true); }}
      onKeyDown={event => {
        if (event.nativeEvent.isComposing) return;
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
          event.preventDefault(); setOpen(true);
          setActive(current => !suggestions.length ? -1 : !open || current < 0
            ? event.key === "ArrowDown" ? 0 : suggestions.length - 1
            : (current + (event.key === "ArrowDown" ? 1 : -1) + suggestions.length) % suggestions.length);
        } else if (event.key === "Enter" && open) {
          event.preventDefault();
          const suggestion = activeSuggestion ?? suggestions[0];
          if (suggestion) select(suggestion.city);
        } else if (event.key === "Escape" && open) {
          event.preventDefault(); event.stopPropagation(); close();
        } else if (event.key === "Tab") close();
      }} />
    <span className="city-sr-only" id={`${id}-help`}>İl yazın; ok tuşlarıyla öneriler arasında gezinin, Enter ile seçin, Escape ile kapatın.</span>
    {error && <span className="field-error" id={`${id}-error`} role="alert">{error}</span>}
    <span className="city-sr-only" role="status">{open ? loading ? "İller yükleniyor." : `${suggestions.length} il önerisi.${suggestions.some(s => s.kind === "similar") ? " Benzer yazılan iller de listeleniyor." : ""}` : ""}</span>
    {open && createPortal(<div ref={popup} className="city-popup" style={position}>
      <div role="listbox" id={`${id}-list`} aria-label={`${label} il önerileri`} aria-busy={loading}>
        {suggestions.map((suggestion, index) => <div key={suggestion.city.id}>
          {suggestion.kind === "similar" && suggestions[index - 1]?.kind !== "similar" &&
            <div className="city-suggestion-heading" aria-hidden="true">Bunu mu demek istediniz?</div>}
          <div role="option" id={optionId(suggestion.city.id)} tabIndex={-1}
            aria-selected={selectedId === suggestion.city.id} className={`city-option${active === index ? " active" : ""}`}
            onPointerDown={event => { if (event.pointerType === "mouse") event.preventDefault(); }}
            onClick={() => select(suggestion.city)}>{suggestion.city.name}</div>
        </div>)}
      </div>
      {!suggestions.length && <p className="city-suggestion-empty">{loading ? "İller yükleniyor…" : cities.length ? "Eşleşen il bulunamadı. Yazımı kontrol edin." : "İl listesi yüklenemedi."}</p>}
    </div>, document.body)}
  </div>;
}
