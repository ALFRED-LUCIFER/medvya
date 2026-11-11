# ADR-018 · Server PDFs with QuestPDF (Community licence) and embedded Noto fonts

- **Status:** Accepted, 11 Nov 2026 (owner decision). Settles Q-05. Tickets: MDV-P2-017 (prescription), MDV-P2-013 (signed note), MDV-P2-018 and MDV-P2-019 (investigation slip and referral letter, with a hash and QR verify, #353).
- **Context:** The signed note and the printed prescription are stored, hashed PDFs. The first writer (`PlainPdf`, no dependency) used the built-in Courier font in Windows-1252, so any Hindi, Tamil or Arabic text (a patient's name, a free-text medicine, a note) was refused and the doctor had to print from the browser. Clinics in India and the UAE write in those scripts every day. A PDF library has to shape Indic and Arabic text correctly, run on the Linux App Service (`DOTNETCORE|10.0`, linux-x64), and cost nothing for the pilot.

## Decision

1. **QuestPDF** (`Directory.Packages.props`) renders every server PDF through `PlainPdf.Write` (`src/BuildingBlocks/Medvya.BuildingBlocks.Infrastructure/PlainPdf.cs`). Its native Skia library ships in the NuGet package; nothing is installed on the server.
2. **Fonts are embedded, never taken from the machine:** Noto Sans, Noto Sans Devanagari, Noto Sans Tamil and Noto Sans Arabic, regular and bold (SIL Open Font Licence, `Fonts/OFL.txt`), registered from embedded resources with system fonts switched off. The same paper comes out on a laptop and on App Service.
3. **A character no embedded font has is refused, never printed as an empty box.** QuestPDF's missing-glyph error becomes `PdfUnsupportedTextException` (422 `pdf_unsupported_text`), and the browser's print view stays the fallback. That error's message quotes the text, so it is never logged or passed on.
4. **Licence: QuestPDF Community.** It is free for organisations under USD 1M annual gross revenue. It does not cover public-sector or publicly listed organisations as the licensee. Above the limit the Professional licence (from USD 999 a year) is needed, with 90 days to move.

## Evidence (spike, 11 Nov 2026, comment on #348)

- Hindi conjuncts, the pre-base vowel sign and the reph, Tamil split vowels, and Arabic joining with right-to-left order (also mixed with a Latin drug name) printed correctly. Bold worked in every script.
- It ran in `mcr.microsoft.com/dotnet/aspnet:10.0` on linux-x64 with no extra packages. The page was pixel-identical on macOS and Linux.
- Fonts embed as subsets: 73 KB for an A5 page in four scripts.

## Consequences

- Text copied out of the PDF is garbled for Hindi and Arabic, and Tamil vowels come out in visual order (the ToUnicode map for joined glyphs). The printed page is right. The web view stays the searchable, screen-reader copy. The PDF is not tagged (PDF/UA).
- **Re-check the licence** before Medvya's revenue nears USD 1M, and before signing a public-sector customer as licensee. It is listed in `docs/05-delivery/session-handoff.md`.
- The repository carries about 2.6 MB of font files.
- Tests read the PDFs back with PdfPig (test project only): each page's text, its size, and that every glyph lies inside the margins.

## Alternatives considered

- **Keep `PlainPdf` with Courier:** no Indic or Arabic, so the browser print stays the only way to print them. Rejected by the owner.
- **Our own TrueType embedding and shaping:** shaping Devanagari, Tamil and Arabic means HarfBuzz-level work. Far more code than the problem needs.
- **A headless browser printing HTML to PDF:** shapes well, but a Chromium per App Service instance on a one-core B1 is heavy and slow to start.
