"""Synthetic PSD fixture and round-trip baseline for Frame Studio tests.

This uses psd-tools only to build and re-open test data. It is not the Frame
Studio writer, does not replace Smart Object contents, and makes no claim about
compatibility with any private/customer PSD.

Run the tests with:
    python3 apps/frame-studio/engine.Tests/test_synthetic_frame_psd_fixture.py

Regenerate the checked-in fixture with:
    python3 apps/frame-studio/engine.Tests/test_synthetic_frame_psd_fixture.py --write-fixture
"""
from __future__ import annotations

import argparse
import io
import tempfile
import unittest
import uuid
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from psd_tools import PSDImage
from psd_tools.constants import Tag
from psd_tools.psd.descriptor import DescriptorBlock, DescriptorBlock2, RawData, String
from psd_tools.psd.engine_data import EngineData
from psd_tools.psd.linked_layer import LinkedLayer, LinkedLayerType
from psd_tools.psd.tagged_blocks import LinkedLayers, PlacedLayerType


ROOT = Path(__file__).resolve().parent
FIXTURE_PATH = ROOT / "Fixtures" / "synthetic_frame_targets.psd"
CANVAS = (24, 16)
TEXT = "Studio Test"
TEXT_FONT = "SyntheticSans"
TEXT_SIZE = 12.0
SLOT_RECTS = {
    "Smart Object Slot XL": (3, 4, 8, 7),
    "Smart Object Slot S": (15, 5, 5, 5),
}
EXPECTED_LAYER_ORDER = [
    "Background",
    "Smart Object Slot S",
    "Smart Object Slot XL",
    "Foreground - Frame overlay",
    "Foreground - Asset badge",
    "Type - Title",
]


def _engine_string(value: str) -> bytes:
    return b"(\xfe\xff" + value.encode("utf-16-be") + b")"


def _rgba_png(size: tuple[int, int], color: tuple[int, int, int, int]) -> bytes:
    buffer = io.BytesIO()
    Image.new("RGBA", size, color).save(buffer, format="PNG")
    return buffer.getvalue()


def _engine_data() -> bytes:
    # EngineData stores the native text styling block. Keep it deliberately
    # small and deterministic while exercising character and paragraph runs.
    return b"".join(
        (
            b"<< /EngineDict << /Editor << /Text ",
            _engine_string(TEXT),
            b" >> /StyleRun << /RunArray [ "
            b"<< /StyleSheet << /StyleSheetData "
            b"<< /Font 0 /FontSize 12.0 /FauxBold true /FauxItalic false "
            b"/FillColor << /Type 1 /Values [ 1.0 0.9 0.6 0.0 ] >> "
            b">> >> >> "
            b"<< /StyleSheet << /StyleSheetData "
            b"<< /Font 0 /FontSize 10.0 /FauxBold false /FauxItalic true "
            b"/FillColor << /Type 1 /Values [ 0.2 0.5 1.0 0.0 ] >> "
            b">> >> >> "
            b"] /RunLengthArray [ 6 5 ] >> "
            b"/ParagraphRun << /RunArray [ "
            b"<< /ParagraphSheet << /Properties "
            b"<< /Justification 1 /FirstLineIndent 4.0 /StartIndent 2.0 "
            b"/EndIndent 1.0 /SpaceBefore 3.0 /SpaceAfter 5.0 >> "
            b">> >> "
            b"] /RunLengthArray [ 11 ] >> >> "
            b"/ResourceDict << /FontSet [ << /Name ",
            _engine_string(TEXT_FONT),
            b" >> ] >> >>",
        )
    )


def _attach_native_type_data(layer) -> None:
    text_data = DescriptorBlock()
    text_data[b"Txt "] = String(TEXT)
    text_data[b"EngineData"] = RawData(_engine_data())
    layer.tagged_blocks.set_data(
        Tag.TYPE_TOOL_OBJECT_SETTING,
        version=1,
        transform=(1.0, 0.0, 0.0, 1.0, 0.0, 0.0),
        text_version=50,
        text_data=text_data,
        warp_version=1,
        warp=DescriptorBlock(),
        left=0,
        top=0,
        right=CANVAS[0],
        bottom=CANVAS[1],
    )


def _attach_embedded_smart_object(document, layer, name: str, png_data: bytes, rect) -> None:
    object_id = str(uuid.uuid5(uuid.NAMESPACE_URL, f"frame-studio-fixture:{name}"))
    config = DescriptorBlock()
    config[b"Idnt"] = String(object_id)
    layer.tagged_blocks.set_data(
        Tag.SMART_OBJECT_LAYER_DATA1,
        kind=b"soLD",
        version=5,
        data=config,
    )
    x, y, width, height = rect
    layer.tagged_blocks.set_data(
        Tag.PLACED_LAYER1,
        kind=b"plcL",
        version=3,
        uuid=object_id.encode("ascii"),
        page=0,
        total_pages=1,
        anti_alias=1,
        layer_type=PlacedLayerType.RASTER,
        transform=(float(x), float(y), float(x + width), float(y),
                   float(x + width), float(y + height), float(x), float(y + height)),
        warp=DescriptorBlock2(),
    )
    linked = LinkedLayer(
        kind=LinkedLayerType.DATA,
        version=4,
        uuid=object_id,
        filename=f"{name.replace(' ', '_')}.png",
        filetype=b"png ",
        creator=b"8BIM",
        data=png_data,
    )
    linked_layers = document.tagged_blocks.get_data(Tag.LINKED_LAYER2)
    if linked_layers is None:
        linked_layers = LinkedLayers()
    linked_layers.append(linked)
    document.tagged_blocks.set_data(Tag.LINKED_LAYER2, linked_layers)


def build_fixture(output_path: Path) -> None:
    """Create a small layered RGB PSD containing two embedded Smart Objects."""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    document = PSDImage.new("RGB", CANVAS, color=(24, 32, 42), depth=8)

    # Add bottom-to-top because psd-tools inserts each new layer at the top.
    background = Image.new("RGBA", CANVAS, (24, 32, 42, 255))
    document.create_pixel_layer(background, name="Background")

    for name, rect, color in (
        ("Smart Object Slot S", SLOT_RECTS["Smart Object Slot S"], (24, 180, 190, 255)),
        ("Smart Object Slot XL", SLOT_RECTS["Smart Object Slot XL"], (210, 100, 52, 255)),
    ):
        x, y, width, height = rect
        slot_image = Image.new("RGB", (width, height), color[:3])
        layer = document.create_pixel_layer(slot_image, name=name, top=y, left=x)
        _attach_embedded_smart_object(
            document, layer, name, _rgba_png((width, height), color), rect
        )
        mask = Image.new("L", (width, height), 255)
        mask.putpixel((0, 0), 0)
        layer.create_mask(mask, top=y, left=x)
        layer.clipping = True

    frame = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    draw = ImageDraw.Draw(frame)
    for x, y, width, height in SLOT_RECTS.values():
        draw.rectangle((x, y, x + width - 1, y + height - 1), outline=(246, 215, 146, 255))
    document.create_pixel_layer(frame, name="Foreground - Frame overlay")

    badge = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    badge_draw = ImageDraw.Draw(badge)
    badge_draw.rectangle((20, 1, 22, 3), fill=(255, 190, 62, 255))
    document.create_pixel_layer(badge, name="Foreground - Asset badge")

    title = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    ImageDraw.Draw(title).text((1, 1), TEXT, font=ImageFont.load_default(), fill=(245, 240, 225, 255))
    title_layer = document.create_pixel_layer(title, name="Type - Title")
    _attach_native_type_data(title_layer)

    document.save(output_path)


def _layers_by_name(document):
    return {layer.name: layer for layer in document.descendants()}


def _type_style_fingerprint(layer) -> bytes:
    engine_data = layer._data.text_data[b"EngineData"].value
    return engine_data.tobytes()


def _type_tagged_block_bytes(layer) -> bytes:
    """Serialize the complete Type Tool tagged block, including its header."""
    return layer.tagged_blocks[Tag.TYPE_TOOL_OBJECT_SETTING].tobytes()


def _composite_rgba(document) -> np.ndarray:
    image = document.composite(force=True, ignore_preview=True).convert("RGBA")
    return np.asarray(image, dtype=np.uint8)


def _assert_outside_slots_equal(test_case: unittest.TestCase, before: np.ndarray, after: np.ndarray) -> None:
    test_case.assertEqual(before.shape, after.shape)
    outside = np.ones(before.shape[:2], dtype=bool)
    for x, y, width, height in SLOT_RECTS.values():
        outside[y:y + height, x:x + width] = False
    test_case.assertTrue(
        np.array_equal(before[outside], after[outside]),
        "composite pixels outside the XL/S target rectangles changed",
    )


class SyntheticFramePsdFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.temp_dir = tempfile.TemporaryDirectory(prefix="frame-studio-synthetic-")
        cls.source_path = Path(cls.temp_dir.name) / "synthetic_frame_targets.psd"
        cls.roundtrip_path = Path(cls.temp_dir.name) / "roundtrip.psd"
        build_fixture(cls.source_path)

    @classmethod
    def tearDownClass(cls) -> None:
        cls.temp_dir.cleanup()

    def test_fixture_has_target_stack_bounds_masks_clipping_and_native_type(self) -> None:
        document = PSDImage.open(self.source_path)
        self.assertEqual(EXPECTED_LAYER_ORDER, [layer.name for layer in document])
        composite = _composite_rgba(document)
        self.assertEqual([24, 32, 42, 255], composite[15, 0].tolist())
        frame_pixel = composite[4, 3]
        self.assertGreater(int(frame_pixel[0]), 230)
        self.assertGreater(int(frame_pixel[1]), 200)
        self.assertGreater(int(frame_pixel[2]), 130)
        self.assertNotEqual(frame_pixel[:3].tolist(), [210, 100, 52])

        for name, expected_rect in SLOT_RECTS.items():
            layer = _layers_by_name(document)[name]
            self.assertEqual("smartobject", layer.kind)
            self.assertEqual(expected_rect, (layer.left, layer.top, layer.width, layer.height))
            self.assertTrue(layer.has_mask())
            self.assertTrue(layer.clipping)
            self.assertEqual("data", layer.smart_object.kind)
            self.assertTrue(layer.smart_object.data.startswith(b"\x89PNG\r\n\x1a\n"))

        text_layer = _layers_by_name(document)["Type - Title"]
        self.assertEqual("type", text_layer.kind)
        self.assertEqual(TEXT, text_layer.text)
        typesetting = text_layer.typesetting
        self.assertEqual(["Studio", " Test"], [run.text for run in typesetting.runs])
        self.assertEqual([TEXT_SIZE, 10.0], [run.style.font_size for run in typesetting.runs])
        self.assertEqual([True, False], [run.style.faux_bold for run in typesetting.runs])
        self.assertEqual([False, True], [run.style.faux_italic for run in typesetting.runs])
        self.assertEqual(1, len(typesetting.paragraphs))
        paragraph_style = typesetting.paragraphs[0].style
        self.assertEqual(4.0, paragraph_style.first_line_indent)
        self.assertEqual(5.0, paragraph_style.space_after)
        style_data = text_layer.engine_dict["StyleRun"]["RunArray"][0]["StyleSheet"]["StyleSheetData"]
        self.assertEqual(TEXT_SIZE, style_data["FontSize"])
        self.assertTrue(style_data["FauxBold"])
        self.assertFalse(style_data["FauxItalic"])
        self.assertEqual(TEXT_FONT, text_layer.resource_dict["FontSet"][0]["Name"])
        self.assertEqual([TEXT_FONT], text_layer.font_names)
        self.assertEqual(
            EngineData.frombytes(_engine_data()).tobytes(),
            text_layer._data.text_data[b"EngineData"].value.tobytes(),
        )

    def test_noop_psd_save_reopen_preserves_type_payload_and_pixels_outside_targets(self) -> None:
        source = PSDImage.open(self.source_path)
        before_composite = _composite_rgba(source)
        before_text = _layers_by_name(source)["Type - Title"]
        text_value = before_text.text
        style_value = _type_style_fingerprint(before_text)
        type_block_bytes = _type_tagged_block_bytes(before_text)

        source.save(self.roundtrip_path)
        reopened = PSDImage.open(self.roundtrip_path)
        after_text = _layers_by_name(reopened)["Type - Title"]

        self.assertEqual(text_value, after_text.text)
        self.assertEqual(style_value, _type_style_fingerprint(after_text))
        self.assertEqual(type_block_bytes, _type_tagged_block_bytes(after_text))
        _assert_outside_slots_equal(self, before_composite, _composite_rgba(reopened))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-fixture", action="store_true", help="regenerate the checked-in PSD fixture")
    args, remaining = parser.parse_known_args()
    if args.write_fixture:
        build_fixture(FIXTURE_PATH)
        print(f"Wrote synthetic fixture: {FIXTURE_PATH}")
        return
    unittest.main(argv=[__file__, *remaining])


if __name__ == "__main__":
    main()
