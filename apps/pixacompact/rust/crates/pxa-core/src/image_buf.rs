//! Port `Services/Editing/PixelBuffer.cs`.

use std::path::Path;

#[derive(Clone, PartialEq, Eq)]
pub struct Image {
    pub width: usize,
    pub height: usize,
    /// RGBA8 non-premultiplied, panjang = width*height*4.
    pub rgba: Vec<u8>,
}

impl std::fmt::Debug for Image {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "Image({}x{})", self.width, self.height)
    }
}

impl Image {
    pub fn new(width: usize, height: usize) -> Self {
        assert!(width > 0 && height > 0, "ukuran gambar harus > 0");
        Self { width, height, rgba: vec![0; width * height * 4] }
    }

    pub fn from_rgba(width: usize, height: usize, rgba: Vec<u8>) -> Self {
        assert_eq!(rgba.len(), width * height * 4, "panjang RGBA tidak sesuai");
        Self { width, height, rgba }
    }

    /// Port `PixelBuffer.FromAlpha`: RGB hitam, alpha dari mask.
    pub fn from_alpha(alpha: &[u8], width: usize, height: usize) -> Self {
        assert!(alpha.len() >= width * height, "panjang alpha tidak sesuai");
        let mut img = Self::new(width, height);
        for i in 0..width * height {
            img.rgba[i * 4 + 3] = alpha[i];
        }
        img
    }

    #[inline]
    pub fn index(&self, x: usize, y: usize) -> usize {
        (y * self.width + x) * 4
    }

    #[inline]
    pub fn in_bounds(&self, x: i64, y: i64) -> bool {
        x >= 0 && y >= 0 && (x as usize) < self.width && (y as usize) < self.height
    }

    /// Alpha 0 bila di luar batas (sama seperti C#).
    #[inline]
    pub fn alpha(&self, x: usize, y: usize) -> u8 {
        if x >= self.width || y >= self.height {
            return 0;
        }
        self.rgba[self.index(x, y) + 3]
    }

    #[inline]
    pub fn pixel(&self, x: usize, y: usize) -> [u8; 4] {
        let i = self.index(x, y);
        [self.rgba[i], self.rgba[i + 1], self.rgba[i + 2], self.rgba[i + 3]]
    }

    #[inline]
    pub fn set_pixel(&mut self, x: usize, y: usize, px: [u8; 4]) {
        if x >= self.width || y >= self.height {
            return;
        }
        let i = self.index(x, y);
        self.rgba[i..i + 4].copy_from_slice(&px);
    }

    pub fn extract_alpha_mask(&self) -> Vec<u8> {
        let mut mask = vec![0u8; self.width * self.height];
        for (i, m) in mask.iter_mut().enumerate() {
            *m = self.rgba[i * 4 + 3];
        }
        mask
    }

    pub fn apply_alpha_mask(&mut self, mask: &[u8]) {
        let total = self.width * self.height;
        for i in 0..total.min(mask.len()) {
            self.rgba[i * 4 + 3] = mask[i];
        }
    }

    pub fn fill_alpha(&mut self, a: u8) {
        for i in 0..self.width * self.height {
            self.rgba[i * 4 + 3] = a;
        }
    }

    pub fn to_opaque_rgb(&self) -> Image {
        let mut out = self.clone();
        out.fill_alpha(255);
        out
    }

    /// Port `PixelBuffer.Resize` (bilinear, math identik).
    pub fn resize(&self, new_w: usize, new_h: usize) -> Image {
        let mut dst = Image::new(new_w, new_h);
        if new_w == self.width && new_h == self.height {
            dst.rgba.copy_from_slice(&self.rgba);
            return dst;
        }
        let sx = self.width as f64 / new_w as f64;
        let sy = self.height as f64 / new_h as f64;
        for y in 0..new_h {
            let fy = (y as f64 + 0.5) * sy - 0.5;
            let mut y0 = fy.floor() as i64;
            let mut wy = fy - y0 as f64;
            if y0 < 0 {
                y0 = 0;
                wy = 0.0;
            }
            let mut y0u = y0 as usize;
            let y1 = (y0u + 1).min(self.height - 1);
            if y0u >= self.height {
                y0u = self.height - 1;
            }
            for x in 0..new_w {
                let fx = (x as f64 + 0.5) * sx - 0.5;
                let mut x0 = fx.floor() as i64;
                let mut wx = fx - x0 as f64;
                if x0 < 0 {
                    x0 = 0;
                    wx = 0.0;
                }
                let mut x0u = x0 as usize;
                let x1 = (x0u + 1).min(self.width - 1);
                if x0u >= self.width {
                    x0u = self.width - 1;
                }
                let i00 = self.index(x0u, y0u);
                let i10 = self.index(x1, y0u);
                let i01 = self.index(x0u, y1);
                let i11 = self.index(x1, y1);
                let d = dst.index(x, y);
                for c in 0..4 {
                    let top = self.rgba[i00 + c] as f64 * (1.0 - wx)
                        + self.rgba[i10 + c] as f64 * wx;
                    let bot = self.rgba[i01 + c] as f64 * (1.0 - wx)
                        + self.rgba[i11 + c] as f64 * wx;
                    let v = top * (1.0 - wy) + bot * wy;
                    dst.rgba[d + c] = (v + 0.5).clamp(0.0, 255.0) as u8;
                }
            }
        }
        dst
    }

    pub fn load(path: impl AsRef<Path>) -> Result<Image, String> {
        let img = image::open(path.as_ref()).map_err(|e| format!("buka gagal: {e}"))?;
        let rgba = img.to_rgba8();
        let (w, h) = (rgba.width() as usize, rgba.height() as usize);
        Ok(Image::from_rgba(w, h, rgba.into_raw()))
    }

    pub fn save_png(&self, path: impl AsRef<Path>) -> Result<(), String> {
        let buf: image::RgbaImage =
            image::ImageBuffer::from_raw(self.width as u32, self.height as u32, self.rgba.clone())
                .ok_or("buffer tidak valid")?;
        buf.save(path.as_ref()).map_err(|e| format!("simpan gagal: {e}"))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn alpha_roundtrip() {
        let mut img = Image::new(3, 2);
        img.fill_alpha(255);
        img.set_pixel(1, 1, [10, 20, 30, 40]);
        let mask = img.extract_alpha_mask();
        assert_eq!(mask.len(), 6);
        assert_eq!(mask[4], 40);
        let mut back = Image::new(3, 2);
        back.apply_alpha_mask(&mask);
        assert_eq!(back.alpha(1, 1), 40);
        assert_eq!(back.alpha(2, 1), 255);
    }

    #[test]
    fn resize_identity_and_half() {
        let mut img = Image::new(4, 4);
        img.set_pixel(0, 0, [255, 0, 0, 255]);
        let same = img.resize(4, 4);
        assert_eq!(same.pixel(0, 0), [255, 0, 0, 255]);

        let half = img.resize(2, 2);
        assert_eq!(half.width, 2);
        // Piksel pojok kiri-atas kini campuran (bilinear), bukan merah penuh.
        let px = half.pixel(0, 0);
        assert!(px[0] > 0 && px[0] < 255, "harus terinterpolasi: {px:?}");
    }

    #[test]
    fn out_of_bounds_is_transparent() {
        let img = Image::new(2, 2);
        assert_eq!(img.alpha(5, 5), 0);
    }

    #[test]
    fn png_roundtrip_preserves_pixels() {
        let mut img = Image::new(4, 3);
        img.set_pixel(1, 2, [12, 34, 56, 78]);
        let dir = std::env::temp_dir().join("pxa-core-tests");
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("roundtrip.png");
        img.save_png(&path).unwrap();
        let back = Image::load(&path).unwrap();
        assert_eq!((back.width, back.height), (4, 3));
        assert_eq!(back.pixel(1, 2), [12, 34, 56, 78]);
        assert_eq!(back.pixel(0, 0), [0, 0, 0, 0]);
        let _ = std::fs::remove_file(&path);
    }
}