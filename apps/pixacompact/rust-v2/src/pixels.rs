use std::path::Path;

use eframe::egui;
use image::{ImageError, ImageFormat, RgbaImage};

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum PixelBufferError {
    InvalidDimensions,
    InvalidLength { expected: usize, actual: usize },
}

/// Straight-alpha RGBA pixel data. It is intentionally not premultiplied in memory.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PixelBuffer {
    width: usize,
    height: usize,
    rgba: Vec<u8>,
}

impl PixelBuffer {
    pub fn from_rgba(width: usize, height: usize, rgba: Vec<u8>) -> Result<Self, PixelBufferError> {
        let expected = width
            .checked_mul(height)
            .and_then(|pixels| pixels.checked_mul(4))
            .filter(|_| width > 0 && height > 0 && width <= u32::MAX as usize && height <= u32::MAX as usize)
            .ok_or(PixelBufferError::InvalidDimensions)?;
        if rgba.len() != expected {
            return Err(PixelBufferError::InvalidLength {
                expected,
                actual: rgba.len(),
            });
        }
        Ok(Self { width, height, rgba })
    }

    pub fn open(path: impl AsRef<Path>) -> Result<Self, ImageError> {
        let image = image::open(path)?.into_rgba8();
        let (width, height) = image.dimensions();
        // Decoded image dimensions are non-zero and their buffer length is validated by image.
        Ok(Self {
            width: width as usize,
            height: height as usize,
            rgba: image.into_raw(),
        })
    }

    pub fn width(&self) -> usize {
        self.width
    }

    pub fn height(&self) -> usize {
        self.height
    }

    pub fn rgba(&self) -> &[u8] {
        &self.rgba
    }

    pub fn to_color_image(&self) -> egui::ColorImage {
        egui::ColorImage::from_rgba_unmultiplied([self.width, self.height], &self.rgba)
    }

    /// Overwrites alpha for available mask pixels and leaves RGB unchanged.
    pub fn apply_alpha_mask(&mut self, mask: &[u8]) {
        let pixels = self.width * self.height;
        for (index, alpha) in mask.iter().copied().take(pixels).enumerate() {
            self.rgba[index * 4 + 3] = alpha;
        }
    }

    pub fn fill_alpha(&mut self, alpha: u8) {
        for pixel in self.rgba.chunks_exact_mut(4) {
            pixel[3] = alpha;
        }
    }

    /// Flatten against an opaque background. A mask replaces source alpha when present.
    pub fn composite_on_background(
        &self,
        mask: Option<&[u8]>,
        apply_mask: bool,
        background: [u8; 3],
    ) -> Self {
        let mut output = Vec::with_capacity(self.rgba.len());
        for (index, pixel) in self.rgba.chunks_exact(4).enumerate() {
            let alpha = if apply_mask {
                mask.and_then(|values| values.get(index)).copied().unwrap_or(pixel[3])
            } else {
                pixel[3]
            };
            for channel in 0..3 {
                let fg = pixel[channel] as u32;
                let bg = background[channel] as u32;
                let a = alpha as u32;
                let blended = (fg * a + bg * (255 - a) + 127) / 255;
                output.push(blended as u8);
            }
            output.push(255);
        }
        Self {
            width: self.width,
            height: self.height,
            rgba: output,
        }
    }

    pub fn render_mask_overlay(
        width: usize,
        height: usize,
        mask: &[u8],
        color: [u8; 4],
        max_alpha: u8,
        invert: bool,
    ) -> Result<Self, PixelBufferError> {
        let count = width
            .checked_mul(height)
            .filter(|_| width > 0 && height > 0 && width <= u32::MAX as usize && height <= u32::MAX as usize)
            .ok_or(PixelBufferError::InvalidDimensions)?;
        let mut rgba = vec![0; count.checked_mul(4).ok_or(PixelBufferError::InvalidDimensions)?];
        for index in 0..count.min(mask.len()) {
            let coverage = if invert { 255 - mask[index] } else { mask[index] };
            if coverage == 0 {
                continue;
            }
            let alpha = (coverage as f64 / 255.0 * color[3] as f64 / 255.0
                * max_alpha as f64)
                .round() as u8;
            let offset = index * 4;
            rgba[offset..offset + 3].copy_from_slice(&color[..3]);
            rgba[offset + 3] = alpha;
        }
        Self::from_rgba(width, height, rgba)
    }

    /// Bilinear resize using pixel-center sampling and half-up channel rounding.
    pub fn resize_bilinear(&self, new_width: usize, new_height: usize) -> Result<Self, PixelBufferError> {
        let output_len = new_width
            .checked_mul(new_height)
            .and_then(|pixels| pixels.checked_mul(4))
            .filter(|_| new_width > 0 && new_height > 0 && new_width <= u32::MAX as usize && new_height <= u32::MAX as usize)
            .ok_or(PixelBufferError::InvalidDimensions)?;
        if new_width == self.width && new_height == self.height {
            return Self::from_rgba(new_width, new_height, self.rgba.clone());
        }

        let mut output = vec![0; output_len];
        let scale_x = self.width as f64 / new_width as f64;
        let scale_y = self.height as f64 / new_height as f64;
        for y in 0..new_height {
            let fy = (y as f64 + 0.5) * scale_y - 0.5;
            let mut y0 = fy.floor() as isize;
            let mut wy = fy - y0 as f64;
            if y0 < 0 {
                y0 = 0;
                wy = 0.0;
            }
            let y0 = (y0 as usize).min(self.height - 1);
            let y1 = (y0 + 1).min(self.height - 1);
            for x in 0..new_width {
                let fx = (x as f64 + 0.5) * scale_x - 0.5;
                let mut x0 = fx.floor() as isize;
                let mut wx = fx - x0 as f64;
                if x0 < 0 {
                    x0 = 0;
                    wx = 0.0;
                }
                let x0 = (x0 as usize).min(self.width - 1);
                let x1 = (x0 + 1).min(self.width - 1);
                let samples = [
                    (y0 * self.width + x0) * 4,
                    (y0 * self.width + x1) * 4,
                    (y1 * self.width + x0) * 4,
                    (y1 * self.width + x1) * 4,
                ];
                let destination = (y * new_width + x) * 4;
                for channel in 0..4 {
                    let top = self.rgba[samples[0] + channel] as f64 * (1.0 - wx)
                        + self.rgba[samples[1] + channel] as f64 * wx;
                    let bottom = self.rgba[samples[2] + channel] as f64 * (1.0 - wx)
                        + self.rgba[samples[3] + channel] as f64 * wx;
                    let value = top * (1.0 - wy) + bottom * wy;
                    output[destination + channel] = value.round().clamp(0.0, 255.0) as u8;
                }
            }
        }
        Self::from_rgba(new_width, new_height, output)
    }

    pub fn save_png(&self, path: impl AsRef<Path>) -> Result<(), ImageError> {
        let image = RgbaImage::from_raw(self.width as u32, self.height as u32, self.rgba.clone())
            .expect("PixelBuffer dimensions and storage are validated");
        image.save_with_format(path, ImageFormat::Png)
    }
}

#[cfg(test)]
mod tests {
    use super::{PixelBuffer, PixelBufferError};

    #[test]
    fn mask_replaces_alpha_without_changing_rgb() {
        let mut pixels = PixelBuffer::from_rgba(2, 1, vec![10, 20, 30, 40, 50, 60, 70, 80]).unwrap();
        pixels.apply_alpha_mask(&[0, 255]);
        assert_eq!(pixels.rgba(), &[10, 20, 30, 0, 50, 60, 70, 255]);
    }

    #[test]
    fn background_composite_uses_mask_and_returns_opaque_rgba() {
        let pixels = PixelBuffer::from_rgba(1, 1, vec![200, 100, 0, 255]).unwrap();
        let output = pixels.composite_on_background(Some(&[128]), true, [0, 0, 255]);
        assert_eq!(output.rgba(), &[100, 50, 127, 255]);
    }

    #[test]
    fn overlay_respects_coverage_and_maximum_alpha() {
        let overlay = PixelBuffer::render_mask_overlay(2, 1, &[255, 0], [255, 0, 0, 255], 128, false).unwrap();
        assert_eq!(overlay.rgba(), &[255, 0, 0, 128, 0, 0, 0, 0]);
    }

    #[test]
    fn resize_to_same_size_is_byte_identical() {
        let pixels = PixelBuffer::from_rgba(1, 1, vec![13, 27, 44, 201]).unwrap();
        assert_eq!(pixels.resize_bilinear(1, 1).unwrap(), pixels);
    }

    #[test]
    fn rejects_invalid_buffer_shape() {
        assert_eq!(
            PixelBuffer::from_rgba(2, 2, vec![0; 8]),
            Err(PixelBufferError::InvalidLength { expected: 16, actual: 8 })
        );
    }
}
