// Hukuki inceleme gerektirir.
import catalog from "./documents.v1.json";
import LegalPageLayout from "./LegalPageLayout";

export default function LegalDocumentPage({ type }: { type: string }) {
  const document = catalog.documents.find(document => document.type === type);
  if (!document) throw new Error("Hukuki belge tanımı bulunamadı.");

  return (
    <LegalPageLayout
      title={document.title}
      version={catalog.version}
      updatedAt={catalog.updatedAt}
    >
      {document.sections.map((section, index) => (
        <section key={section.title} aria-labelledby={`legal-section-${index}`}>
          <h2 id={`legal-section-${index}`}>{section.title}</h2>
          {section.paragraphs.map((paragraph, paragraphIndex) => (
            <p key={paragraphIndex}>{paragraph}</p>
          ))}
        </section>
      ))}
    </LegalPageLayout>
  );
}
