//! Geometri ring radial — aturan pintah / split rule (abeng 27 Nov 20:03,
//! ref `~/workspace/radial-refs/8-layout.jpg`).
//!
//! Ring dibagi N segmen SAMA RATA sesuai jumlah item radial yang aktif
//! (2–12). Semua sudut, posisi tombol, busur wedge, dan garis pembatas
//! dihitung dari N di sini — bukan hardcode untuk 8 saja.
//!
//! Konvensi (sama seperti `ui/radial.slint` C#):
//! - Sudut diukur dari Utara (0°) searah jarum jam.
//! - Sektor `i` berpusat di `i * 360/N`, batasnya di `±180/N` dari pusat.
//! - Kanvas 320×320, pusat (160, 160); pelat (viewbox 200) berpusat
//!   (100, 100), R_luar = 100, R_dalam = 44; tombol 36×36 di R = 72.
//! - `wedge_path` memakai koordinat lokal pelat (pusat 100,100);
//!   `button_top_left` memakai koordinat kanvas (pusat 160,160).

/// Sudut satu segmen (derajat) untuk `n` segmen sama rata.
pub fn sector_angle_deg(n: usize) -> f64 {
    360.0 / n as f64
}

/// Nomor sektor (0..n-1) untuk sudut pointer `deg` (0° = Utara,
/// searah jarum jam, `deg` dalam [0, 360)).
pub fn sector_index(deg: f64, n: usize) -> usize {
    let step = sector_angle_deg(n);
    let mut shifted = deg + step / 2.0;
    if shifted >= 360.0 {
        shifted -= 360.0;
    }
    ((shifted / step).floor() as usize).min(n - 1)
}

/// Titik pada lingkaran: `deg` diukur dari Utara, searah jarum jam.
pub fn point_on_circle(cx: f64, cy: f64, r: f64, deg_from_north: f64) -> (f64, f64) {
    let a = deg_from_north.to_radians();
    (cx + r * a.sin(), cy - r * a.cos())
}

/// Posisi kiri-atas tombol 36×36 untuk sektor `i` dari `n`
/// (koordinat kanvas 320×320, pusat 160,160, R = 72).
pub fn button_top_left(i: usize, n: usize) -> (f64, f64) {
    let step = sector_angle_deg(n);
    let (x, y) = point_on_circle(160.0, 160.0, 72.0, i as f64 * step);
    (x - 18.0, y - 18.0)
}

fn fmt(v: f64) -> String {
    // Samakan format dengan perintah Path yang sudah ada ("61.73").
    // Hindari "-0.00" (sin 180° ≈ 1.2e-16 → -0.00 setelah pembulatan).
    let r = (v * 100.0).round() / 100.0;
    if r == 0.0 {
        "0.00".to_string()
    } else {
        format!("{r:.2}")
    }
}

/// Perintah SVG busur anular (wedge) sektor `i` dari `n`
/// (koordinat lokal pelat 200×200, pusat 100,100, R 44..100).
/// Sama persis dengan 8 wedge hardcode di `ui/radial.slint` untuk n=8.
pub fn wedge_path(i: usize, n: usize) -> String {
    let step = sector_angle_deg(n);
    let a0 = i as f64 * step - step / 2.0;
    let a1 = i as f64 * step + step / 2.0;
    let (ox0, oy0) = point_on_circle(100.0, 100.0, 100.0, a0);
    let (ox1, oy1) = point_on_circle(100.0, 100.0, 100.0, a1);
    let (ix1, iy1) = point_on_circle(100.0, 100.0, 44.0, a1);
    let (ix0, iy0) = point_on_circle(100.0, 100.0, 44.0, a0);
    let large = if step > 180.0 { 1 } else { 0 };
    format!(
        "M {} {} A 100 100 0 {} 1 {} {} L {} {} A 44 44 0 {} 0 {} {} Z",
        fmt(ox0),
        fmt(oy0),
        large,
        fmt(ox1),
        fmt(oy1),
        fmt(ix1),
        fmt(iy1),
        large,
        fmt(ix0),
        fmt(iy0)
    )
}

/// Busur tepi luar saja sektor `i` dari `n` (garis glow aksen di tepi
/// luar segmen aktif, ala ref 3a/2 — dipakai gaya Kaca/HUD).
pub fn wedge_arc_path(i: usize, n: usize) -> String {
    let step = sector_angle_deg(n);
    let a0 = i as f64 * step - step / 2.0;
    let a1 = i as f64 * step + step / 2.0;
    let (ox0, oy0) = point_on_circle(100.0, 100.0, 100.0, a0);
    let (ox1, oy1) = point_on_circle(100.0, 100.0, 100.0, a1);
    let large = if step > 180.0 { 1 } else { 0 };
    format!(
        "M {} {} A 100 100 0 {} 1 {} {}",
        fmt(ox0),
        fmt(oy0),
        large,
        fmt(ox1),
        fmt(oy1)
    )
}

/// Perintah SVG garis pembatas antar-sektor (dari R_dalam ke R_luar
/// di tiap batas sektor; koordinat lokal pelat).
pub fn divider_commands(n: usize) -> String {
    let step = sector_angle_deg(n);
    (0..n)
        .map(|i| {
            let b = (i as f64 + 0.5) * step;
            let (ix, iy) = point_on_circle(100.0, 100.0, 44.0, b);
            let (ox, oy) = point_on_circle(100.0, 100.0, 100.0, b);
            format!("M {} {} L {} {}", fmt(ix), fmt(iy), fmt(ox), fmt(oy))
        })
        .collect::<Vec<_>>()
        .join(" ")
}

/// Semua geometri ring untuk `n` segmen, siap didorong ke Slint.
pub struct RadialGeometry {
    pub wedge_cmds: Vec<String>,
    pub arc_cmds: Vec<String>,
    pub divider_cmds: String,
    pub btn_x: Vec<f64>,
    pub btn_y: Vec<f64>,
}

pub fn radial_geometry(n: usize) -> RadialGeometry {
    let wedge_cmds = (0..n).map(|i| wedge_path(i, n)).collect();
    let arc_cmds = (0..n).map(|i| wedge_arc_path(i, n)).collect();
    let divider_cmds = divider_commands(n);
    // Posisi tombol dibulatkan ke piksel utuh — konvensi btn-x/btn-y
    // hardcode lama (72·sin45° = 50,91 dibulatkan jadi 51 → 193px).
    let (btn_x, btn_y): (Vec<f64>, Vec<f64>) = (0..n)
        .map(|i| {
            let (x, y) = button_top_left(i, n);
            (x.round(), y.round())
        })
        .unzip();
    RadialGeometry {
        wedge_cmds,
        arc_cmds,
        divider_cmds,
        btn_x,
        btn_y,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const NS: [usize; 9] = [2, 3, 4, 5, 6, 8, 9, 10, 12];

    #[test]
    fn sector_angles_sum_to_360_for_all_supported_counts() {
        for &n in &NS {
            let step = sector_angle_deg(n);
            assert!((step * n as f64 - 360.0).abs() < 1e-9, "n={n}");
        }
    }

    #[test]
    fn sector_index_covers_full_circle_without_gaps() {
        for &n in &NS {
            // Titik tengah tiap sektor harus memetakan ke sektor itu.
            let step = sector_angle_deg(n);
            for i in 0..n {
                assert_eq!(sector_index(i as f64 * step, n), i, "n={n} i={i}");
            }
            // Batas antar-sektor: tepat di batas, masuk sektor berikutnya.
            for i in 0..n {
                let boundary = (i as f64 + 0.5) * step;
                let expected = (i + 1) % n;
                assert_eq!(sector_index(boundary, n), expected, "n={n} i={i}");
            }
            assert_eq!(sector_index(0.0, n), 0);
            assert_eq!(sector_index(359.9, n), 0);
        }
    }

    #[test]
    fn buttons_sit_on_the_ring_circle() {
        for &n in &NS {
            for i in 0..n {
                let (x, y) = button_top_left(i, n);
                // Pusat tombol = top-left + 18.
                let dx = (x + 18.0) - 160.0;
                let dy = (y + 18.0) - 160.0;
                let dist = (dx * dx + dy * dy).sqrt();
                assert!((dist - 72.0).abs() < 0.01, "n={n} i={i} dist={dist}");
            }
        }
    }

    #[test]
    fn n8_matches_the_hardcoded_layout_pixel_for_pixel() {
        // Regresi: geometri n=8 harus sama persis dengan btn-x/btn-y dan
        // wedge Path hardcode di ui/radial.slint (sebelum refactor).
        // Tombol dicek lewat radial_geometry (membulatkan ke piksel
        // utuh seperti hardcode lama); button_top_left mentah dicek
        // presisinya di buttons_sit_on_the_ring_circle.
        let expected_btns = [
            (142.0, 70.0),
            (193.0, 91.0),
            (214.0, 142.0),
            (193.0, 193.0),
            (142.0, 214.0),
            (91.0, 193.0),
            (70.0, 142.0),
            (91.0, 91.0),
        ];
        let g = radial_geometry(8);
        for (i, &(ex, ey)) in expected_btns.iter().enumerate() {
            assert!(
                (g.btn_x[i] - ex).abs() < 0.01 && (g.btn_y[i] - ey).abs() < 0.01,
                "btn {i}"
            );
        }
        assert_eq!(
            wedge_path(0, 8),
            "M 61.73 7.61 A 100 100 0 0 1 138.27 7.61 L 116.84 59.35 A 44 44 0 0 0 83.16 59.35 Z"
        );
        assert_eq!(
            divider_commands(8),
            "M 116.84 59.35 L 138.27 7.61 M 140.65 83.16 L 192.39 61.73 M 140.65 116.84 L 192.39 138.27 M 116.84 140.65 L 138.27 192.39 M 83.16 140.65 L 61.73 192.39 M 59.35 116.84 L 7.61 138.27 M 59.35 83.16 L 7.61 61.73 M 83.16 59.35 L 61.73 7.61"
        );
    }

    #[test]
    fn wedge_and_divider_counts_follow_n() {
        for &n in &NS {
            let g = radial_geometry(n);
            assert_eq!(g.wedge_cmds.len(), n, "n={n}");
            assert_eq!(g.arc_cmds.len(), n, "n={n}");
            assert_eq!(g.btn_x.len(), n, "n={n}");
            assert_eq!(g.btn_y.len(), n, "n={n}");
            // Satu garis "M..L.." per batas sektor.
            assert_eq!(g.divider_cmds.matches('M').count(), n, "n={n}");
            // Tiap wedge valid: diawali M, berisi 2 busur, diakhiri Z.
            for cmd in &g.wedge_cmds {
                assert!(cmd.starts_with("M "), "n={n}: {cmd}");
                assert!(cmd.ends_with(" Z"), "n={n}: {cmd}");
                assert_eq!(cmd.matches('A').count(), 2, "n={n}: {cmd}");
            }
        }
    }

    #[test]
    fn n4_splits_into_quadrants() {
        // N=4: tombol di Utara/Timur/Selatan/Barat, sudut 90°.
        assert_eq!(sector_angle_deg(4), 90.0);
        let g = radial_geometry(4);
        let (x0, y0) = (g.btn_x[0], g.btn_y[0]);
        let (x1, y1) = (g.btn_x[1], g.btn_y[1]);
        let (x2, y2) = (g.btn_x[2], g.btn_y[2]);
        let (x3, y3) = (g.btn_x[3], g.btn_y[3]);
        assert!((x0 - 142.0).abs() < 0.01 && (y0 - 70.0).abs() < 0.01);
        assert!((x1 - 214.0).abs() < 0.01 && (y1 - 142.0).abs() < 0.01);
        assert!((x2 - 142.0).abs() < 0.01 && (y2 - 214.0).abs() < 0.01);
        assert!((x3 - 70.0).abs() < 0.01 && (y3 - 142.0).abs() < 0.01);
        assert_eq!(sector_index(45.0, 4), 1);
        assert_eq!(sector_index(135.0, 4), 2);
        assert_eq!(sector_index(225.0, 4), 3);
        assert_eq!(sector_index(315.0, 4), 0);
    }
}
