//! Port `Services/Editing/SelectionState.cs` + `SelectionContour.cs`.

use std::collections::{HashMap, HashSet};

use crate::mask;
use crate::raster::{self, CombineOp, Region};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CombineMode {
    Replace,
    Add,
    Subtract,
    Intersect,
}

#[derive(Clone)]
pub struct SelectionState {
    pub width: usize,
    pub height: usize,
    /// Coverage 0..255, panjang = width*height.
    pub coverage: Vec<u8>,
    pub version: u64,
}

impl SelectionState {
    pub fn new(width: usize, height: usize) -> Self {
        Self { width, height, coverage: vec![0; width * height], version: 0 }
    }

    pub fn combine_region(&mut self, region: &Region, mode: CombineMode, anti_alias: bool, feather: f64) {
        if region.is_empty() {
            return;
        }
        let mut tmp = vec![0u8; self.coverage.len()];
        raster::fill_region(region, &mut tmp, self.width, self.height, 255, CombineOp::Replace, anti_alias, feather);
        self.combine_raw(&tmp, mode);
    }

    pub fn combine_raw(&mut self, region: &[u8], mode: CombineMode) {
        if region.len() != self.coverage.len() {
            return;
        }
        match mode {
            CombineMode::Replace => self.coverage.copy_from_slice(region),
            CombineMode::Add => {
                for (dst, r) in self.coverage.iter_mut().zip(region) {
                    if *r > *dst {
                        *dst = *r;
                    }
                }
            }
            CombineMode::Subtract => {
                for (dst, r) in self.coverage.iter_mut().zip(region) {
                    if *r == 0 {
                        continue;
                    }
                    *dst = (*dst as u32 * (255 - *r as u32) / 255) as u8;
                }
            }
            CombineMode::Intersect => {
                for (dst, r) in self.coverage.iter_mut().zip(region) {
                    *dst = (*dst as u32 * *r as u32 / 255) as u8;
                }
            }
        }
        self.version += 1;
    }

    pub fn grow(&mut self, radius: usize) {
        if radius == 0 {
            return;
        }
        mask::shift_edge(&mut self.coverage, self.width, self.height, radius, true);
        self.version += 1;
    }

    pub fn shrink(&mut self, radius: usize) {
        if radius == 0 {
            return;
        }
        mask::shift_edge(&mut self.coverage, self.width, self.height, radius, false);
        self.version += 1;
    }

    pub fn feather(&mut self, radius: i32) {
        if radius <= 0 {
            return;
        }
        mask::feather(&mut self.coverage, self.width, self.height, radius);
        self.version += 1;
    }

    pub fn invert(&mut self) {
        for v in self.coverage.iter_mut() {
            *v = 255 - *v;
        }
        self.version += 1;
    }

    pub fn clear(&mut self) {
        self.coverage.fill(0);
        self.version += 1;
    }

    pub fn select_all(&mut self) {
        self.coverage.fill(255);
        self.version += 1;
    }

    pub fn transform(&mut self, dx: i64, dy: i64) {
        if dx == 0 && dy == 0 {
            return;
        }
        let src = self.coverage.clone();
        self.coverage.fill(0);
        for y in 0..self.height {
            let sy = y as i64 - dy;
            if sy < 0 || sy >= self.height as i64 {
                continue;
            }
            let dst_row = y * self.width;
            let src_row = sy as usize * self.width;
            for x in 0..self.width {
                let sx = x as i64 - dx;
                if sx < 0 || sx >= self.width as i64 {
                    continue;
                }
                self.coverage[dst_row + x] = src[src_row + sx as usize];
            }
        }
        self.version += 1;
    }

    pub fn has_selection(&self) -> bool {
        self.coverage.iter().any(|v| *v != 0)
    }

    pub fn is_all(&self) -> bool {
        !self.coverage.is_empty() && self.coverage.iter().all(|v| *v == 255)
    }

    pub fn count_selected(&self) -> u64 {
        self.coverage.iter().filter(|v| **v != 0).count() as u64
    }

    pub fn bounds(&self) -> Option<[i32; 4]> {
        mask::alpha_bounds(&self.coverage, self.width, self.height)
    }
}

// ========================
// KONTUR (marching squares)
// ========================

pub const DEFAULT_THRESHOLD: u8 = 128;

type Key = (i32, i32, bool); // (i, j, vertikal?)

/// Port `SelectionContour.Trace`: loop polygon tertutup pada coverage >= threshold.
pub fn trace(coverage: &[u8], width: usize, height: usize, threshold: u8) -> Vec<Vec<[f64; 2]>> {
    let mut result = Vec::new();
    if width == 0 || height == 0 || coverage.len() < width * height {
        return result;
    }
    let cw = width as i32 + 2;
    let ch = height as i32 + 2;

    let mut b_min_x = i32::MAX;
    let mut b_max_x = -1;
    let mut b_min_y = i32::MAX;
    let mut b_max_y = -1;
    for y in 0..height {
        let row = y * width;
        for x in 0..width {
            if coverage[row + x] >= threshold {
                b_min_x = b_min_x.min(x as i32);
                b_max_x = b_max_x.max(x as i32);
                b_min_y = b_min_y.min(y as i32);
                b_max_y = b_max_y.max(y as i32);
            }
        }
    }
    if b_max_x < 0 {
        return result;
    }
    let i_start = (b_min_x - 1).max(0);
    let i_end = (b_max_x + 1).min(cw - 2);
    let j_start = (b_min_y - 1).max(0);
    let j_end = (b_max_y + 1).min(ch - 2);

    let val = |i: i32, j: i32| -> f64 {
        if i < 1 || i > width as i32 || j < 1 || j > height as i32 {
            return 0.0;
        }
        coverage[(j as usize - 1) * width + (i as usize - 1)] as f64
    };

    let t = threshold as f64;
    let mut h_pts: HashMap<Key, [f64; 2]> = HashMap::new();
    let mut v_pts: HashMap<Key, [f64; 2]> = HashMap::new();

    let mut h_point = |i: i32, j: i32| -> [f64; 2] {
        let key: Key = (i, j, false);
        if let Some(p) = h_pts.get(&key) {
            return *p;
        }
        let a = val(i, j);
        let b = val(i + 1, j);
        let tt = (t - a) / (b - a);
        let p = [i as f64 + tt - 0.5, j as f64 - 0.5];
        h_pts.insert(key, p);
        p
    };
    let mut v_point = |i: i32, j: i32| -> [f64; 2] {
        let key: Key = (i, j, true);
        if let Some(p) = v_pts.get(&key) {
            return *p;
        }
        let a = val(i, j);
        let b = val(i, j + 1);
        let tt = (t - a) / (b - a);
        let p = [i as f64 - 0.5, j as f64 + tt - 0.5];
        v_pts.insert(key, p);
        p
    };

    let mut segments: Vec<(Key, Key)> = Vec::new();
    for j in j_start..=j_end {
        for i in i_start..=i_end {
            let a = val(i, j) >= t;
            let b = val(i + 1, j) >= t;
            let c = val(i + 1, j + 1) >= t;
            let d = val(i, j + 1) >= t;
            let idx = (a as u8) | ((b as u8) << 1) | ((c as u8) << 2) | ((d as u8) << 3);
            if idx == 0 || idx == 15 {
                continue;
            }
            let top: Key = (i, j, false);
            let bottom: Key = (i, j + 1, false);
            let left: Key = (i, j, true);
            let right: Key = (i + 1, j, true);

            if matches!(idx, 1 | 2 | 3 | 5 | 7 | 9 | 11 | 13 | 14) {
                h_point(i, j);
            }
            if matches!(idx, 4 | 6 | 7 | 8 | 10 | 11 | 12 | 13 | 14) {
                h_point(i, j + 1);
            }
            if matches!(idx, 1 | 3 | 5 | 8 | 9 | 10 | 11 | 12 | 14) {
                v_point(i, j);
            }
            if matches!(idx, 2 | 4 | 5 | 6 | 8 | 10 | 12 | 13 | 14) {
                v_point(i + 1, j);
            }

            match idx {
                1 | 14 => segments.push((left, top)),
                2 | 13 => segments.push((top, right)),
                3 | 12 => segments.push((left, right)),
                4 | 11 => segments.push((right, bottom)),
                6 | 9 => segments.push((top, bottom)),
                7 | 8 => segments.push((bottom, left)),
                5 => {
                    let center = (val(i, j) + val(i + 1, j) + val(i + 1, j + 1) + val(i, j + 1)) * 0.25;
                    if center >= t {
                        segments.push((top, right));
                        segments.push((bottom, left));
                    } else {
                        segments.push((left, top));
                        segments.push((right, bottom));
                    }
                }
                10 => {
                    let center = (val(i, j) + val(i + 1, j) + val(i + 1, j + 1) + val(i, j + 1)) * 0.25;
                    if center >= t {
                        segments.push((left, top));
                        segments.push((right, bottom));
                    } else {
                        segments.push((top, right));
                        segments.push((bottom, left));
                    }
                }
                _ => {}
            }
        }
    }
    if segments.is_empty() {
        return result;
    }

    let mut adj: HashMap<Key, Vec<Key>> = HashMap::new();
    for (a, b) in &segments {
        adj.entry(*a).or_default().push(*b);
        adj.entry(*b).or_default().push(*a);
    }

    fn seg_key(a: Key, b: Key) -> (Key, Key) {
        if a <= b {
            (a, b)
        } else {
            (b, a)
        }
    }
    let mut used: HashSet<(Key, Key)> = HashSet::new();

    for (sa, sb) in segments.clone() {
        if used.contains(&seg_key(sa, sb)) {
            continue;
        }
        let mut loop_pts: Vec<[f64; 2]> = Vec::new();
        let start = sa;
        let mut current = sa;
        let mut prev: Option<Key> = None;

        loop {
            let p = if current.2 {
                v_point(current.0, current.1)
            } else {
                h_point(current.0, current.1)
            };
            loop_pts.push(p);
            used.insert(seg_key(current, prev.unwrap_or(sb)));

            let mut next: Option<Key> = None;
            if let Some(nbrs) = adj.get(&current) {
                for n in nbrs {
                    if Some(*n) == prev {
                        continue;
                    }
                    if !used.contains(&seg_key(current, *n)) {
                        next = Some(*n);
                        break;
                    }
                }
            }
            let Some(n) = next else { break };
            prev = Some(current);
            current = n;
            if current == start {
                break;
            }
        }
        if loop_pts.len() >= 3 {
            result.push(loop_pts);
        }
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;

    fn rect_selection(w: usize, h: usize, x0: usize, y0: usize, x1: usize, y1: usize) -> SelectionState {
        let mut sel = SelectionState::new(w, h);
        for y in y0..y1 {
            for x in x0..x1 {
                sel.coverage[y * w + x] = 255;
            }
        }
        sel.version += 1;
        sel
    }

    #[test]
    fn combine_modes_follow_source_semantics() {
        let (w, h) = (10, 10);
        let mut sel = rect_selection(w, h, 0, 0, 5, 5);
        let mut other = vec![0u8; w * h];
        for y in 3..8 {
            for x in 3..8 {
                other[y * w + x] = 255;
            }
        }
        let mut add = sel.clone();
        add.combine_raw(&other, CombineMode::Add);
        assert_eq!(add.coverage[7 * w + 7], 255);
        assert_eq!(add.coverage[1 * w + 1], 255);

        let mut intersect = sel.clone();
        intersect.combine_raw(&other, CombineMode::Intersect);
        assert_eq!(intersect.coverage[4 * w + 4], 255);
        assert_eq!(intersect.coverage[1 * w + 1], 0);

        let mut sub = sel.clone();
        sub.combine_raw(&other, CombineMode::Subtract);
        assert_eq!(sub.coverage[4 * w + 4], 0);
        assert_eq!(sub.coverage[1 * w + 1], 255);

        sel.combine_raw(&other, CombineMode::Replace);
        assert_eq!(sel.coverage[7 * w + 7], 255);
    }

    #[test]
    fn grow_shrink_invert_clear() {
        let (w, h) = (15, 15);
        let mut sel = rect_selection(w, h, 6, 6, 9, 9);
        sel.grow(2);
        assert_eq!(sel.coverage[4 * w + 4], 255);
        sel.shrink(2);
        assert_eq!(sel.coverage[4 * w + 4], 0);
        assert_eq!(sel.coverage[7 * w + 7], 255);
        sel.invert();
        assert_eq!(sel.coverage[7 * w + 7], 0);
        sel.clear();
        assert!(!sel.has_selection());
        sel.select_all();
        assert!(sel.is_all());
        assert_eq!(sel.count_selected(), (w * h) as u64);
    }

    #[test]
    fn transform_moves_coverage() {
        let (w, h) = (10, 10);
        let mut sel = SelectionState::new(w, h);
        sel.coverage[0] = 255;
        sel.transform(2, 3);
        assert_eq!(sel.coverage[3 * w + 2], 255);
        assert_eq!(sel.coverage[0], 0);
    }

    #[test]
    fn contour_of_square_has_four_points_per_side() {
        let (w, h) = (8, 8);
        let sel = rect_selection(w, h, 2, 2, 6, 6);
        let loops = trace(&sel.coverage, w, h, DEFAULT_THRESHOLD);
        assert_eq!(loops.len(), 1, "satu loop tertutup");
        let l = &loops[0];
        // Marching squares memotong tiap transisi piksel, bukan hanya sudut:
        // 4 sisi x 4 crossing = 16 titik untuk kotak 4x4.
        assert_eq!(l.len(), 16, "titik kontur: {:?}", l);
        let xs: Vec<f64> = l.iter().map(|p| p[0]).collect();
        let ys: Vec<f64> = l.iter().map(|p| p[1]).collect();
        assert!((xs.iter().cloned().fold(f64::MAX, f64::min) - 2.0).abs() < 0.01);
        assert!((xs.iter().cloned().fold(f64::MIN, f64::max) - 5.99).abs() < 0.02);
        assert!((ys.iter().cloned().fold(f64::MAX, f64::min) - 2.0).abs() < 0.01);
        assert!((ys.iter().cloned().fold(f64::MIN, f64::max) - 5.99).abs() < 0.02);
    }

    #[test]
    fn empty_selection_has_no_contour() {
        let sel = SelectionState::new(8, 8);
        assert!(trace(&sel.coverage, 8, 8, DEFAULT_THRESHOLD).is_empty());
    }
}