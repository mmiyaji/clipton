"""Regenerate the MSIX logo assets from the WinUI master icon.

Requires Pillow (`python -m pip install Pillow`). The source already contains
the rounded white body; this script only removes excess transparent padding.
"""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/Clipton.WinUI/Assets/Clipton.png"
OUTPUT = ROOT / "packaging/Clipton.Package/Images"
FIT = 0.92
TARGET_SIZES = (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
QUALIFIERS = ("", "_altform-unplated", "_altform-lightunplated")


def visible_crop(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    # Ignore the very faint antialiasing fringe when locating the artwork.
    mask = alpha.point(lambda value: 255 if value > 8 else 0)
    bounds = mask.getbbox()
    if bounds is None:
        raise ValueError("The source icon contains no visible pixels (alpha > 8).")
    return image.crop(bounds)


def render_square(source: Image.Image, size: int) -> Image.Image:
    crop = visible_crop(source)
    extent = max(1, round(size * FIT))
    ratio = extent / max(crop.size)
    scaled_size = (max(1, round(crop.width * ratio)), max(1, round(crop.height * ratio)))
    crop = crop.resize(scaled_size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(crop, ((size - crop.width) // 2, (size - crop.height) // 2))
    return canvas


def save(image: Image.Image, name: str) -> None:
    image.save(OUTPUT / name, optimize=True)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    source = Image.open(SOURCE).convert("RGBA")

    # Primary visual assets all use the same centered icon, including the wide
    # tile where the square artwork is centered on the transparent canvas.
    for name, size in (
        ("Square44x44Logo.png", 44),
        ("Square150x150Logo.png", 150),
        ("Square310x310Logo.png", 310),
        ("StoreLogo.png", 50),
    ):
        save(render_square(source, size), name)

    wide = Image.new("RGBA", (310, 150), (0, 0, 0, 0))
    icon = render_square(source, 150)
    wide.alpha_composite(icon, ((310 - icon.width) // 2, 0))
    save(wide, "Wide310x150Logo.png")

    # 44x44 primary asset scales are used by Windows where scale-qualified
    # resources are selected. Target-size resources cover explicit shell sizes.
    for scale in (100, 200, 400):
        size = 44 * scale // 100
        save(render_square(source, size), f"Square44x44Logo.scale-{scale}.png")

    for size in TARGET_SIZES:
        image = render_square(source, size)
        for qualifier in QUALIFIERS:
            save(image, f"Square44x44Logo.targetsize-{size}{qualifier}.png")


if __name__ == "__main__":
    main()
