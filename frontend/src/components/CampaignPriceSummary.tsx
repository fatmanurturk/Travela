import type { CampaignPrice } from "../campaigns";
import { money } from "../reservations";
import "../campaigns.css";

export default function CampaignPriceSummary({ price }: { price: CampaignPrice }) {
  return <div className="campaign-price" aria-live="polite"><dl><div><dt>Biletler toplamı</dt><dd>{money.format(price.originalAmount)}</dd></div>
    {price.discountAmount > 0 && <div className="campaign-saving"><dt>{price.campaignName}{price.couponCode && ` · ${price.couponCode}`}</dt><dd>−{money.format(price.discountAmount)}</dd></div>}
    <div className="campaign-total"><dt>Ödenecek toplam</dt><dd>{money.format(price.amount)}</dd></div></dl>
    <small>{price.discountAmount > 0 ? "İndirim sipariş oluşturulurken kesinleşir ve her bilete paylaştırılır. İadeler ödenen bilet tutarı üzerinden yapılır." : "Bu sepet için uygulanabilir otomatik indirim bulunmuyor."}</small>
  </div>;
}
