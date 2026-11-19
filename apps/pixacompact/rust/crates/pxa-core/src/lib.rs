//! PixaCompact editor engine, ported from the C# original in `apps/pixacompact`.
//!
//! Format internal: RGBA8 non-premultiplied, 4 byte/piksel (C# memakai BGRA;
//! Rust memakai RGBA karena itu format native `image`/egui — konversi channel
//! hanya terjadi di batas I/O).
//!
//! Semantik tiap fungsi sengaja mengikuti aslinya, termasuk pembulatan
//! (`+ 0.5` lalu clamp) dan truncation integer, supaya hasil port byte-identik
//! dengan C# pada input yang sama.

pub mod brush;
pub mod compose;
pub mod image_buf;
pub mod mask;
pub mod raster;
pub mod document;
pub mod selection;

pub use brush::{interpolate, stamp, stamp_bounds, BrushStamp, PixelBounds};
pub use compose::{compose, compose_on_background, magic_wand, mask_overlay, selection_outline};
pub use image_buf::Image;
pub use raster::{fill_region, CombineOp, Region};
pub use selection::{trace, CombineMode, SelectionState};