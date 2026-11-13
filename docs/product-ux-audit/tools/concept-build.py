"""Builds concepts/medvya-workspace-concept.html from the .src.html by embedding the Manrope font (so the file is self-contained).
Run from the repository root: python3 -I docs/product-ux-audit/tools/concept-build.py"""
import base64, glob, pathlib
here = pathlib.Path(__file__).resolve().parent.parent
font = glob.glob(str(here.parents[1] / "node_modules/.pnpm/@fontsource-variable+manrope@*/node_modules/@fontsource-variable/manrope/files/manrope-latin-wght-normal.woff2"))[0]
css = '@font-face{font-family:"Manrope";font-weight:200 800;font-display:swap;src:url(data:font/woff2;base64,' + base64.b64encode(open(font, "rb").read()).decode() + ') format("woff2")}'
src = (here / "concepts/medvya-workspace-concept.src.html").read_text()
assert "/*FONT*/" in src
(here / "concepts/medvya-workspace-concept.html").write_text(src.replace("/*FONT*/", css))
print("built")
