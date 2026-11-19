//! Port `Services/Ai/RefineHairService.cs` — orkestrasi refine hair:
//! trimap dari mask sekarang → inferensi matting → blend alpha HANYA di band
//! unknown dengan feather transisi, sehingga area solid tidak bergeser.

use crate::matting::MattingSession;
use crate::trimap::build_within;
use pxa_core::image_buf::Image;

/// Hasil satu kali refine hair, siap dipasang ke sesi mask.
pub struct RefineHairResult {
    /// Mask alpha baru (panjang W*H).
    pub mask: Vec<u8>,
    pub min_x: i32,
    pub min_y: i32,
    pub max_x: i32,
    pub max_y: i32,
    /// True bila tidak ada piksel yang berubah.
    pub no_change: bool,
}

pub struct RefineHair {
    session: MattingSession,
}

impl RefineHair {
    pub fn new(session: MattingSession) -> Self {
        Self { session }
    }

    /// Jalankan refine hair.
    /// - `image` = sumber RGBA (full image)
    /// - `current_mask` = mask alpha sekarang
    /// - `selection` = opsional, pembatas band
    /// - `band_radius`, `feather`
    pub fn refine(
        &mut self,
        image: &Image,
        current_mask: &[u8],
        selection: Option<&[u8]>,
        band_radius: usize,
        feather: usize,
    ) -> Result<RefineHairResult, String> {
        let w = image.width;
        let h = image.height;
        let n = w * h;
        if current_mask.len() < n {
            return Err("mask terlalu pendek".into());
        }

        // 1) Trimap (opsional dibatasi selection)
        let trimap = build_within(current_mask, selection, w, h, band_radius);
        if !trimap.has_unknown() {
            // Solid seluruhnya → tak ada tepi untuk di-refine.
            return Ok(RefineHairResult {
                mask: current_mask.to_vec(),
                min_x: 0,
                min_y: 0,
                max_x: -1,
                max_y: -1,
                no_change: true,
            });
        }

        // 2) Inferensi matting pada SELURUH gambar (potret butuh konteks penuh).
        let alpha = self.session.matte(image)?;
        if alpha.len() != n {
            // resize balik (harusnya sudah pas, jaga-jaga)
            let side = (n as f64).sqrt() as usize;
            let tmp = Image::from_alpha(&alpha, side, n / side.max(1));
            let alpha = tmp.resize(w, h).extract_alpha_mask();
            return self.finalize_blend(image, current_mask, selection, &alpha, &trimap, band_radius, feather);
        }
        self.finalize_blend(image, current_mask, selection, &alpha, &trimap, band_radius, feather)
    }

    fn finalize_blend(
        &self,
        _image: &Image,
        current_mask: &[u8],
        _selection: Option<&[u8]>,
        alpha: &[u8],
        trimap: &crate::trimap::TrimapResult,
        band_radius: usize,
        feather: usize,
    ) -> Result<RefineHairResult, String> {
        let w = trimap.width;
        let h = trimap.height;
        let n = w * h;
        if alpha.len() < n {
            return Err("alpha hasil lebih pendek dari canvas".into());
        }

        // 3) Blend: alpha baru HANYA menimpa di band unknown (dengan feather).
        let weight = build_blend_weight(&trimap.trimap, w, 0, 0, w, h, feather);
        let mut result = current_mask.to_vec();
        let mut min_x = i32::MAX;
        let mut min_y = i32::MAX;
        let mut max_x = i32::MIN;
        let mut max_y = i32::MIN;
        let mut changed = 0;

        for y in 0..h {
            let row_c = y * w;
            for x in 0..w {
                let t = weight[row_c + x];
                if t <= 0.0 {
                    continue;
                }
                let gi = row_c + x;
                let old_a = current_mask[gi] as i32;
                let new_a = alpha[gi] as i32;
                let blended = (old_a + (new_a - old_a) * t as i32) as i32;
                let blended = blended.max(0).min(255);
                if blended.abs_diff(old_a) >= 1 {
                    result[gi] = blended as u8;
                    changed += 1;
                    min_x = min_x.min(x as i32);
                    min_y = min_y.min(y as i32);
                    max_x = max_x.max(x as i32);
                    max_y = max_y.max(y as i32);
                }
            }
        }

        let _ = band_radius; // dipakai di trimap
        let no_change = changed == 0;
        Ok(RefineHairResult {
            mask: result,
            min_x: if no_change { 0 } else { min_x },
            min_y: if no_change { 0 } else { min_y },
            max_x: if no_change { -1 } else { max_x },
            max_y: if no_change { -1 } else { max_y },
            no_change,
        })
    }
}

/// Bobot blend 0..1 per piksel: 1.0 di band unknown, feather turun ke 0 di luar.
fn build_blend_weight(trimap: &[u8], w: usize, cx0: usize, cy0: usize, cw: usize, ch: usize, feather: usize) -> Vec<f32> {
    let n = cw * ch;
    let mut unknown = vec![0u8; n];
    for y in 0..ch {
        for x in 0..cw {
            if trimap[(cy0 + y) * w + cx0 + x] == 128 {
                unknown[y * cw + x] = 255;
            }
        }
    }
    if feather == 0 {
        return unknown.iter().map(|&v| if v > 0 { 1.0 } else { 0.0 }).collect();
    }
    let r = (feather / 2).max(1);
    let mut spread = unknown.clone();
    for _ in 0..2 {
        spread = box_dilate(&spread, cw, ch, r);
    }
    let mut weight = vec![0.0f32; n];
    for i in 0..n {
        if unknown[i] > 0 {
            weight[i] = 1.0;
            continue;
        }
        let t = spread[i] as f32 / 255.0;
        weight[i] = t * t * (3.0 - 2.0 * t); // smoothstep
        if weight[i] < 0.02 {
            weight[i] = 0.0;
        }
    }
    weight
}

/// Max filter (dilate) separable 8-bit, radius r (Chebyshev box).
fn box_dilate(src: &[u8], w: usize, h: usize, r: usize) -> Vec<u8> {
    let n = src.len();
    let mut tmp = vec![0u8; n];
    for y in 0..h {
        let row = y * w;
        for x in 0..w {
            let mut best = 0u8;
            let x0 = x.saturating_sub(r);
            let x1 = (x + r).min(w - 1);
            for xx in x0..=x1 {
                if src[row + xx] > best {
                    best = src[row + xx];
                }
            }
            tmp[row + x] = best;
        }
    }
    let mut dst = vec![0u8; n];
    for x in 0..w {
        for y in 0..h {
            let mut best = 0u8;
            let y0 = y.saturating_sub(r);
            let y1 = (y + r).min(h - 1);
            for yy in y0..=y1 {
                if tmp[yy * w + x] > best {
                    best = tmp[yy * w + x];
                }
            }
            dst[y * w + x] = best;
        }
    }
    dst
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn blend_weight_hard_when_no_feather() {
        // Trimap sederhana: 4x4, band = baris 1
        let trimap = vec![
            0, 0, 0, 0,
            255, 128, 128, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
        ];
        let w = build_blend_weight(&trimap, 4, 0, 0, 4, 4, 0);
        assert_eq!(w[5], 1.0); // band
        assert_eq!(w[1], 0.0); // luar
    }

    #[test]
    fn blend_weight_spreads_with_feather() {
        let trimap = vec![
            0, 0, 0, 0,
            255, 128, 128, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
        ];
        let w = build_blend_weight(&trimap, 4, 0, 0, 4, 4, 4);
        // Di luar band tapi dekat → bobot > 0
        assert!(w[1] > 0.0 || w[9] > 0.0, "feather harus nyebar, got [1]={} [9]={}", w[1], w[9]);
    }

    #[test]
    fn box_dilate_spreads() {
        let src = vec![0u8; 5 * 5];
        let mut src = src;
        src[12] = 255; // tengah
        let out = box_dilate(&src, 5, 5, 1);
        // Tetangga harus ikut 255
        assert_eq!(out[12], 255);
        assert_eq!(out[7], 255); // atas
        assert_eq!(out[17], 255); // bawah
    }

    #[test]
    fn refine_no_unknown_is_no_change() {
        // Mask solid semua FG → tak ada unknown → no_change
        let img = {
            let mut i = Image::new(4, 4);
            for p in i.rgba.iter_mut() {
                *p = 0;
            }
            for p in i.rgba.chunks_exact_mut(4) {
                p.copy_from_slice(&[100, 150, 200, 255]);
            }
            i
        };
        let mask = vec![255u8; 16];
        let session_path = std::path::PathBuf::from("nonexistent.onnx");
        // Tanpa model valid, new() gagal → test pakai RefineHair tak bisa.
        // Test logika no_change lewat trimap saja:
        let tr = crate::trimap::build(&mask, 4, 4, 2);
        assert!(!tr.has_unknown());
    }
}