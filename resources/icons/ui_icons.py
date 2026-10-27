"""Значки панели команд окон (16×16): одно описание → XAML-ресурсы WPF и SVG для предпросмотра.

Запуск: python3 resources/icons/ui_icons.py [папка для превью]
Результат: src/InventorGostSpec.UI/Themes/Icons.xaml (ключи Icon.<Имя>) и, если указана папка, ui-icons.svg.
Синтаксис путей общий для SVG и WPF: пишите числа через пробел, без сокращений вида «1-1».
"""
import os
import sys

BLUE = "#2E7DD1"
BLUE_D = "#1C5F9E"
BLUE_L = "#A9D0F5"
GRAY = "#616161"
GRAY_L = "#9E9E9E"
PAPER = "#FFFFFF"
GREEN = "#3E9B4F"
RED = "#D64545"
YELLOW = "#F5C342"
YELLOW_D = "#A87B0C"
ORANGE = "#F59B23"

# Каждая фигура: (путь, заливка или None, контур или None, толщина контура).
ICONS = {
    # Сборка
    "Refresh": [
        ("M 13 8 A 5 5 0 1 1 11.5 4.5", None, BLUE, 1.5),
        ("M 13.5 1.5 L 13.5 6 L 9 6 Z", BLUE, None, 0),
    ],
    "Accept": [
        ("M 3 8.5 L 6.5 12 L 13 4.5", None, GREEN, 2),
    ],
    "NextChange": [
        ("M 2.5 2.5 L 13.5 2.5 L 13.5 6.5 L 2.5 6.5 Z", YELLOW, YELLOW_D, 1),
        ("M 8 8.5 L 8 14", None, BLUE, 1.5),
        ("M 5 11.5 L 8 14.5 L 11 11.5", None, BLUE, 1.5),
    ],
    # Строки
    "AddItem": [
        ("M 1.5 2.5 L 10.5 2.5 L 10.5 11.5 L 1.5 11.5 Z", PAPER, GRAY, 1),
        ("M 3.5 5.5 L 8.5 5.5 M 3.5 8.5 L 7 8.5", None, GRAY_L, 1),
        ("M 12.5 8.5 L 12.5 15 M 9.25 11.75 L 15.75 11.75", None, GREEN, 2),
    ],
    "AddText": [
        ("M 1.5 3.5 L 9.5 3.5 M 5.5 3.5 L 5.5 12", None, GRAY, 1.5),
        ("M 12.5 8.5 L 12.5 15 M 9.25 11.75 L 15.75 11.75", None, GREEN, 2),
    ],
    "Delete": [
        ("M 4 4 L 12 12 M 12 4 L 4 12", None, RED, 2),
    ],
    "Up": [
        ("M 8 13.5 L 8 3", None, BLUE, 1.5),
        ("M 4 7 L 8 3 L 12 7", None, BLUE, 1.5),
    ],
    "Down": [
        ("M 8 2.5 L 8 13", None, BLUE, 1.5),
        ("M 4 9 L 8 13 L 12 9", None, BLUE, 1.5),
    ],
    # Структура
    "Section": [
        ("M 4 3.5 L 12 3.5", None, BLUE, 1.5),
        ("M 4 5.5 L 12 5.5", None, BLUE_D, 1),
        ("M 2 9.5 L 14 9.5 M 2 12.5 L 11 12.5", None, GRAY_L, 1),
    ],
    "Group": [
        ("M 5.5 1.5 L 2.5 1.5 L 2.5 14.5 L 5.5 14.5", None, BLUE, 1.5),
        ("M 7 4.5 L 14 4.5 M 7 8 L 13 8 M 7 11.5 L 14 11.5", None, GRAY, 1),
    ],
    "MoveToSection": [
        ("M 1.5 3.5 L 7.5 3.5 M 1.5 6.5 L 5.5 6.5", None, GRAY_L, 1),
        ("M 8.5 9.5 L 14.5 9.5 M 8.5 12.5 L 12.5 12.5", None, GRAY_L, 1),
        ("M 3.5 9 L 3.5 13.5 L 6.5 13.5", None, BLUE, 1.5),
        ("M 5 11.5 L 7 13.5 L 5 15.5", None, BLUE, 1.2),
    ],
    # Позиции
    "Renumber": [
        ("M 4 3 L 3 13 M 7 3 L 6 13 M 1.5 6 L 8.5 6 M 1.5 10 L 8.5 10", None, BLUE, 1.2),
        ("M 10.5 4.5 L 15 4.5 M 10.5 8 L 15 8 M 10.5 11.5 L 15 11.5", None, GRAY_L, 1),
    ],
    "Sort": [
        ("M 3.5 2 L 3.5 13", None, BLUE, 1.5),
        ("M 1 10.5 L 3.5 13.5 L 6 10.5", None, BLUE, 1.5),
        ("M 8 3.5 L 15 3.5 M 8 7.5 L 13 7.5 M 8 11.5 L 11 11.5", None, GRAY, 1.5),
    ],
    "Reserve": [
        ("M 2.5 1.5 L 13.5 1.5 L 13.5 4.5 L 2.5 4.5 Z", BLUE_L, BLUE_D, 1),
        ("M 2.5 6.5 L 13.5 6.5 L 13.5 9.5 L 2.5 9.5 Z", PAPER, GRAY_L, 1),
        ("M 2.5 11.5 L 13.5 11.5 L 13.5 14.5 L 2.5 14.5 Z", BLUE_L, BLUE_D, 1),
    ],
    "Balloon": [
        ("M 9.5 3.5 L 15 3.5 L 15 13.5 L 9.5 13.5 Z", PAPER, GRAY, 1),
        ("M 10 4 L 14.5 4 L 14.5 6 L 10 6 Z", BLUE, None, 0),
        ("M 10 7 L 12 7 L 12 9.5 L 10 9.5 Z", YELLOW, None, 0),
        ("M 7.5 8.5 L 11 8.5", None, BLUE_D, 1),
        ("M 7.5 8.5 A 3.5 3.5 0 1 1 0.5 8.5 A 3.5 3.5 0 1 1 7.5 8.5 Z", PAPER, BLUE_D, 1),
        ("M 4 6.75 L 4 10.25", None, BLUE_D, 1),
    ],
    # Запись
    "Save": [
        ("M 2.5 2.5 L 11.5 2.5 L 13.5 4.5 L 13.5 13.5 L 2.5 13.5 Z", BLUE, BLUE_D, 1),
        ("M 4.5 3 L 10.5 3 L 10.5 6.5 L 4.5 6.5 Z", PAPER, None, 0),
        ("M 4.5 9 L 11.5 9 L 11.5 13 L 4.5 13 Z", BLUE_L, None, 0),
    ],
    "Close": [
        ("M 4.5 4.5 L 11.5 11.5 M 11.5 4.5 L 4.5 11.5", None, GRAY, 1.5),
    ],
    "Properties": [
        ("M 7.5 1.5 L 14.5 1.5 L 14.5 7.5 L 7.5 14.5 L 1.5 8.5 Z", YELLOW, YELLOW_D, 1),
        ("M 11.5 4.5 L 11.5 4.5", None, YELLOW_D, 2),
    ],
    "Excel": [
        ("M 2.5 2.5 L 13.5 2.5 L 13.5 13.5 L 2.5 13.5 Z", PAPER, GREEN, 1),
        ("M 3 3 L 13 3 L 13 5.5 L 3 5.5 Z", GREEN, None, 0),
        ("M 6.5 6 L 6.5 13 M 3 8.5 L 13 8.5 M 3 11 L 13 11", None, GRAY_L, 1),
    ],
    "Settings": [
        ("M 8 1.5 L 8 3.5 M 8 12.5 L 8 14.5 M 1.5 8 L 3.5 8 M 12.5 8 L 14.5 8 M 3.4 3.4 L 4.8 4.8 M 11.2 11.2 L 12.6 12.6 M 3.4 12.6 L 4.8 11.2 M 11.2 4.8 L 12.6 3.4", None, GRAY, 2),
        ("M 12 8 A 4 4 0 1 1 4 8 A 4 4 0 1 1 12 8 Z", PAPER, GRAY, 1.5),
        ("M 9.5 8 A 1.5 1.5 0 1 1 6.5 8 A 1.5 1.5 0 1 1 9.5 8 Z", None, GRAY, 1),
    ],
    "Pdf": [
        ("M 3 1.5 L 10 1.5 L 13 4.5 L 13 14.5 L 3 14.5 Z", PAPER, GRAY, 1),
        ("M 10 1.5 L 10 4.5 L 13 4.5", None, GRAY, 1),
        ("M 1.5 7.5 L 11 7.5 L 11 11.5 L 1.5 11.5 Z", RED, None, 0),
        ("M 5 13 L 11 13", None, GRAY_L, 1),
    ],
    # Прочее
    "Search": [
        ("M 11 6.5 A 4.5 4.5 0 1 1 2 6.5 A 4.5 4.5 0 1 1 11 6.5 Z", None, GRAY, 1.5),
        ("M 10 10 L 14.5 14.5", None, GRAY, 2),
    ],
    "RestoreValue": [
        ("M 3 6 A 5 5 0 1 1 3.5 11", None, BLUE, 1.5),
        ("M 1 2.5 L 1 7 L 5.5 7 Z", BLUE, None, 0),
    ],
}


def to_xaml():
    lines = [
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        "    <!-- Сгенерировано resources/icons/ui_icons.py. Не править вручную. -->",
    ]
    for name, shapes in ICONS.items():
        lines.append(f'    <DrawingImage x:Key="Icon.{name}">')
        lines.append("        <DrawingImage.Drawing>")
        lines.append("            <DrawingGroup>")
        # Прозрачный квадрат задаёт размер 16×16, чтобы значки были одного размера.
        lines.append('                <GeometryDrawing Brush="Transparent" Geometry="M 0 0 L 16 0 L 16 16 L 0 16 Z" />')
        for d, fill, stroke, width in shapes:
            brush = f' Brush="{fill}"' if fill else ""
            if stroke:
                lines.append(f'                <GeometryDrawing{brush} Geometry="{d}">')
                lines.append("                    <GeometryDrawing.Pen>")
                lines.append(f'                        <Pen Brush="{stroke}" Thickness="{width}" StartLineCap="Round" EndLineCap="Round" LineJoin="Round" />')
                lines.append("                    </GeometryDrawing.Pen>")
                lines.append("                </GeometryDrawing>")
            else:
                lines.append(f'                <GeometryDrawing{brush} Geometry="{d}" />')
        lines.append("            </DrawingGroup>")
        lines.append("        </DrawingImage.Drawing>")
        lines.append("    </DrawingImage>")
    lines.append("</ResourceDictionary>")
    return "\n".join(lines) + "\n"


def to_svg(scale):
    cell = 16 * scale + 24
    columns = 6
    rows = (len(ICONS) + columns - 1) // columns
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{columns * cell}" height="{rows * (cell + 16)}" font-family="sans-serif" font-size="11">',
           f'<rect width="100%" height="100%" fill="#F5F6F8"/>']
    for i, (name, shapes) in enumerate(ICONS.items()):
        x = (i % columns) * cell + 12
        y = (i // columns) * (cell + 16) + 8
        out.append(f'<g transform="translate({x} {y}) scale({scale})">')
        for d, fill, stroke, width in shapes:
            out.append(f'<path d="{d}" fill="{fill or "none"}" stroke="{stroke or "none"}" stroke-width="{width}" '
                       'stroke-linecap="round" stroke-linejoin="round"/>')
        out.append("</g>")
        out.append(f'<text x="{x}" y="{y + 16 * scale + 12}">{name}</text>')
    out.append("</svg>")
    return "\n".join(out)


if __name__ == "__main__":
    root = os.path.dirname(os.path.abspath(__file__))
    target = os.path.join(root, "..", "..", "src", "InventorGostSpec.UI", "Themes", "Icons.xaml")
    with open(target, "w", encoding="utf-8") as f:
        f.write(to_xaml())
    if len(sys.argv) > 1:
        for scale in (1, 4):
            with open(os.path.join(sys.argv[1], f"ui-icons-x{scale}.svg"), "w", encoding="utf-8") as f:
                f.write(to_svg(scale))
    print(f"{len(ICONS)} icons -> {os.path.normpath(target)}")
