//! Port `Services/Editing/BrushTool.cs` — cap disk dengan falloff smoothstep.

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct BrushStamp {
    pub x: f64,
    pub y: f64,
    /// Jari-jari dalam piksel gambar.
    pub radius: f64,
    /// 0 = sangat lembut, 1 = tajam.
    pub hardness: f64,
    /// Batas akumulasi per stroke.
    pub opacity: f64,
    /// true = restore (menuju 255), false = erase (menuju 0).
    pub restore: bool,
}

impl Default for BrushStamp {
    fn default() -> Self {
        Self { x: 0.0, y: 0.0, radius: 20.0, hardness: 0.6, opacity: 0.8, restore: false }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct PixelBounds {
    pub x: usize,
    pub y: usize,
    pub width: usize,
    pub height: usize,
}

impl PixelBounds {
    pub const EMPTY: PixelBounds = PixelBounds { x: 0, y: 0, width: 0, height: 0 };

    pub fn is_empty(&self) -> bool {
        self.width == 0 || self.height == 0
    }

    pub fn right(&self) -> usize {
        self.x + self.width
    }

    pub fn bottom(&self) -> usize {
        self.y + self.height
    }
}

pub fn stamp_bounds(stamp: &BrushStamp, width: usize, height: usize) -> PixelBounds {
    if width == 0 || height == 0 || !stamp.x.is_finite() || !stamp.y.is_finite() {
        return PixelBounds::EMPTY;
    }
    if !stamp.radius.is_finite() || stamp.radius < 0.5 {
        return PixelBounds::EMPTY;
    }
    let cx = stamp.x.round();
    let cy = stamp.y.round();
    let extent = stamp.radius.ceil();
    let left = (cx - extent).max(0.0);
    let top = (cy - extent).max(0.0);
    let right = (cx + extent).min(width as f64 - 1.0);
    let bottom = (cy + extent).min(height as f64 - 1.0);
    if right < left || bottom < top {
        return PixelBounds::EMPTY;
    }
    PixelBounds {
        x: left as usize,
        y: top as usize,
        width: right as usize - left as usize + 1,
        height: bottom as usize - top as usize + 1,
    }
}

fn finite_clamp(v: f64, min: f64, max: f64) -> f64 {
    if v.is_finite() {
        v.clamp(min, max)
    } else {
        min
    }
}

/// Terapkan satu cap ke mask.
pub fn stamp(mask: &mut [u8], width: usize, height: usize, stamp: &BrushStamp) {
    if width == 0 || height == 0 || mask.len() < width * height {
        return;
    }
    let bounds = stamp_bounds(stamp, width, height);
    if bounds.is_empty() {
        return;
    }
    let radius = stamp.radius;
    let hardness = finite_clamp(stamp.hardness, 0.0, 1.0);
    let inner = radius * hardness;
    let falloff = (radius - inner).max(0.5);
    let opacity = finite_clamp(stamp.opacity, 0.0, 1.0);
    let target = if stamp.restore { 255.0 } else { 0.0 };

    for y in bounds.y..bounds.bottom() {
        let dy = y as f64 - stamp.y;
        let row = y * width;
        for x in bounds.x..bounds.right() {
            let dx = x as f64 - stamp.x;
            let dist = (dx * dx + dy * dy).sqrt();
            if dist > radius {
                continue;
            }
            let mut t = if dist <= inner { 1.0 } else { 1.0 - (dist - inner) / falloff };
            if t <= 0.0 {
                continue;
            }
            t = t * t * (3.0 - 2.0 * t);
            let amount = t * opacity;
            if amount <= 0.0 {
                continue;
            }
            let idx = row + x;
            let v = mask[idx] as f64 + (target - mask[idx] as f64) * amount;
            mask[idx] = v.clamp(0.0, 255.0) as u8;
        }
    }
}

/// Cap di sepanjang segmen garis supaya goresan mulus.
pub fn interpolate(out: &mut Vec<BrushStamp>, from: &BrushStamp, to: &BrushStamp, spacing_px: f64) {
    let spacing = if spacing_px.is_finite() { spacing_px.max(1.0) } else { 1.0 };
    let dx = to.x - from.x;
    let dy = to.y - from.y;
    let dist = (dx * dx + dy * dy).sqrt();
    if !dist.is_finite() {
        return;
    }
    if dist <= spacing {
        out.push(*to);
        return;
    }
    let steps = (dist / spacing).ceil().min(100_000.0);
    let steps = steps as usize;
    for i in 1..=steps {
        let t = i as f64 / steps as f64;
        let mut s = *to;
        s.x = from.x + dx * t;
        s.y = from.y + dy * t;
        out.push(s);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn erase_drives_mask_to_zero_at_center() {
        let (w, h) = (41, 41);
        let mut mask = vec![255u8; w * h];
        let st = BrushStamp { x: 20.0, y: 20.0, radius: 10.0, hardness: 1.0, opacity: 1.0, restore: false };
        stamp(&mut mask, w, h, &st);
        assert_eq!(mask[20 * w + 20], 0, "pusat harus terhapus penuh");
        assert_eq!(mask[0], 255, "pojok di luar cap tidak boleh berubah");
    }

    #[test]
    fn restore_raises_alpha() {
        let (w, h) = (21, 21);
        let mut mask = vec![0u8; w * h];
        let st = BrushStamp { x: 10.0, y: 10.0, radius: 5.0, hardness: 0.5, opacity: 1.0, restore: true };
        stamp(&mut mask, w, h, &st);
        assert_eq!(mask[10 * w + 10], 255);
        assert!(mask[10 * w + 14] > 0, "tepi falloff harus tumbuh");
    }

    #[test]
    fn interpolate_spaces_dense_caps() {
        let from = BrushStamp { x: 0.0, y: 0.0, ..Default::default() };
        let to = BrushStamp { x: 10.0, y: 0.0, ..Default::default() };
        let mut out = Vec::new();
        interpolate(&mut out, &from, &to, 2.0);
        assert_eq!(out.len(), 5);
        assert_eq!(out.last().unwrap().x, 10.0);
    }
}