#[derive(Debug, Clone, Copy, PartialEq)]
pub struct BrushValues {
    pub size: i32,
    pub hardness: f64,
}

/// Port of PixelcutCompact.Services.Editing.BrushHudMath.Compute.
/// C# Math.Round uses midpoint-to-even, so Rust's round_ties_even is required
/// to preserve behavior at half-pixel brush sizes.
pub fn compute(size0: i32, hardness0: f64, dx: f64, dy: f64) -> BrushValues {
    let size = (size0 as f64 + dx * 0.75)
        .round_ties_even()
        .clamp(1.0, 500.0) as i32;
    let hardness = (hardness0 - dy / 250.0).clamp(0.0, 1.0);
    BrushValues { size, hardness }
}

#[cfg(test)]
mod tests {
    use super::compute;

    #[test]
    fn rounds_half_sizes_to_even_like_dotnet() {
        assert_eq!(compute(9, 0.5, 2.0, 0.0).size, 10);
        assert_eq!(compute(10, 0.5, 2.0, 0.0).size, 12);
        assert_eq!(compute(10, 0.5, -2.0, 0.0).size, 8);
    }

    #[test]
    fn clamps_size_and_hardness_to_supported_ranges() {
        assert_eq!(compute(500, 0.5, 100.0, -1000.0).size, 500);
        assert_eq!(compute(1, 0.5, -100.0, 1000.0).size, 1);
        assert_eq!(compute(20, 0.0, 0.0, -250.0).hardness, 1.0);
        assert_eq!(compute(20, 1.0, 0.0, 250.0).hardness, 0.0);
    }

    #[test]
    fn horizontal_and_vertical_drag_update_independently() {
        let result = compute(20, 0.5, 4.0, -25.0);
        assert_eq!(result.size, 23);
        assert!((result.hardness - 0.6).abs() < 1e-12);
    }
}
