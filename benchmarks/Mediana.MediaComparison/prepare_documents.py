# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Vadim Belov <https://belov.us>

"""Create authored Office workloads with fixed geometry, fonts and numeric caches."""

from __future__ import annotations

import argparse
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

from docx import Document
from docx.shared import Inches, Pt
from pptx import Presentation
from pptx.enum.dml import MSO_THEME_COLOR
from pptx.enum.shapes import MSO_SHAPE
from pptx.util import Inches as SlideInches
from pptx.util import Pt as SlidePoints


def normalize_package(path: Path) -> None:
    """Keep ZIP metadata stable across repeated input preparation."""
    with ZipFile(path) as archive:
        parts = [(name, archive.read(name)) for name in archive.namelist()]
    with ZipFile(path, "w", ZIP_DEFLATED) as archive:
        for name, contents in parts:
            info = ZipInfo(name)
            info.compress_type = ZIP_DEFLATED
            archive.writestr(info, contents)


def prepare_workbook(output: Path) -> None:
    """Write 400,000 numeric cells, including 80,000 stored formula results."""
    spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
    relationships = (
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    )
    package_relationships = (
        "http://schemas.openxmlformats.org/package/2006/relationships"
    )
    with ZipFile(output / "numeric-workbook.xlsx", "w", ZIP_DEFLATED) as archive:
        archive.writestr(
            "[Content_Types].xml",
            (
                '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                '<Default Extension="xml" ContentType="application/xml"/>'
                '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
                '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
                "</Types>"
            ),
        )
        archive.writestr(
            "_rels/.rels",
            f'<Relationships xmlns="{package_relationships}"><Relationship Id="rId1" Type="{relationships}/officeDocument" Target="xl/workbook.xml"/></Relationships>',
        )
        archive.writestr(
            "xl/workbook.xml",
            f'<workbook xmlns="{spreadsheet}" xmlns:r="{relationships}"><sheets><sheet name="Measurements" sheetId="1" r:id="rId1"/></sheets></workbook>',
        )
        archive.writestr(
            "xl/_rels/workbook.xml.rels",
            f'<Relationships xmlns="{package_relationships}"><Relationship Id="rId1" Type="{relationships}/worksheet" Target="worksheets/sheet1.xml"/></Relationships>',
        )
        with archive.open("xl/worksheets/sheet1.xml", "w") as stream:
            stream.write(
                f'<worksheet xmlns="{spreadsheet}"><dimension ref="A1:T20000"/><sheetData>'.encode()
            )
            for row in range(1, 20001):
                cells: list[str] = []
                for column in range(1, 21):
                    address = f"{chr(64 + column)}{row}"
                    formula = "<f>ROW()*20+COLUMN()</f>" if column % 5 == 0 else ""
                    cells.append(
                        f'<c r="{address}">{formula}<v>{row * 20 + column}</v></c>'
                    )
                stream.write((f'<row r="{row}">' + "".join(cells) + "</row>").encode())
            stream.write(b"</sheetData></worksheet>")


def prepare_word(output: Path, photo: Path) -> None:
    """Create a letter-sized page with paragraphs, table, picture and footer."""
    document = Document()
    section = document.sections[0]
    section.page_width, section.page_height = Inches(8.5), Inches(11)
    section.top_margin = section.bottom_margin = Inches(0.6)
    section.left_margin = section.right_margin = Inches(0.7)
    for name in ("Normal", "Title", "Heading 1"):
        style = document.styles[name]
        style.font.name = "DejaVu Sans"
        style.font.size = Pt(11)
    document.add_heading("Multimedia processing report", 0)
    for index in range(6):
        document.add_paragraph(
            f"Section {index + 1}. A reproducible document contains text, figures and measurements. "
            "Line wrapping and table geometry are part of the page preview."
        )
    table = document.add_table(rows=6, cols=3)
    table.style = "Table Grid"
    for row in range(6):
        for column in range(3):
            table.cell(row, column).text = f"Measurement {row + 1}.{column + 1}"
    document.add_picture(str(photo), width=Inches(2.1))
    section.footer.paragraphs[0].text = "Mediana document comparison"
    document.save(str(output / "report-page.docx"))


def prepare_presentation(output: Path, photo: Path) -> None:
    """Create a 16:9 slide with a title placeholder, photograph and vector chart."""
    presentation = Presentation()
    presentation.slide_width, presentation.slide_height = SlideInches(10), SlideInches(
        5.625
    )
    slide = presentation.slides.add_slide(presentation.slide_layouts[5])
    title = slide.shapes.title
    if title is None:
        raise ValueError("Title-only slide layout has no title placeholder")
    title.text = "Media workloads"
    for run in title.text_frame.paragraphs[0].runs:
        run.font.name, run.font.size = "DejaVu Sans", SlidePoints(26)
    slide.shapes.add_picture(
        str(photo), SlideInches(5.5), SlideInches(1.25), width=SlideInches(4)
    )
    for index in range(12):
        shape = slide.shapes.add_shape(
            MSO_SHAPE.RECTANGLE,
            SlideInches(0.6),
            SlideInches(1.35 + index * 0.3),
            SlideInches(1.4 + index * 0.22),
            SlideInches(0.2),
        )
        shape.fill.solid()
        shape.fill.fore_color.theme_color = MSO_THEME_COLOR.ACCENT_1
        shape.line.fill.background()
    presentation.save(str(output / "media-slide.pptx"))


def main() -> None:
    """Prepare public authored documents from an explicit photo input."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--photo", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    prepare_workbook(args.output)
    prepare_word(args.output, args.photo)
    prepare_presentation(args.output, args.photo)
    for name in ("numeric-workbook.xlsx", "report-page.docx", "media-slide.pptx"):
        normalize_package(args.output / name)


if __name__ == "__main__":
    main()
