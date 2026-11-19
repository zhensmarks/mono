//! Port `Services/Editing/CompositeRenderer.cs` + magic wand (flood fill).

use crate::image_buf::Image;

/// Komposit mask ke RGB: alpha diambil dari mask.
pub fn compose(rgb: &Image, mask: Option<&[u8]>) -> Image {
    let mut out = rgb.clone();
    if let Some(m) = mask {
        if m.len() >= out.width * out.height {
            out.apply_alpha_mask(m);
        }
    }
    out
}

/// Komposit di atas warna background solid (alpha hasil = 255).
pub fn compose_on_background(rgb: &Image, mask: Option<&[u8]>, background: [u8; 3]) -> Image {
    let (w, h) = (rgb.width, rgb.height);
    let mut out = Image::new(w, h);
    for i in 0..w * h {
        let si = i * 4;
        let a = match mask {
            Some(m) if i < m.len() => m[i],
            _ => rgb.rgba[si + 3],
        };
        if a == 255 {
            out.rgba[si] = rgb.rgba[si];
            out.rgba[si + 1] = rgb.rgba[si + 1];
            out.rgba[si + 2] = rgb.rgba[si + 2];
        } else {
            let t = a as f64 / 255.0;
            for c in 0..3 {
                let v = rgb.rgba[si + c] as f64 * t + background[c] as f64 * (1.0 - t);
                out.rgba[si + c] = (v + 0.5).clamp(0.0, 255.0) as u8;
            }
        }
        out.rgba[si + 3] = 255;
    }
    out
}

/// Overlay quick-mask: warna semi-transparan pada area coverage > 0.
pub fn mask_overlay(
    mask: &[u8],
    width: usize,
    height: usize,
    color: [u8; 3],
    max_alpha: u8,
    invert: bool,
) -> Image {
    let mut buf = Image::new(width, height);
    for i in 0..(width * height).min(mask.len()) {
        let v = mask[i] as i32;
        let coverage = if invert { 255 - v } else { v };
        if coverage <= 0 {
            continue;
        }
        let t = coverage as f64 / 255.0 * (max_alpha as f64 / 255.0);
        let di = i * 4;
        buf.rgba[di] = color[0];
        buf.rgba[di + 1] = color[1];
        buf.rgba[di + 2] = color[2];
        buf.rgba[di + 3] = (t * 255.0 + 0.5).clamp(0.0, 255.0) as u8;
    }
    buf
}

/// Outline seleksi: transisi mask 0↔255, 1 px.
pub fn selection_outline(mask: &[u8], width: usize, height: usize, color: [u8; 4]) -> Image {
    let mut buf = Image::new(width, height);
    let inside_at = |i: usize| mask.get(i).copied().unwrap_or(0) > 127;
    for y in 0..height {
        for x in 0..width {
            let i = y * width + x;
            if i >= mask.len() {
                continue;
            }
            let inside = mask[i] > 127;
            let mut edge = false;
            if x + 1 < width {
                edge |= inside_at(i + 1) != inside;
            }
            if x > 0 {
                edge |= inside_at(i - 1) != inside;
            }
            if y + 1 < height {
                edge |= inside_at(i + width) != inside;
            }
            if y > 0 {
                edge |= inside_at(i - width) != inside;
            }
            if edge {
                let di = i * 4;
                buf.rgba[di..di + 4].copy_from_slice(&color);
            }
        }
    }
    buf
}

/// Magic wand: flood fill 4-arah dari (x,y), toleransi jarak warna 0..255.
/// ponytail: satu ambang jarak RGB; C# punya kontiguitas + tolerance terpisah -
/// tambah mode "contiguous=false" kalau user minta seleksi by-warna global.
pub fn magic_wand(img: &Image, x: usize, y: usize, tolerance: u8) -> Vec<u8> {
    let (w, h) = (img.width, img.height);
    let mut out = vec![0u8; w * h];
    if x >= w || y >= h {
        return out;
    }
    let seed = img.pixel(x, y);
    let tol = tolerance as i32;
    let matches = |p: [u8; 4]| -> bool {
        let d = (p[0] as i32 - seed[0] as i32).abs()
            + (p[1] as i32 - seed[1] as i32).abs()
            + (p[2] as i32 - seed[2] as i32).abs();
        d <= tol * 3
    };
    let mut stack = vec![y * w + x];
    out[y * w + x] = 255;
    while let Some(i) = stack.pop() {
        let (px, py) = (i % w, i / w);
        let push = |nx: usize, ny: usize, out: &mut Vec<u8>, stack: &mut Vec<usize>| {
            let ni = ny * w + nx;
            if out[ni] == 0 && matches(img.pixel(nx, ny)) {
                out[ni] = 255;
                stack.push(ni);
            }
        };
        if px > 0 {
            push(px - 1, py, &mut out, &mut stack);
        }
        if px + 1 < w {
            push(px + 1, py, &mut out, &mut stack);
        }
        if py > 0 {
            push(px, py - 1, &mut out, &mut stack);
        }
        if py + 1 < h {
            push(px, py + 1, &mut out, &mut stack);
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn compose_applies_mask_alpha_only() {
        let mut img = Image::new(2, 1);
        img.set_pixel(0, 0, [10, 20, 30, 255]);
        img.set_pixel(1, 0, [40, 50, 60, 255]);
        let m = vec![0u8, 128];
        let out = compose(&img, Some(&m));
        assert_eq!(out.pixel(0, 0), [10, 20, 30, 0]);
        assert_eq!(out.pixel(1, 0), [40, 50, 60, 128]);
    }

    #[test]
    fn compose_on_background_blends_and_is_opaque() {
        let mut img = Image::new(1, 1);
        img.set_pixel(0, 0, [255, 0, 0, 0]);
        let out = compose_on_background(&img, Some(&[0]), [0, 0, 255]);
        // alpha 0 → warna background penuh, hasil opak.
        assert_eq!(out.pixel(0, 0), [0, 0, 255, 255]);

        let out2 = compose_on_background(&img, Some(&[128]), [0, 0, 255]);
        let px = out2.pixel(0, 0);
        assert_eq!(px[3], 255);
        assert!(px[0] > 120 && px[2] > 120, "harus campuran merah+biru: {px:?}");
    }

    #[test]
    fn overlay_only_covers_selected_pixels() {
        let m = vec![0u8, 255];
        let out = mask_overlay(&m, 2, 1, [255, 0, 0], 128, false);
        assert_eq!(out.alpha(0, 0), 0);
        assert_eq!(out.alpha(1, 0), 128);
    }

    #[test]
    fn outline_marks_boundary_only() {
        let (w, h) = (5, 5);
        let mut m = vec![0u8; w * h];
        for y in 1..4 {
            for x in 1..4 {
                m[y * w + x] = 255;
            }
        }
        let out = selection_outline(&m, w, h, [0, 255, 0, 255]);
        assert_eq!(out.alpha(2, 2), 0, "tengah bukan tepi");
        assert_eq!(out.alpha(1, 2), 255, "tepi kiri harus digambar");
    }

    #[test]
    fn wand_selects_flat_region() {
        let (w, h) = (6, 4);
        let mut img = Image::new(w, h);
        for y in 0..h {
            for x in 0..w {
                let c = if x < 3 { [200, 200, 200, 255] } else { [10, 10, 10, 255] };
                img.set_pixel(x, y, c);
            }
        }
        let sel = magic_wand(&img, 0, 0, 10);
        assert_eq!(sel.iter().filter(|v| **v == 255).count(), 3 * h);
        assert_eq!(sel[2], 255);
        assert_eq!(sel[4], 0);
    }
}