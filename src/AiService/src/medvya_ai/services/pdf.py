"""Approved-note PDF. Server-side template rendering with fpdf2: text is drawn, never
interpreted as HTML, so user content cannot inject markup. Bundled Noto fonts (SIL OFL,
src/medvya_ai/fonts) with HarfBuzz shaping give correct Devanagari (Hindi, Marathi)."""

from dataclasses import dataclass
from datetime import date, datetime
from importlib import resources

from fpdf import FPDF

from medvya_ai.schemas import FactOut, NoteContent
from medvya_ai.services import render

_FONTS = resources.files("medvya_ai") / "fonts"
LATIN, DEVANAGARI = "NotoSans", "NotoSansDevanagari"


@dataclass(frozen=True)
class Document:
    clinic_name: str
    clinician_name: str
    patient_ref: str
    specialty: str | None
    scheduled_date: date
    approved_at: datetime | None
    rev_no: int
    template: str
    content: NoteContent
    facts: list[FactOut]
    content_hash: str  # sha256 of the JSON export this PDF was rendered from


class _Pdf(FPDF):
    def __init__(self, content_hash: str) -> None:
        super().__init__()
        self._hash = content_hash
        with resources.as_file(_FONTS / f"{LATIN}-Regular.ttf") as p:
            self.add_font(LATIN, fname=str(p))
        with resources.as_file(_FONTS / f"{DEVANAGARI}-Regular.ttf") as p:
            self.add_font(DEVANAGARI, fname=str(p))
        self.set_fallback_fonts([DEVANAGARI])
        self.set_text_shaping(True)
        self.set_auto_page_break(auto=True, margin=18)
        self.alias_nb_pages()

    def footer(self) -> None:
        self.set_y(-14)
        self.set_font(LATIN, size=7)
        self.set_text_color(90)
        self.cell(0, 5, f"sha256 {self._hash}  |  page {self.page_no()}/{{nb}}", align="C")

    def heading(self, text: str, size: int = 12) -> None:
        self.ln(2)
        self.set_font(LATIN, size=size)
        self.set_text_color(0)
        self.multi_cell(0, 7, text, new_x="LMARGIN", new_y="NEXT")

    def para(self, text: str, size: int = 10) -> None:
        self.set_font(LATIN, size=size)
        self.set_text_color(0)
        self.multi_cell(0, 5.5, text, new_x="LMARGIN", new_y="NEXT")


def build_pdf(doc: Document) -> bytes:
    pdf = _Pdf(doc.content_hash)
    pdf.set_title(f"Clinical note {doc.patient_ref}")
    pdf.set_author(doc.clinic_name)
    pdf.add_page()
    pdf.heading(doc.clinic_name, 16)
    pdf.para(
        f"Patient: {doc.patient_ref}    Encounter date: {doc.scheduled_date.isoformat()}"
        + (f"    Specialty: {doc.specialty}" if doc.specialty else "")
    )
    approved = doc.approved_at.strftime("%Y-%m-%d %H:%M UTC") if doc.approved_at else "not approved"
    pdf.para(
        f"Approved by {doc.clinician_name} on {approved}    "
        f"Note version {doc.rev_no} ({doc.template})"
    )
    pdf.para(
        "AI-assisted draft reviewed and approved by the named clinician. "
        "Not a diagnosis or prescription by itself.",
        size=8,
    )
    if doc.template == "concise":
        pdf.heading("Summary")
        pdf.para(render.concise_text(doc.content, doc.facts))
    else:
        for title, text in render.soap_sections(doc.content):
            pdf.heading(title)
            pdf.para(text)
        if doc.content.summary.strip():
            pdf.heading("Summary")
            pdf.para(doc.content.summary.strip())
        for title, lines in render.fact_groups(doc.facts):
            pdf.heading(title)
            pdf.para("\n".join(f"- {line}" for line in lines))
        if doc.content.missing_or_uncertain:
            pdf.heading("To confirm")
            pdf.para("\n".join(f"- {x}" for x in doc.content.missing_or_uncertain))
    return bytes(pdf.output())
