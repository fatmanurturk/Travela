import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";
import Login from "../pages/Login";
import "../guestTickets.css";

export default function CheckoutLogin({ onClose, onLogin }: { onClose: () => void; onLogin: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close(); }, []);
  return createPortal(<dialog ref={dialog} className="checkout-login-dialog" aria-label="İsteğe bağlı hesap girişi" onCancel={event => { event.preventDefault(); onClose(); }}>
    <button type="button" className="site-button secondary" onClick={onClose}>Giriş yapmadan forma dön</button>
    <Login onPassengerLogin={onLogin} />
  </dialog>, document.body);
}
