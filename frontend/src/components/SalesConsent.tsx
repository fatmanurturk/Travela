// Hukuki inceleme gerektirir.
import { Link } from "react-router-dom";
import { REFUND_NOTICE } from "../legal";
import "./LegalConsent.css";

type Props = {
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
};

export default function SalesConsent({ checked, onChange, disabled = false }: Props) {
  return (
    <div className="legal-consent-block">
      <label className="legal-consent">
        <input
          type="checkbox"
          required
          checked={checked}
          disabled={disabled}
          onChange={event => onChange(event.target.checked)}
          aria-label="Mesafeli satış sözleşmesini ve ön bilgilendirme formunu okudum, kabul ediyorum"
        />
        <span>
          <Link to="/mesafeli-satis-sozlesmesi" target="_blank" rel="noopener noreferrer">
            Mesafeli satış sözleşmesini ve ön bilgilendirme formunu
          </Link>
          {" "}okudum, kabul ediyorum.
        </span>
      </label>
      <p className="legal-refund-note">
        {REFUND_NOTICE}{" "}
        <Link to="/iade-ve-iptal" target="_blank" rel="noopener noreferrer">
          İade ve iptal koşulları
        </Link>
      </p>
    </div>
  );
}
