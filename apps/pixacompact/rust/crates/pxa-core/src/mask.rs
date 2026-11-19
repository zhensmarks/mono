//! Port `Services/Editing/MaskOperations.cs` — operasi piksel pada mask 8-bit.

use crate::image_buf::Image;

pub fn invert(mask: &mut [u8]) {
    for v in mask.iter_mut() {
        *v = 255 - *v;
    }
}

pub fn fill(mask: &mut [u8], value: u8) {
    mask.fill(value);
}

/// Port `ShiftEdge`: box kernel separabel, max (expand) / min (contract).
pub fn shift_edge(mask: &mut [u8], width: usize, height: usize, radius: usize, expand: bool) {
    if radius == 0 || width == 0 || height == 0 || mask.len() < width * height {
        return;
    }
    let src = mask.to_vec();
    let mut tmp = vec![0u8; src.len()];
    pass_h(&src, &mut tmp, width, height, radius, expand);
    pass_v(&tmp, mask, width, height, radius, expand);
}

fn pass_h(src: &[u8], dst: &mut [u8], w: usize, h: usize, r: usize, expand: bool) {
    for y in 0..h {
        let row = y * w;
        for x in 0..w {
            let mut best = src[row + x];
            let x0 = x.saturating_sub(r);
            let x1 = (x + r).min(w - 1);
            for sx in x0..=x1 {
                let v = src[row + sx];
                if expand {
                    if v > best {
                        best = v;
                    }
                } else if v < best {
                    best = v;
                }
            }
            dst[row + x] = best;
        }
    }
}

fn pass_v(src: &[u8], dst: &mut [u8], w: usize, h: usize, r: usize, expand: bool) {
    for y in 0..h {
        let y0 = y.saturating_sub(r);
        let y1 = (y + r).min(h - 1);
        for x in 0..w {
            let mut best = src[y * w + x];
            for sy in y0..=y1 {
                let v = src[sy * w + x];
                if expand {
                    if v > best {
                        best = v;
                    }
                } else if v < best {
                    best = v;
                }
            }
            dst[y * w + x] = best;
        }
    }
}

/// Port `Feather`: 3x box blur, radius box = max(1, round(radius/1.5)).
pub fn feather(mask: &mut [u8], width: usize, height: usize, radius: i32) {
    if radius <= 0 || width == 0 || height == 0 || mask.len() < width * height {
        return;
    }
    let r = ((radius as f64 / 1.5).round() as i32).max(1) as usize;
    let mut tmp = vec![0u8; mask.len()];
    for _ in 0..3 {
        box_blur_h(mask, &mut tmp, width, height, r);
        box_blur_v(&tmp, mask, width, height, r);
    }
}

fn box_blur_h(src: &[u8], dst: &mut [u8], w: usize, h: usize, r: usize) {
    let window = (2 * r + 1) as i64;
    for y in 0..h {
        let row = y * w;
        let mut sum: i64 = 0;
        for x in -(r as i64)..=(r as i64) {
            sum += src[row + x.clamp(0, w as i64 - 1) as usize] as i64;
        }
        for x in 0..w {
            dst[row + x] = (sum / window) as u8;
            let add = (x as i64 + r as i64 + 1).clamp(0, w as i64 - 1) as usize;
            let sub = (x as i64 - r as i64).clamp(0, w as i64 - 1) as usize;
            sum += src[row + add] as i64 - src[row + sub] as i64;
        }
    }
}

fn box_blur_v(src: &[u8], dst: &mut [u8], w: usize, h: usize, r: usize) {
    let window = (2 * r + 1) as i64;
    for x in 0..w {
        let mut sum: i64 = 0;
        for y in -(r as i64)..=(r as i64) {
            sum += src[y.clamp(0, h as i64 - 1) as usize * w + x] as i64;
        }
        for y in 0..h {
            dst[y * w + x] = (sum / window) as u8;
            let add = (y as i64 + r as i64 + 1).clamp(0, h as i64 - 1) as usize;
            let sub = (y as i64 - r as i64).clamp(0, h as i64 - 1) as usize;
            sum += src[add * w + x] as i64 - src[sub * w + x] as i64;
        }
    }
}

/// Port `Defringe`: piksel tepi mengambil warna dari piksel paling opaque di sekitarnya.
pub fn defringe(img: &mut Image, mask: &[u8], radius: i32) {
    if radius <= 0 {
        return;
    }
    let (w, h) = (img.width, img.height);
    if mask.len() < w * h {
        return;
    }
    let mut fg = vec![[0u8; 4]; w * h];
    for i in 0..w * h {
        let bi = i * 4;
        fg[i] = [
            img.rgba[bi],
            img.rgba[bi + 1],
            img.rgba[bi + 2],
            (mask[i] as u32 * img.rgba[bi + 3] as u32 / 255) as u8,
        ];
    }
    for y in 0..h {
        for x in 0..w {
            let idx = y * w + x;
            let a_here = mask[idx];
            if a_here == 0 || a_here == 255 {
                continue;
            }
            let mut best_a: i32 = -1;
            let mut best = [fg[idx][0], fg[idx][1], fg[idx][2]];
            let x0 = x.saturating_sub(radius as usize);
            let x1 = (x + radius as usize).min(w - 1);
            let y0 = y.saturating_sub(radius as usize);
            let y1 = (y + radius as usize).min(h - 1);
            for sy in y0..=y1 {
                for sx in x0..=x1 {
                    let si = sy * w + sx;
                    let a = fg[si][3] as i32;
                    if a > best_a {
                        best_a = a;
                        best = [fg[si][0], fg[si][1], fg[si][2]];
                    }
                }
            }
            if best_a <= 0 {
                continue;
            }
            let t = 1.0 - a_here as f64 / 255.0;
            let bi = idx * 4;
            for c in 0..3 {
                img.rgba[bi + c] = lerp(img.rgba[bi + c], best[c], t);
            }
        }
    }
}

#[inline]
fn lerp(a: u8, b: u8, t: f64) -> u8 {
    let v = a as f64 + (b as f64 - a as f64) * t;
    (v.clamp(0.0, 255.0) + 0.5) as u8
}

/// Bounding box piksel mask dengan nilai di rentang (lo, hi) eksklusif.
pub fn mask_bounds(
    mask: &[u8],
    width: usize,
    height: usize,
    lo_exclusive: u8,
    hi_exclusive: u8,
) -> Option<[i32; 4]> {
    let mut min_x = i32::MAX;
    let mut min_y = i32::MAX;
    let mut max_x = i32::MIN;
    let mut max_y = i32::MIN;
    for y in 0..height {
        let row = y * width;
        for x in 0..width {
            let v = mask[row + x];
            if v > lo_exclusive && v < hi_exclusive {
                min_x = min_x.min(x as i32);
                min_y = min_y.min(y as i32);
                max_x = max_x.max(x as i32);
                max_y = max_y.max(y as i32);
            }
        }
    }
    if max_x >= min_x && max_y >= min_y {
        Some([min_x, min_y, max_x, max_y])
    } else {
        None
    }
}

/// Bounding box semua piksel mask > 0.
pub fn alpha_bounds(mask: &[u8], width: usize, height: usize) -> Option<[i32; 4]> {
    let mut min_x = i32::MAX;
    let mut min_y = i32::MAX;
    let mut max_x = i32::MIN;
    let mut max_y = i32::MIN;
    for y in 0..height {
        let row = y * width;
        for x in 0..width {
            if mask[row + x] == 0 {
                continue;
            }
            min_x = min_x.min(x as i32);
            min_y = min_y.min(y as i32);
            max_x = max_x.max(x as i32);
            max_y = max_y.max(y as i32);
        }
    }
    if max_x >= min_x && max_y >= min_y {
        Some([min_x, min_y, max_x, max_y])
    } else {
        None
    }
}

pub fn threshold(coverage: &mut [u8], t: u8) {
    for v in coverage.iter_mut() {
        *v = if *v >= t { 255 } else { 0 };
    }
}

/// Port `RefineEdgeBand`: shift+feather lokal di dalam kuas, blend berbobot hardness.
#[allow(clippy::too_many_arguments)]
pub fn refine_edge_band(
    mask: &mut [u8],
    width: usize,
    height: usize,
    cx: f64,
    cy: f64,
    radius: f64,
    shift_px: i32,
    expand: bool,
    feather_px: i32,
    hardness: f64,
) {
    if width == 0 || height == 0 || radius <= 0.5 {
        return;
    }
    let r2 = radius * radius;
    let mut min_x = (cx - radius).floor() as i64;
    let mut max_x = (cx + radius).ceil() as i64;
    let mut min_y = (cy - radius).floor() as i64;
    let mut max_y = (cy + radius).ceil() as i64;
    min_x = min_x.max(0);
    min_y = min_y.max(0);
    max_x = max_x.min(width as i64 - 1);
    max_y = max_y.min(height as i64 - 1);
    if max_x < min_x || max_y < min_y {
        return;
    }
    let (bw, bh) = ((max_x - min_x + 1) as usize, (max_y - min_y + 1) as usize);
    let mut crop = vec![0u8; bw * bh];
    for y in 0..bh {
        let src = ((min_y as usize + y) * width) + min_x as usize;
        crop[y * bw..y * bw + bw].copy_from_slice(&mask[src..src + bw]);
    }
    let mut cleaned = crop.clone();
    if shift_px > 0 {
        shift_edge(&mut cleaned, bw, bh, shift_px as usize, expand);
    }
    if feather_px > 0 {
        feather(&mut cleaned, bw, bh, feather_px);
    }
    let hh = hardness.clamp(0.0, 1.0);
    for y in 0..bh {
        let dy = (min_y as usize + y) as f64 - cy;
        for x in 0..bw {
            let dx = (min_x as usize + x) as f64 - cx;
            let d2 = dx * dx + dy * dy;
            if d2 > r2 {
                continue;
            }
            let d = d2.sqrt() / radius;
            let w = if hh >= 0.999 {
                1.0
            } else {
                1.0 - ((d - hh) / (1.0 - hh)).clamp(0.0, 1.0)
            };
            if w <= 0.0 {
                continue;
            }
            let ci = y * bw + x;
            let gi = (min_y as usize + y) * width + min_x as usize + x;
            let a = crop[ci] as f64;
            let b = cleaned[ci] as f64;
            mask[gi] = (a + (b - a) * w + 0.5) as u8;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn invert_and_fill() {
        let mut m = vec![0u8, 128, 255];
        invert(&mut m);
        assert_eq!(m, vec![255, 127, 0]);
        fill(&mut m, 42);
        assert_eq!(m, vec![42, 42, 42]);
    }

    #[test]
    fn shift_edge_expand_then_contract() {
        // 9x9, satu piksel putih di tengah.
        let (w, h) = (9, 9);
        let mut m = vec![0u8; w * h];
        m[4 * w + 4] = 255;

        let mut grow = m.clone();
        shift_edge(&mut grow, w, h, 2, true);
        // Radius 2 → kotak 5x5 penuh putih (jarak Chebyshev, bukan disk).
        assert_eq!(grow[2 * w + 2], 255);
        assert_eq!(grow[2 * w + 6], 255);
        assert_eq!(grow[(2 * w) + 1], 0);

        let mut shrink = grow.clone();
        shift_edge(&mut shrink, w, h, 2, false);
        assert_eq!(shrink[4 * w + 4], 255);
        assert_eq!(shrink[2 * w + 2], 0);
    }

    #[test]
    fn feather_spreads_edges_and_keeps_extremes() {
        let (w, h) = (32, 32);
        let mut m = vec![0u8; w * h];
        for y in 8..24 {
            for x in 8..24 {
                m[y * w + x] = 255;
            }
        }
        feather(&mut m, w, h, 6);
        assert!(m[16 * w + 16] > 200, "tengah tetap hampir solid, dapat {}", m[16 * w + 16]);
        assert_eq!(m[0], 0, "pojok harus tetap kosong");
        // Tepi jadi gradasi: ada nilai parsial di sekitar batas x=8..24.
        let edge = m[16 * w + 8];
        assert!(edge > 0 && edge < 255, "tepi harus parsial, dapat {edge}");
    }

    #[test]
    fn bounds_and_threshold() {
        let (w, h) = (5, 4);
        let mut m = vec![0u8; w * h];
        m[1 * w + 2] = 200;
        m[3 * w + 4] = 100;
        assert_eq!(alpha_bounds(&m, w, h), Some([2, 1, 4, 3]));
        assert_eq!(mask_bounds(&m, w, h, 150, 255), Some([2, 1, 2, 1]));
        threshold(&mut m, 150);
        assert_eq!(m[1 * w + 2], 255);
        assert_eq!(m[3 * w + 4], 0);
    }

    #[test]
    fn defringe_pulls_color_from_opaque_neighbour() {
        let mut img = Image::new(3, 1);
        img.set_pixel(0, 0, [0, 200, 0, 255]);
        img.set_pixel(1, 0, [0, 0, 0, 255]);
        img.set_pixel(2, 0, [0, 0, 0, 255]);
        // Kiri solid (255) supaya jadi sumber warna; tengah piksel tepi parsial.
        let mask = vec![255u8, 40, 0];
        defringe(&mut img, &mask, 2);
        let px = img.pixel(1, 0);
        assert!(px[1] > 0, "hijau harus merembes ke piksel tepi: {px:?}");
    }
}