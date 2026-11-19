//! Port `Services/Ai/TrimapBuilder.cs` — bangun trimap (0 bg / 128 unknown / 255 fg)
//! dari mask alpha, dengan band erode/dilate. Dipakai Refine Hair.

pub struct TrimapResult {
    pub trimap: Vec<u8>,
    pub width: usize,
    pub height: usize,
    pub min_x: i32,
    pub min_y: i32,
    pub max_x: i32,
    pub max_y: i32,
}

impl TrimapResult {
    pub fn has_unknown(&self) -> bool {
        self.max_x >= self.min_x && self.max_y >= self.min_y
    }
}

pub fn build(mask: &[u8], width: usize, height: usize, band_radius: usize) -> TrimapResult {
    let n = width * height;
    debug_assert!(mask.len() >= n, "mask terlalu pendek");
    let r = band_radius.max(1);

    let fg: Vec<u8> = mask.iter().take(n).map(|&v| if v >= 128 { 1 } else { 0 }).collect();
    let eroded = erode(&fg, width, height, r);
    let dilated = dilate(&fg, width, height, r);

    let mut trimap = vec![0u8; n];
    let (mut min_x, mut min_y) = (i32::MAX, i32::MAX);
    let (mut max_x, mut max_y) = (i32::MIN, i32::MIN);

    for i in 0..n {
        let v = if eroded[i] != 0 {
            255u8
        } else if dilated[i] == 0 {
            0
        } else {
            128
        };
        trimap[i] = v;
        if v == 128 {
            let x = (i % width) as i32;
            let y = (i / width) as i32;
            min_x = min_x.min(x);
            min_y = min_y.min(y);
            max_x = max_x.max(x);
            max_y = max_y.max(y);
        }
    }

    TrimapResult { trimap, width, height, min_x, min_y, max_x, max_y }
}

/// Versi dibatasi selection: di luar selection, piksel band dipaksa FG/BG sesuai mask.
pub fn build_within(mask: &[u8], selection: Option<&[u8]>, width: usize, height: usize, band_radius: usize) -> TrimapResult {
    let mut result = build(mask, width, height, band_radius);
    let Some(sel) = selection else { return result };
    if sel.len() < width * height {
        return result;
    }

    let n = width * height;
    let (mut min_x, mut min_y) = (i32::MAX, i32::MAX);
    let (mut max_x, mut max_y) = (i32::MIN, i32::MIN);
    for i in 0..n {
        if result.trimap[i] != 128 {
            continue;
        }
        if sel[i] == 0 {
            result.trimap[i] = if mask[i] >= 128 { 255 } else { 0 };
            continue;
        }
        let x = (i % width) as i32;
        let y = (i / width) as i32;
        min_x = min_x.min(x);
        min_y = min_y.min(y);
        max_x = max_x.max(x);
        max_y = max_y.max(y);
    }

    result.min_x = if min_x == i32::MAX { 0 } else { min_x };
    result.min_y = if min_y == i32::MAX { 0 } else { min_y };
    result.max_x = if max_x == i32::MIN { -1 } else { max_x };
    result.max_y = if max_y == i32::MIN { -1 } else { max_y };
    result
}

// ---- Morfologi (box min/max separable, Chebyshev) ----

fn erode(src: &[u8], w: usize, h: usize, r: usize) -> Vec<u8> {
    let n = src.len();
    let mut tmp = vec![0u8; n];
    min_h(src, &mut tmp, w, h, r);
    let mut dst = vec![0u8; n];
    min_v(&tmp, &mut dst, w, h, r);
    dst
}
fn dilate(src: &[u8], w: usize, h: usize, r: usize) -> Vec<u8> {
    let n = src.len();
    let mut tmp = vec![0u8; n];
    max_h(src, &mut tmp, w, h, r);
    let mut dst = vec![0u8; n];
    max_v(&tmp, &mut dst, w, h, r);
    dst
}
fn min_h(src: &[u8], dst: &mut [u8], w: usize, h: usize, r: usize) {
    for y in 0..h {
        let row = y * w;
        for x in 0..w {
            let mut m = 1u8;
            let x0 = x.saturating_sub(r);
            let x1 = (x + r).min(w - 1);
            for sx in x0..=x1 {
                if src[row + sx] == 0 {
                    m = 0;
                    break;
                }
            }
            dst[row + x] = m;
        }
    }
}
fn min_v(src: &[u8], dst: &mut [u8], w: usize, h: usize, r: usize) {
    for y in 0..h {
        let y0 = y.saturating_sub(r);
        let y1 = (y + r).min(h - 1);
        let row = y * w;
        for x in 0..w {
            let mut m = 1u8;
            for sy in y0..=y1 {
                if src[sy * w + x] == 0 {
                    m = 0;
                    break;
                }
            }
            dst[row + x] = m;
        }
    }
}
fn max_h(src: &[u8], dst: &mut [u8], w: usize, h: usize, r: usize) {
    for y in 0..h {
        let row = y * w;
        for x in 0..w {
            let mut m = 0u8;
            let x0 = x.saturating_sub(r);
            let x1 = (x + r).min(w - 1);
            for sx in x0..=x1 {
                if src[row + sx] != 0 {
                    m = 1;
                    break;
                }
            }
            dst[row + x] = m;
        }
    }
}
fn max_v(src: &[u8], dst: &mut [u8], w: usize, h: usize, r: usize) {
    for y in 0..h {
        let y0 = y.saturating_sub(r);
        let y1 = (y + r).min(h - 1);
        let row = y * w;
        for x in 0..w {
            let mut m = 0u8;
            for sy in y0..=y1 {
                if src[sy * w + x] != 0 {
                    m = 1;
                    break;
                }
            }
            dst[row + x] = m;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn trimap_solid_all_fg() {
        let mask = vec![255u8; 4 * 4];
        let r = build(&mask, 4, 4, 2);
        // Semua FG, erode semua 1 → semua 255, tak ada unknown
        assert!(!r.has_unknown());
        assert!(r.trimap.iter().all(|&v| v == 255));
    }

    #[test]
    fn trimap_solid_all_bg() {
        let mask = vec![0u8; 4 * 4];
        let r = build(&mask, 4, 4, 2);
        assert!(!r.has_unknown());
        assert!(r.trimap.iter().all(|&v| v == 0));
    }

    #[test]
    fn trimap_band_between_fg_bg() {
        // 8x8: kiri FG, kanan BG, band unknown di tengah
        let mut mask = vec![0u8; 8 * 8];
        for y in 0..8 {
            for x in 0..4 {
                mask[y * 8 + x] = 255;
            }
        }
        let r = build(&mask, 8, 8, 1);
        assert!(r.has_unknown());
        let band = r.trimap.iter().filter(|&&v| v == 128).count();
        assert!(band > 0, "harus ada band unknown, dapat {}", band);
    }

    #[test]
    fn build_within_limits_to_selection() {
        let mut mask = vec![0u8; 8 * 8];
        for y in 0..8 {
            for x in 0..4 {
                mask[y * 8 + x] = 255;
            }
        }
        let mut sel = vec![0u8; 8 * 8];
        // selection hanya separuh atas
        for y in 0..4 {
            for x in 0..8 {
                sel[y * 8 + x] = 255;
            }
        }
        let full = build(&mask, 8, 8, 1);
        let within = build_within(&mask, Some(&sel), 8, 8, 1);
        // Dalam selection: unknown tetap; di luar: dipaksa FG/BG
        let unknown_full = full.trimap.iter().filter(|&&v| v == 128).count();
        let unknown_within = within.trimap.iter().filter(|&&v| v == 128).count();
        assert!(unknown_within <= unknown_full);
    }
}