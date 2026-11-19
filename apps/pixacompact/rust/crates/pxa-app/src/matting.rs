//! Engine matting ONNX lokal — port `Services/Ai/MattingOnnxService.cs`
//! (preprocess + inferensi MODNet/BiRefNet).
//!
//! ONNX Runtime di-dlopen via fitur `load-dynamic` ort, jadi tidak butuh
//! linker MSVC. Path DLL: `ORT_DYLIB_PATH` atau default `onnxruntime.dll`
//! (DLL yang sudah ada di folder app C#, `bin/Release/net8.0/win-x64/`).

use pxa_core::Image;

#[derive(Clone, Debug)]
pub struct MattingSpec {
    pub model_path: String,
    /// 0 = mode shortest-edge (MODNet: 512, pembagi 32).
    pub square_resize: bool,
    pub input_size: usize,
    pub shortest_edge: usize,
    pub size_divisibility: usize,
    pub mean: [f64; 3],
    pub std: [f64; 3],
}

impl MattingSpec {
    /// Port `OnnxModelManager.ModNet`.
    pub fn modnet(model_path: impl Into<String>) -> Self {
        Self {
            model_path: model_path.into(),
            square_resize: false,
            input_size: 0,
            shortest_edge: 512,
            size_divisibility: 32,
            mean: [0.5, 0.5, 0.5],
            std: [0.5, 0.5, 0.5],
        }
    }

    /// Port `OnnxModelManager.BiRefNet` (kualitas maksimum; butuh model 1024x1024).
    pub fn birefnet(model_path: impl Into<String>) -> Self {
        Self {
            model_path: model_path.into(),
            square_resize: true,
            input_size: 1024,
            shortest_edge: 0,
            size_divisibility: 0,
            mean: [0.485, 0.456, 0.406],
            std: [0.229, 0.224, 0.225],
        }
    }

    fn round_up(v: usize, div: usize) -> usize {
        let d = div.max(1);
        ((v + d - 1) / d) * d
    }

    /// Ukuran masukan model untuk gambar ini (identik dengan C#).
    fn input_size_for(&self, src_w: usize, src_h: usize) -> (usize, usize) {
        if self.square_resize {
            return (self.input_size, self.input_size);
        }
        let se = self.shortest_edge.max(1);
        let scale = if src_w < src_h { se as f64 / src_w as f64 } else { se as f64 / src_h as f64 };
        let mut iw = ((src_w as f64 * scale).round() as usize).max(1);
        let mut ih = ((src_h as f64 * scale).round() as usize).max(1);
        iw = Self::round_up(iw, self.size_divisibility);
        ih = Self::round_up(ih, self.size_divisibility);
        (iw, ih)
    }
}

pub struct MattingSession {
    // Environment harus hidup selama sesi (rc.15 tidak pakai global env).
    _env: ort::environment::Environment,
    session: ort::session::Session,
    input_name: String,
    spec: MattingSpec,
}

impl MattingSession {
    /// Cari ONNX Runtime: env ORT_DYLIB_PATH → onnxruntime.dll di folder exe → default.
    fn build_env() -> Result<ort::environment::Environment, String> {
        let exe_dylib = std::env::current_exe()
            .ok()
            .and_then(|p| p.parent().map(|d| d.join("onnxruntime.dll")))
            .filter(|p| p.exists());
        let mut candidates: Vec<(String, Option<std::path::PathBuf>)> = Vec::new();
        if let Ok(p) = std::env::var("ORT_DYLIB_PATH") {
            candidates.push(("ORT_DYLIB_PATH".into(), Some(p.into())));
        }
        if let Some(p) = exe_dylib {
            candidates.push(("onnxruntime.dll di folder exe".into(), Some(p)));
        }
        let mut errors: Vec<String> = Vec::new();
        for (label, path) in candidates {
            let res = match path {
                Some(p) => ort::init_from(p)
                    .map_err(|e| format!("{label}: {e}"))
                    .and_then(|b| b.build().map_err(|e| format!("{label} env: {e}"))),
                None => ort::init().build().map_err(|e| format!("{label}: {e}")),
            };
            match res {
                Ok(env) => return Ok(env),
                Err(e) => errors.push(e),
            }
        }
        match ort::init().build() {
            Ok(env) => Ok(env),
            Err(e) => {
                errors.push(format!("default: {e}"));
                Err(errors.join(" | "))
            }
        }
    }

    pub fn load(spec: MattingSpec) -> Result<Self, String> {
        let env = Self::build_env()?;
        let session = ort::session::Session::builder(&env)
            .map_err(|e| format!("builder: {e}"))?
            .commit_from_file(&spec.model_path)
            .map_err(|e| format!("buka model {}: {e}", spec.model_path))?;
        let input_name = session
            .inputs()
            .first()
            .map(|i| i.name().to_owned())
            .unwrap_or_else(|| "input".to_string());
        Ok(Self { _env: env, session, input_name, spec })
    }

    /// Jalankan matting → mask alpha 1 byte/piksel ukuran gambar asli.
    pub fn matte(&mut self, img: &Image) -> Result<Vec<u8>, String> {
        let (src_w, src_h) = (img.width, img.height);
        let (iw, ih) = self.spec.input_size_for(src_w, src_h);

        // Preprocess NCHW: bilinear + normalisasi (math identik C#).
        let mut data = vec![0f32; 3 * ih * iw];
        let sx = src_w as f64 / iw as f64;
        let sy = src_h as f64 / ih as f64;
        for y in 0..ih {
            let fy = (y as f64 + 0.5) * sy - 0.5;
            let mut y0 = fy.floor() as i64;
            let mut wy = fy - y0 as f64;
            if y0 < 0 {
                y0 = 0;
                wy = 0.0;
            }
            let mut y0u = y0 as usize;
            let y1 = (y0u + 1).min(src_h - 1);
            if y0u >= src_h {
                y0u = src_h - 1;
            }
            for x in 0..iw {
                let fx = (x as f64 + 0.5) * sx - 0.5;
                let mut x0 = fx.floor() as i64;
                let mut wx = fx - x0 as f64;
                if x0 < 0 {
                    x0 = 0;
                    wx = 0.0;
                }
                let mut x0u = x0 as usize;
                let x1 = (x0u + 1).min(src_w - 1);
                if x0u >= src_w {
                    x0u = src_w - 1;
                }
                let i00 = img.index(x0u, y0u);
                let i10 = img.index(x1, y0u);
                let i01 = img.index(x0u, y1);
                let i11 = img.index(x1, y1);
                for c in 0..3 {
                    // Buffer RGBA: kanal 0=R, 1=G, 2=B.
                    let top = img.rgba[i00 + c] as f64 * (1.0 - wx) + img.rgba[i10 + c] as f64 * wx;
                    let bot = img.rgba[i01 + c] as f64 * (1.0 - wx) + img.rgba[i11 + c] as f64 * wx;
                    let v = (top * (1.0 - wy) + bot * wy) / 255.0;
                    let v = (v - self.spec.mean[c]) / self.spec.std[c];
                    data[(c * ih + y) * iw + x] = v as f32;
                }
            }
        }

        let tensor = ort::value::Tensor::from_array(([1usize, 3, ih, iw], data))
            .map_err(|e| format!("tensor input: {e}"))?;
        let inputs = ort::inputs![self.input_name.clone() => tensor];
        let outputs = self
            .session
            .run(inputs)
            .map_err(|e| format!("inferensi: {e}"))?;
        let first = outputs
            .iter()
            .next()
            .ok_or_else(|| "model tidak mengeluarkan output".to_string())?;
        let (shape, vals) = first
            .1
            .try_extract_tensor::<f32>()
            .map_err(|e| format!("baca output: {e}"))?;
        let dims: Vec<usize> = shape.iter().map(|d| *d as usize).collect();
        let (out_h, out_w) = if dims.len() >= 4 {
            (dims[2], dims[3])
        } else if dims.len() == 3 {
            (dims[1], dims[2])
        } else {
            let side = (vals.len() as f64).sqrt() as usize;
            (side, vals.len() / side.max(1))
        };
        if out_w == 0 || out_h == 0 || out_w * out_h > vals.len() {
            return Err(format!("shape output tak terduga: {dims:?}"));
        }

        // MODNet sigmoid sudah 0..1 — JANGAN min-max (jebakan lama di C#).
        let mut alpha = vec![0f32; out_w * out_h];
        alpha.copy_from_slice(&vals[..out_w * out_h]);
        let max = alpha.iter().cloned().fold(0.0f32, f32::max);
        let min = alpha.iter().cloned().fold(f32::MAX, f32::min);
        let already_unit = min >= -0.01 && max <= 1.01;
        let (min_f, max_f) = (min as f64, max as f64);

        // Resize alpha balik ke ukuran asli (bilinear, math sama dgn Image::resize).
        let mut out = vec![0u8; src_w * src_h];
        let sxs = out_w as f64 / src_w as f64;
        let sys = out_h as f64 / src_h as f64;
        for y in 0..src_h {
            let fy = (y as f64 + 0.5) * sys - 0.5;
            let mut y0 = fy.floor() as i64;
            let mut wy = fy - y0 as f64;
            if y0 < 0 {
                y0 = 0;
                wy = 0.0;
            }
            let mut y0u = y0 as usize;
            let y1 = (y0u + 1).min(out_h - 1);
            if y0u >= out_h {
                y0u = out_h - 1;
            }
            for x in 0..src_w {
                let fx = (x as f64 + 0.5) * sxs - 0.5;
                let mut x0 = fx.floor() as i64;
                let mut wx = fx - x0 as f64;
                if x0 < 0 {
                    x0 = 0;
                    wx = 0.0;
                }
                let mut x0u = x0 as usize;
                let x1 = (x0u + 1).min(out_w - 1);
                if x0u >= out_w {
                    x0u = out_w - 1;
                }
                let at = |xx: usize, yy: usize| alpha[yy * out_w + xx] as f64;
                let top = at(x0u, y0u) * (1.0 - wx) + at(x1, y0u) * wx;
                let bot = at(x0u, y1) * (1.0 - wx) + at(x1, y1) * wx;
                let v = top * (1.0 - wy) + bot * wy;
                let v = if already_unit { v } else { (v - min_f) / (max_f - min_f).max(1e-6) };
                out[y * src_w + x] = (v.clamp(0.0, 1.0) * 255.0 + 0.5) as u8;
            }
        }
        Ok(out)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn input_size_matches_modnet_rule() {
        let spec = MattingSpec::modnet("x.onnx");
        // Shortest edge 512, lalu bulatkan ke kelipatan 32.
        let (w, h) = spec.input_size_for(1000, 500);
        assert_eq!(h, 512);
        assert_eq!(w, 1024);
        let (w2, h2) = spec.input_size_for(640, 480);
        // 640*512/480 = 682.7 → 683 → bulat ke 32 → 704; tinggi 512.
        assert_eq!(w2, 704);
        assert_eq!(h2, 512);
    }
}