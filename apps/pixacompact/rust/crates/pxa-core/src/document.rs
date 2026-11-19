//! Port `Services/Editing/Document/*` — model dokumen & layer.
//!
//! Catatan: C# memakai BGRA; di sini RGBA (R=+0, G=+1, B=+2), jadi indeks
//! channel BlendMath digeser. Perilaku blend & komposit otherwise identik,
//! termasuk mode Hue/Saturation/Color/Luminosity yang di sumber jatuh ke
//! `default` (= Normal) — sengaja dipertahankan apa adanya.

use std::sync::atomic::{AtomicU64, Ordering};

use crate::image_buf::Image;

static NEXT_ID: AtomicU64 = AtomicU64::new(1);

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u8)]
pub enum LayerBlendMode {
    Normal = 0,
    Multiply = 1,
    Screen = 2,
    Overlay = 3,
    SoftLight = 4,
    HardLight = 5,
    ColorDodge = 6,
    ColorBurn = 7,
    Darken = 8,
    Lighten = 9,
    Difference = 10,
    Exclusion = 11,
    Hue = 12,
    Saturation = 13,
    Color = 14,
    Luminosity = 15,
}

impl LayerBlendMode {
    pub fn label(&self) -> &'static str {
        match self {
            Self::Normal => "Normal",
            Self::Multiply => "Multiply",
            Self::Screen => "Screen",
            Self::Overlay => "Overlay",
            Self::SoftLight => "Soft Light",
            Self::HardLight => "Hard Light",
            Self::ColorDodge => "Color Dodge",
            Self::ColorBurn => "Color Burn",
            Self::Darken => "Darken",
            Self::Lighten => "Lighten",
            Self::Difference => "Difference",
            Self::Exclusion => "Exclusion",
            Self::Hue => "Hue",
            Self::Saturation => "Saturation",
            Self::Color => "Color",
            Self::Luminosity => "Luminosity",
        }
    }
    pub const ALL: [LayerBlendMode; 16] = [
        Self::Normal, Self::Multiply, Self::Screen, Self::Overlay, Self::SoftLight,
        Self::HardLight, Self::ColorDodge, Self::ColorBurn, Self::Darken, Self::Lighten,
        Self::Difference, Self::Exclusion, Self::Hue, Self::Saturation, Self::Color, Self::Luminosity,
    ];
}

#[inline]
fn cl(v: f64) -> u8 {
    (v + 0.5).clamp(0.0, 255.0) as u8
}
fn ovl(a: f64, b: f64) -> f64 {
    if a < 128.0 { 2.0 * a * b / 255.0 } else { 255.0 - 2.0 * (255.0 - a) * (255.0 - b) / 255.0 }
}
fn sl(a: f64, b: f64) -> f64 {
    let an = a / 255.0;
    let bn = b / 255.0;
    if bn <= 0.5 {
        return (2.0 * an * bn + an * an * (1.0 - 2.0 * bn)) * 255.0;
    }
    let d = if an <= 0.25 { ((16.0 * an - 12.0) * an + 4.0) * an } else { an.sqrt() };
    (2.0 * an * (1.0 - bn) + d * (2.0 * bn - 1.0)) * 255.0
}
fn dg(a: f64, b: f64) -> f64 {
    if b >= 255.0 { 255.0 } else { (a * 255.0 / (255.0 - b)).min(255.0) }
}
fn bn(a: f64, b: f64) -> f64 {
    if b <= 0.0 { 0.0 } else { (255.0 - (255.0 - a) * 255.0 / b).max(0.0) }
}

/// Port `BlendMath.Blend` (indeks channel disesuaikan ke RGBA).
pub fn blend(
    dst: &mut [u8],
    di: usize,
    src: &[u8],
    si: usize,
    mode: LayerBlendMode,
    opacity: u8,
    mask: Option<&[u8]>,
    mi: usize,
) {
    let mask_a = match mask {
        Some(m) if mi < m.len() => m[mi] as f64 / 255.0,
        _ => 1.0,
    };
    let a = (opacity as f64 / 255.0) * mask_a;
    if a <= 0.0 {
        return;
    }
    let sr = src[si] as f64;
    let sg = src[si + 1] as f64;
    let sb = src[si + 2] as f64;
    let sa = src[si + 3] as f64 * a;
    let dr = dst[di] as f64;
    let dg_ = dst[di + 1] as f64;
    let db = dst[di + 2] as f64;
    let da = dst[di + 3] as f64;

    let (fr, fg, fb) = match mode {
        LayerBlendMode::Multiply => (sr * dr / 255.0, sg * dg_ / 255.0, sb * db / 255.0),
        LayerBlendMode::Screen => (
            255.0 - (255.0 - sr) * (255.0 - dr) / 255.0,
            255.0 - (255.0 - sg) * (255.0 - dg_) / 255.0,
            255.0 - (255.0 - sb) * (255.0 - db) / 255.0,
        ),
        LayerBlendMode::Overlay => (ovl(dr, sr), ovl(dg_, sg), ovl(db, sb)),
        LayerBlendMode::SoftLight => (sl(dr, sr), sl(dg_, sg), sl(db, sb)),
        LayerBlendMode::HardLight => (ovl(sr, dr), ovl(sg, dg_), ovl(sb, db)),
        LayerBlendMode::ColorDodge => (dg(dr, sr), dg(dg_, sg), dg(db, sb)),
        LayerBlendMode::ColorBurn => (bn(dr, sr), bn(dg_, sg), bn(db, sb)),
        LayerBlendMode::Darken => (dr.min(sr), dg_.min(sg), db.min(sb)),
        LayerBlendMode::Lighten => (dr.max(sr), dg_.max(sg), db.max(sb)),
        LayerBlendMode::Difference => ((dr - sr).abs(), (dg_ - sg).abs(), (db - sb).abs()),
        LayerBlendMode::Exclusion => (
            dr + sr - 2.0 * dr * sr / 255.0,
            dg_ + sg - 2.0 * dg_ * sg / 255.0,
            db + sb - 2.0 * db * sb / 255.0,
        ),
        // Hue/Saturation/Color/Luminosity + Normal → sumber apa adanya (default C#).
        _ => (sr, sg, sb),
    };

    let as2 = sa / 255.0;
    let ad = da / 255.0;
    let ao = as2 + ad * (1.0 - as2);
    if ao <= 0.0 {
        return;
    }
    dst[di] = cl((fr * as2 + dr * ad * (1.0 - as2)) / ao);
    dst[di + 1] = cl((fg * as2 + dg_ * ad * (1.0 - as2)) / ao);
    dst[di + 2] = cl((fb * as2 + db * ad * (1.0 - as2)) / ao);
    dst[di + 3] = cl(ao * 255.0);
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct LayerTransform {
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
    pub rotation: f64,
    pub flip_x: bool,
    pub flip_y: bool,
}

impl LayerTransform {
    pub fn default_for(w: usize, h: usize) -> Self {
        Self { x: 0.0, y: 0.0, width: w as f64, height: h as f64, rotation: 0.0, flip_x: false, flip_y: false }
    }
}

impl Default for LayerTransform {
    fn default() -> Self {
        Self { x: 0.0, y: 0.0, width: 0.0, height: 0.0, rotation: 0.0, flip_x: false, flip_y: false }
    }
}

#[derive(Clone)]
pub struct ImageLayer {
    pub id: u64,
    pub name: String,
    pub visible: bool,
    pub opacity: u8,
    pub blend_mode: LayerBlendMode,
    pub transform: LayerTransform,
    pub pixels: Option<Image>,
    pub mask: Option<Vec<u8>>,
    pub is_background: bool,
}

impl ImageLayer {
    pub fn new(name: impl Into<String>, pixels: Image) -> Self {
        let transform = LayerTransform::default_for(pixels.width, pixels.height);
        Self {
            id: NEXT_ID.fetch_add(1, Ordering::Relaxed),
            name: name.into(),
            visible: true,
            opacity: 255,
            blend_mode: LayerBlendMode::Normal,
            transform,
            pixels: Some(pixels),
            mask: None,
            is_background: false,
        }
    }

    /// Thumbnail (tanpa cache global; UI memegang texture sendiri).
    pub fn thumbnail(&self, max_size: usize) -> Option<Image> {
        let px = self.pixels.as_ref()?;
        let scale = (max_size as f64 / px.width as f64).min(max_size as f64 / px.height as f64);
        if scale >= 1.0 {
            return Some(px.clone());
        }
        let tw = ((px.width as f64 * scale) as usize).max(1);
        let th = ((px.height as f64 * scale) as usize).max(1);
        Some(px.resize(tw, th))
    }
}

#[derive(Clone)]
pub struct CanvasDocument {
    pub width: usize,
    pub height: usize,
    pub layers: Vec<ImageLayer>,
    pub active_layer_id: u64,
}

impl CanvasDocument {
    pub fn from_single(pixels: Image, name: &str) -> Self {
        let layer = ImageLayer::new(name, pixels);
        let id = layer.id;
        Self { width: layer.pixels.as_ref().unwrap().width, height: layer.pixels.as_ref().unwrap().height, layers: vec![layer], active_layer_id: id }
    }

    pub fn active_layer(&self) -> Option<&ImageLayer> {
        self.layers.iter().find(|l| l.id == self.active_layer_id)
    }

    pub fn replace_layer(&self, updated: ImageLayer) -> Self {
        let layers = self.layers.iter().map(|l| if l.id == updated.id { updated.clone() } else { l.clone() }).collect();
        Self { layers, ..self.clone() }
    }

    pub fn add_layer(&self, layer: ImageLayer, index: i64) -> Self {
        let mut layers = self.layers.clone();
        if index < 0 || index as usize >= layers.len() {
            layers.push(layer);
        } else {
            layers.insert(index as usize, layer);
        }
        Self { layers, ..self.clone() }
    }

    pub fn remove_layer(&self, id: u64) -> Self {
        let layers = self.layers.iter().filter(|l| l.id != id).cloned().collect();
        Self { layers, ..self.clone() }
    }

    /// Duplikat layer aktif; salinan di atas, nama "<nama> copy".
    pub fn duplicate_active(&self) -> Option<Self> {
        let pos = self.layers.iter().position(|l| l.id == self.active_layer_id)?;
        let mut layers = self.layers.clone();
        let mut copy = layers[pos].clone();
        let new_id = NEXT_ID.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        copy.id = new_id;
        copy.name = format!("{} copy", copy.name);
        layers.insert(pos + 1, copy);
        Some(Self { layers, active_layer_id: new_id, ..self.clone() })
    }

    /// Merge Down: composite penuh → layer bawah, layer aktif dihapus. No-op
    /// kalau teratas atau satu layer. (C# `DocEditorSession.MergeDown`.)
    pub fn merge_down(&self) -> Option<Self> {
        let pos = self.layers.iter().position(|l| l.id == self.active_layer_id)?;
        if pos == 0 {
            return None;
        }
        let comp = composite(self);
        let mut layers = self.layers.clone();
        let merged = ImageLayer::new(
            self.layers[pos - 1].name.clone(),
            Image {
                width: self.width,
                height: self.height,
                rgba: comp.rgba.clone(),
            },
        );
        let new_active = merged.id;
        layers[pos - 1] = merged;
        layers.remove(pos);
        Some(Self { layers, active_layer_id: new_active, ..self.clone() })
    }
}

/// Port `LayerCompositor.Composite` → Image RGBA.
pub fn composite(doc: &CanvasDocument) -> Image {
    let mut dst = Image::new(doc.width, doc.height);
    for layer in &doc.layers {
        if !layer.visible {
            continue;
        }
        let Some(src) = layer.pixels.as_ref() else { continue };
        compose_layer(&mut dst, layer, src);
    }
    dst
}

fn compose_layer(dst: &mut Image, layer: &ImageLayer, src: &Image) {
    let off_x = layer.transform.x as i64;
    let off_y = layer.transform.y as i64;
    let (cw, ch) = (dst.width, dst.height);
    for y in 0..ch {
        let sy = y as i64 - off_y;
        if sy < 0 || sy >= src.height as i64 {
            continue;
        }
        for x in 0..cw {
            let sx = x as i64 - off_x;
            if sx < 0 || sx >= src.width as i64 {
                continue;
            }
            let di = dst.index(x, y);
            let si = src.index(sx as usize, sy as usize);
            let mi = sy as usize * src.width + sx as usize;
            blend(&mut dst.rgba, di, &src.rgba, si, layer.blend_mode, layer.opacity, layer.mask.as_deref(), mi);
        }
    }
}

// ========================
// HISTORY
// ========================

#[derive(Clone)]
pub struct HistoryEntry {
    pub document: CanvasDocument,
    pub label: String,
}

pub struct DocumentHistory {
    max_steps: usize,
    undo: Vec<HistoryEntry>,
    redo: Vec<HistoryEntry>,
}

impl DocumentHistory {
    pub fn new(max_steps: usize) -> Self {
        Self { max_steps, undo: Vec::new(), redo: Vec::new() }
    }
    pub fn can_undo(&self) -> bool {
        !self.undo.is_empty()
    }
    pub fn can_redo(&self) -> bool {
        !self.redo.is_empty()
    }
    pub fn undo_stack(&self) -> &[HistoryEntry] {
        &self.undo
    }
    pub fn push(&mut self, doc: &CanvasDocument, label: impl Into<String>) {
        self.undo.push(HistoryEntry { document: doc.clone(), label: label.into() });
        while self.undo.len() > self.max_steps {
            self.undo.remove(0);
        }
        self.redo.clear();
    }
    pub fn undo(&mut self, current: &CanvasDocument) -> Option<CanvasDocument> {
        let e = self.undo.pop()?;
        self.redo.push(HistoryEntry { document: current.clone(), label: e.label.clone() });
        Some(e.document)
    }
    pub fn redo(&mut self, current: &CanvasDocument) -> Option<CanvasDocument> {
        let e = self.redo.pop()?;
        self.undo.push(HistoryEntry { document: current.clone(), label: e.label.clone() });
        Some(e.document)
    }
    pub fn clear(&mut self) {
        self.undo.clear();
        self.redo.clear();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn solid(w: usize, h: usize, rgba: [u8; 4]) -> Image {
        let mut img = Image::new(w, h);
        for i in 0..w * h {
            img.rgba[i * 4..i * 4 + 4].copy_from_slice(&rgba);
        }
        img
    }

    #[test]
    fn normal_blend_over_transparent_yields_source() {
        let mut dst = solid(1, 1, [0, 0, 0, 0]);
        let src = solid(1, 1, [200, 100, 50, 255]);
        blend(&mut dst.rgba, 0, &src.rgba, 0, LayerBlendMode::Normal, 255, None, 0);
        assert_eq!(&dst.rgba[0..4], &[200, 100, 50, 255]);
    }

    #[test]
    fn multiply_darkens() {
        let mut dst = solid(1, 1, [200, 200, 200, 255]);
        let src = solid(1, 1, [128, 128, 128, 255]);
        blend(&mut dst.rgba, 0, &src.rgba, 0, LayerBlendMode::Multiply, 255, None, 0);
        // 200*128/255 ≈ 100
        assert!(dst.rgba[0] < 120 && dst.rgba[0] > 80, "multiply: {}", dst.rgba[0]);
    }

    #[test]
    fn opacity_zero_is_noop() {
        let mut dst = solid(1, 1, [10, 20, 30, 255]);
        let src = solid(1, 1, [255, 255, 255, 255]);
        blend(&mut dst.rgba, 0, &src.rgba, 0, LayerBlendMode::Normal, 0, None, 0);
        assert_eq!(&dst.rgba[0..4], &[10, 20, 30, 255]);
    }

    #[test]
    fn mask_scales_coverage() {
        let mut dst = solid(1, 1, [0, 0, 0, 0]);
        let src = solid(1, 1, [255, 0, 0, 255]);
        let mask = vec![128u8];
        blend(&mut dst.rgba, 0, &src.rgba, 0, LayerBlendMode::Normal, 255, Some(&mask), 0);
        // alpha ≈ 128
        assert!(dst.rgba[3] > 100 && dst.rgba[3] < 160, "alpha: {}", dst.rgba[3]);
    }

    #[test]
    fn compositor_respects_offset_and_visibility() {
        let base = solid(4, 4, [0, 0, 0, 0]);
        let mut doc = CanvasDocument::from_single(base, "bg");
        let mut top = ImageLayer::new("top", solid(2, 2, [255, 0, 0, 255]));
        top.transform.x = 1.0;
        top.transform.y = 1.0;
        doc = doc.add_layer(top, -1);
        let out = composite(&doc);
        assert_eq!(out.pixel(1, 1), [255, 0, 0, 255]);
        assert_eq!(out.pixel(0, 0), [0, 0, 0, 0]);
        assert_eq!(out.pixel(3, 3), [0, 0, 0, 0]);
    }

    #[test]
    fn history_undo_redo_roundtrip() {
        let a = CanvasDocument::from_single(solid(2, 2, [1, 1, 1, 255]), "a");
        let b = CanvasDocument::from_single(solid(2, 2, [2, 2, 2, 255]), "b");
        let mut h = DocumentHistory::new(10);
        h.push(&a, "step a");
        let restored = h.undo(&b).expect("undo");
        assert_eq!(restored.layers[0].pixels.as_ref().unwrap().pixel(0, 0)[0], 1);
        assert!(h.can_redo());
        let redone = h.redo(&restored).expect("redo");
        assert_eq!(redone.layers[0].pixels.as_ref().unwrap().pixel(0, 0)[0], 2);
    }

    #[test]
    fn add_remove_replace_layer() {
        let doc = CanvasDocument::from_single(solid(2, 2, [0, 0, 0, 255]), "base");
        let extra = ImageLayer::new("extra", solid(2, 2, [9, 9, 9, 255]));
        let id = extra.id;
        let doc = doc.add_layer(extra, -1);
        assert_eq!(doc.layers.len(), 2);
        let doc = doc.remove_layer(id);
        assert_eq!(doc.layers.len(), 1);
    }

    #[test]
    fn duplicate_active_makes_copy_above() {
        let doc = CanvasDocument::from_single(solid(2, 2, [5, 5, 5, 255]), "base");
        let doc = doc.duplicate_active().expect("dup");
        assert_eq!(doc.layers.len(), 2);
        assert!(doc.layers.iter().any(|l| l.name == "base copy"));
        // layer aktif baru = salinan (di atas).
        let active = &doc.layers.iter().find(|l| l.id == doc.active_layer_id).unwrap();
        assert_eq!(active.name, "base copy");
    }

    #[test]
    fn merge_down_folds_active_into_below() {
        // base (bawah) + top (atas, aktif).
        let mut doc = CanvasDocument::from_single(solid(2, 2, [10, 0, 0, 255]), "base");
        let top = ImageLayer::new("top", solid(2, 2, [0, 10, 0, 255]));
        let top_id = top.id;
        doc = doc.add_layer(top, -1);
        doc.active_layer_id = top_id;
        assert_eq!(doc.layers.len(), 2);
        let merged = doc.merge_down().expect("merge");
        assert_eq!(merged.layers.len(), 1);
        assert_eq!(merged.layers[0].name, "base");
        // composite penuh: top (hijau, opacity 255) di atas base (merah) → hijau di atas.
        let c = composite(&merged);
        assert_eq!(c.pixel(0, 0)[1], 10); // hijau dari top
    }
}