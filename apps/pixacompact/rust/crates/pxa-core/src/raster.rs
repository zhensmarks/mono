//! Port `Services/Editing/MaskRasterizer.cs` — jalur tertutup → mask, supersampling 4x.

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CombineOp {
    Replace,
    Add,
    Subtract,
    Intersect,
}

#[derive(Clone, Debug, Default)]
pub struct Region {
    pub subpaths: Vec<Vec<[f64; 2]>>,
    pub even_odd: bool,
}

impl Region {
    pub fn is_empty(&self) -> bool {
        self.subpaths.iter().all(|s| s.len() < 3)
    }

    pub fn bounds(&self) -> Option<[f64; 4]> {
        let mut min_x = f64::INFINITY;
        let mut min_y = f64::INFINITY;
        let mut max_x = f64::NEG_INFINITY;
        let mut max_y = f64::NEG_INFINITY;
        for sub in &self.subpaths {
            for p in sub {
                min_x = min_x.min(p[0]);
                min_y = min_y.min(p[1]);
                max_x = max_x.max(p[0]);
                max_y = max_y.max(p[1]);
            }
        }
        if min_x.is_finite() && min_y.is_finite() && max_x.is_finite() && max_y.is_finite() {
            Some([min_x, min_y, max_x, max_y])
        } else {
            None
        }
    }

    pub fn rect(x: f64, y: f64, w: f64, h: f64) -> Self {
        Region {
            subpaths: vec![vec![[x, y], [x + w, y], [x + w, y + h], [x, y + h]]],
            even_odd: false,
        }
    }

    /// Elips sebagai poligon 128 sisi (AA 4x membuatnya mulus).
    pub fn ellipse(cx: f64, cy: f64, rx: f64, ry: f64) -> Self {
        const N: usize = 128;
        let mut pts = Vec::with_capacity(N);
        for i in 0..N {
            let a = i as f64 / N as f64 * std::f64::consts::TAU;
            pts.push([cx + rx * a.cos(), cy + ry * a.sin()]);
        }
        Region { subpaths: vec![pts], even_odd: false }
    }

    pub fn lasso(points: &[[f64; 2]]) -> Self {
        Region { subpaths: vec![points.to_vec()], even_odd: false }
    }
}

/// Isi region ke mask, gabung dengan `op` (semantik sama dengan C#).
pub fn fill_region(
    region: &Region,
    mask: &mut [u8],
    width: usize,
    height: usize,
    value: u8,
    op: CombineOp,
    anti_alias: bool,
    feather: f64,
) {
    if region.is_empty() || width == 0 || height == 0 || mask.len() < width * height {
        return;
    }
    let Some([min_x, min_y, max_x, max_y]) = region.bounds() else {
        return;
    };
    if min_x > max_x || min_y > max_y {
        return;
    }

    let feather_radius = if feather.is_finite() {
        feather.clamp(0.0, width.max(height) as f64)
    } else {
        0.0
    };
    let pad = (feather_radius.ceil() as i64).max(1);
    let x0 = ((min_x.floor() as i64 - pad).clamp(0, width as i64 - 1)) as usize;
    let y0 = ((min_y.floor() as i64 - pad).clamp(0, height as i64 - 1)) as usize;
    let x1 = ((max_x.ceil() as i64 + pad).clamp(0, width as i64 - 1)) as usize;
    let y1 = ((max_y.ceil() as i64 + pad).clamp(0, height as i64 - 1)) as usize;
    if x1 < x0 || y1 < y0 {
        return;
    }
    let local_w = x1 - x0 + 1;
    let local_h = y1 - y0 + 1;
    let ss: usize = if anti_alias { 4 } else { 1 };
    let mut coverage = vec![0f32; local_w * local_h];

    let mut total_vertices = 0;
    for s in &region.subpaths {
        if s.len() >= 3 {
            total_vertices += s.len();
        }
    }
    if total_vertices < 3 {
        return;
    }
    let scale = 1.0 / ss as f64;
    let mut crossings: Vec<(f64, i32)> = Vec::with_capacity(16);
    let rows = local_h * ss;
    for sy in 0..rows {
        let y = y0 as f64 + (sy as f64 + 0.5) * scale;
        crossings.clear();
        for sub in &region.subpaths {
            let m = sub.len();
            if m < 3 {
                continue;
            }
            for i in 0..m {
                let a = sub[i];
                let b = sub[(i + 1) % m];
                let (ay, by) = (a[1], b[1]);
                if ay == by {
                    continue;
                }
                let ymin = ay.min(by);
                let ymax = ay.max(by);
                if y < ymin || y >= ymax {
                    continue;
                }
                let t = (y - ay) / (by - ay);
                let x = a[0] + (b[0] - a[0]) * t;
                crossings.push((x, if by > ay { 1 } else { -1 }));
            }
        }
        if crossings.len() < 2 {
            continue;
        }
        crossings.sort_by(|p, q| p.0.partial_cmp(&q.0).unwrap_or(std::cmp::Ordering::Equal));

        let row_index = sy / ss;
        let weight = scale;
        let mut winding = 0;
        for k in 0..crossings.len() - 1 {
            winding += crossings[k].1;
            let inside = if region.even_odd { k % 2 == 0 } else { winding != 0 };
            if !inside {
                continue;
            }
            let xa = crossings[k].0;
            let xb = crossings[k + 1].0;
            if xb <= x0 as f64 || xa >= (x1 + 1) as f64 {
                continue;
            }
            let mut px_start = xa.floor() as i64;
            let mut px_end = xb.ceil() as i64 - 1;
            px_start = px_start.max(x0 as i64);
            px_end = px_end.min(x1 as i64);
            let row_off = row_index * local_w;
            for px in px_start..=px_end {
                let cell_l = px as f64;
                let cell_r = px as f64 + 1.0;
                let overlap = xb.min(cell_r) - xa.max(cell_l);
                if overlap <= 0.0 {
                    continue;
                }
                coverage[row_off + (px as usize - x0)] += (overlap * weight) as f32;
            }
        }
    }

    if feather_radius > 0.5 {
        blur_coverage(&mut coverage, local_w, local_h, feather_radius);
    }

    for ly in 0..local_h {
        let mask_row = (y0 + ly) * width + x0;
        let cov_row = ly * local_w;
        for lx in 0..local_w {
            let c = coverage[cov_row + lx];
            if c <= 0.0 {
                continue;
            }
            apply_coverage(mask, mask_row + lx, c.min(1.0), value, op);
        }
    }
}

fn apply_coverage(mask: &mut [u8], index: usize, coverage: f32, value: u8, op: CombineOp) {
    let cur = mask[index];
    match op {
        CombineOp::Replace => mask[index] = lerp_byte(cur, value, coverage),
        CombineOp::Add => {
            mask[index] = (cur as f32 + value as f32 * coverage).min(255.0) as u8;
        }
        CombineOp::Subtract => {
            mask[index] = (cur as f32 - value as f32 * coverage).max(0.0) as u8;
        }
        CombineOp::Intersect => {
            let target = (cur as f32 * value as f32 / 255.0) as u8;
            mask[index] = lerp_byte(cur, target, coverage);
        }
    }
}

fn lerp_byte(a: u8, b: u8, t: f32) -> u8 {
    let v = a as f32 + (b as f32 - a as f32) * t;
    (v.clamp(0.0, 255.0) + 0.5) as u8
}

/// Box blur float untuk feather coverage.
pub fn blur_coverage(coverage: &mut [f32], width: usize, height: usize, radius: f64) {
    if radius < 0.5 {
        return;
    }
    let r = radius.round() as i64;
    if r < 1 {
        return;
    }
    let r = r as usize;
    let mut tmp = vec![0f32; coverage.len()];
    let norm = 1.0 / (2 * r + 1) as f64;
    for y in 0..height {
        let row = y * width;
        for x in 0..width {
            let mut sum = 0.0f64;
            for dx in -(r as i64)..=(r as i64) {
                let sx = (x as i64 + dx).clamp(0, width as i64 - 1) as usize;
                sum += coverage[row + sx] as f64;
            }
            tmp[row + x] = (sum * norm) as f32;
        }
    }
    for y in 0..height {
        for x in 0..width {
            let mut sum = 0.0f64;
            for dy in -(r as i64)..=(r as i64) {
                let sy = (y as i64 + dy).clamp(0, height as i64 - 1) as usize;
                sum += tmp[sy * width + x] as f64;
            }
            coverage[y * width + x] = (sum * norm) as f32;
        }
    }
}

/// Flatten cubic Bézier (Pen Tool), adaptive de Casteljau.
pub fn flatten_cubic(
    p0: [f64; 2],
    p1: [f64; 2],
    p2: [f64; 2],
    p3: [f64; 2],
    tolerance: f64,
    out: &mut Vec<[f64; 2]>,
) {
    fn dist(a: [f64; 2], b: [f64; 2]) -> f64 {
        let (dx, dy) = (a[0] - b[0], a[1] - b[1]);
        (dx * dx + dy * dy).sqrt()
    }
    fn mid(a: [f64; 2], b: [f64; 2]) -> [f64; 2] {
        [(a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5]
    }
    fn flat(p0: [f64; 2], p1: [f64; 2], p2: [f64; 2], p3: [f64; 2], tol: f64) -> bool {
        let dx = p3[0] - p0[0];
        let dy = p3[1] - p0[1];
        let len2 = dx * dx + dy * dy;
        if len2 < 1e-9 {
            return dist(p1, p0) <= tol && dist(p2, p0) <= tol;
        }
        let d = ((p1[0] - p0[0]) * dy - (p1[1] - p0[1]) * dx).abs() / len2.sqrt();
        let e = ((p2[0] - p0[0]) * dy - (p2[1] - p0[1]) * dx).abs() / len2.sqrt();
        d.max(e) * d.max(e) * len2 <= tol * tol * len2 || d.max(e) <= tol
    }
    fn rec(p0: [f64; 2], p1: [f64; 2], p2: [f64; 2], p3: [f64; 2], tol: f64, out: &mut Vec<[f64; 2]>, depth: u32) {
        if depth > 16 || flat(p0, p1, p2, p3, tol) {
            out.push(p3);
            return;
        }
        let p01 = mid(p0, p1);
        let p12 = mid(p1, p2);
        let p23 = mid(p2, p3);
        let p012 = mid(p01, p12);
        let p123 = mid(p12, p23);
        let m = mid(p012, p123);
        rec(p0, p01, p012, m, tol, out, depth + 1);
        rec(m, p123, p23, p3, tol, out, depth + 1);
    }
    rec(p0, p1, p2, p3, tolerance, out, 0);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rect_fills_expected_area() {
        let (w, h) = (20, 20);
        let mut mask = vec![0u8; w * h];
        let region = Region::rect(5.0, 5.0, 10.0, 10.0);
        fill_region(&region, &mut mask, w, h, 255, CombineOp::Replace, false, 0.0);
        // 10x10 = 100 piksel penuh di dalam, luar kosong (rect half-open [5,15)).
        assert_eq!(mask[10 * w + 10], 255);
        assert_eq!(mask[4 * w + 4], 0);
        assert_eq!(mask[14 * w + 14], 255);
        assert_eq!(mask[15 * w + 15], 0);
        let count = mask.iter().filter(|v| **v > 0).count();
        assert_eq!(count, 100);
    }

    #[test]
    fn ellipse_is_centered_and_round() {
        let (w, h) = (40, 40);
        let mut mask = vec![0u8; w * h];
        let region = Region::ellipse(20.0, 20.0, 12.0, 12.0);
        fill_region(&region, &mut mask, w, h, 255, CombineOp::Replace, true, 0.0);
        assert_eq!(mask[20 * w + 20], 255, "pusat penuh");
        assert_eq!(mask[20 * w + 0], 0, "luar kosong");
        // Sudut bounding box harus kosong (bentuk bulat, bukan kotak).
        assert_eq!(mask[8 * w + 8], 0);
    }

    #[test]
    fn subtract_and_add_compose() {
        let (w, h) = (20, 20);
        let mut mask = vec![0u8; w * h];
        fill_region(&Region::rect(0.0, 0.0, 20.0, 20.0), &mut mask, w, h, 255, CombineOp::Replace, false, 0.0);
        fill_region(&Region::rect(0.0, 0.0, 10.0, 20.0), &mut mask, w, h, 255, CombineOp::Subtract, false, 0.0);
        assert_eq!(mask[5 * w + 5], 0);
        assert_eq!(mask[5 * w + 15], 255);
    }

    #[test]
    fn feather_softens_edge() {
        let (w, h) = (40, 40);
        let mut mask = vec![0u8; w * h];
        fill_region(&Region::rect(10.0, 10.0, 20.0, 20.0), &mut mask, w, h, 255, CombineOp::Replace, true, 4.0);
        let at_edge = mask[20 * w + 10];
        assert!(at_edge > 0 && at_edge < 255, "tepi harus parsial, dapat {at_edge}");
    }
}