type CompanyPlaceholderPageProps = {
  title: string;
};

function CompanyPlaceholderPage({ title }: CompanyPlaceholderPageProps) {
  return (
    <div className="company-dashboard">
      <div className="company-dashboard-header">
        <h1>{title}</h1>
        <p>Bu alan geliştirme aşamasında. Kısa süre içinde aktif hale getirilecektir.</p>
      </div>

      <div className="company-panel" style={{ marginTop: 12 }}>
        <h3>Geliştirme Aşamasında</h3>
        <div className="company-empty-state">
          Bu özellik şu an için kullanılabilir değildir. Firma panelinin mevcut işlevleri korunurken,
          bu modül ileride eklenmeye devam edecektir.
        </div>
      </div>
    </div>
  );
}

export default CompanyPlaceholderPage;
