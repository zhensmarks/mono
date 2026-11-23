/// 8-bit mask operations ported from the C# editor logic.
/// Buffers may be larger than width * height; trailing bytes are left untouched.
pub fn invert(mask: &mut [u8]) {
    for value in mask {
        *value = 255 - *value;
    }
}

pub fn fill(mask: &mut [u8], value: u8) {
    mask.fill(value);
}

pub fn threshold(mask: &mut [u8], threshold: u8) {
    for value in mask {
        *value = if *value >= threshold { 255 } else { 0 };
    }
}

/// Expand (max filter) or contract (min filter) using a separable square kernel.
/// Invalid or undersized shapes are ignored instead of indexing outside the buffer.
pub fn shift_edge(mask: &mut [u8], width: usize, height: usize, radius: usize, expand: bool) {
    let Some(length) = width.checked_mul(height) else {
        return;
    };
    if width == 0 || height == 0 || radius == 0 || mask.len() < length {
        return;
    }

    let source = mask[..length].to_vec();
    let mut temporary = vec![0; length];
    pass_horizontal(&source, &mut temporary, width, height, radius, expand);
    pass_vertical(&temporary, &mut mask[..length], width, height, radius, expand);
}

/// Three clamped-edge box blurs approximate a Gaussian, matching the C# editor.
pub fn feather(mask: &mut [u8], width: usize, height: usize, radius: usize) {
    let Some(length) = width.checked_mul(height) else {
        return;
    };
    if width == 0 || height == 0 || radius == 0 || mask.len() < length {
        return;
    }

    let box_radius = ((radius as f64 / 1.5).round_ties_even() as usize).max(1);
    let mut temporary = vec![0; length];
    for _ in 0..3 {
        blur_horizontal(&mask[..length], &mut temporary, width, height, box_radius);
        blur_vertical(&temporary, &mut mask[..length], width, height, box_radius);
    }
}

fn pass_horizontal(src: &[u8], dst: &mut [u8], width: usize, height: usize, radius: usize, expand: bool) {
    for y in 0..height {
        let row = y * width;
        for x in 0..width {
            let start = x.saturating_sub(radius);
            let end = x.saturating_add(radius).min(width - 1);
            let mut best = src[row + x];
            for sx in start..=end {
                let value = src[row + sx];
                best = if expand { best.max(value) } else { best.min(value) };
            }
            dst[row + x] = best;
        }
    }
}

fn pass_vertical(src: &[u8], dst: &mut [u8], width: usize, height: usize, radius: usize, expand: bool) {
    for y in 0..height {
        let start = y.saturating_sub(radius);
        let end = y.saturating_add(radius).min(height - 1);
        for x in 0..width {
            let mut best = src[y * width + x];
            for sy in start..=end {
                let value = src[sy * width + x];
                best = if expand { best.max(value) } else { best.min(value) };
            }
            dst[y * width + x] = best;
        }
    }
}

fn blur_horizontal(src: &[u8], dst: &mut [u8], width: usize, height: usize, radius: usize) {
    let window = radius.saturating_mul(2).saturating_add(1).max(1) as u128;
    for y in 0..height {
        let row = y * width;
        let mut sum = 0_u128;
        for offset in 0..window as usize {
            let x = if offset < radius { 0 } else { (offset - radius).min(width - 1) };
            sum += src[row + x] as u128;
        }
        for x in 0..width {
            dst[row + x] = (sum / window) as u8;
            let add = x.saturating_add(radius).saturating_add(1).min(width - 1);
            let sub = x.saturating_sub(radius);
            sum += src[row + add] as u128;
            sum -= src[row + sub] as u128;
        }
    }
}

fn blur_vertical(src: &[u8], dst: &mut [u8], width: usize, height: usize, radius: usize) {
    let window = radius.saturating_mul(2).saturating_add(1).max(1) as u128;
    for x in 0..width {
        let mut sum = 0_u128;
        for offset in 0..window as usize {
            let y = if offset < radius { 0 } else { (offset - radius).min(height - 1) };
            sum += src[y * width + x] as u128;
        }
        for y in 0..height {
            dst[y * width + x] = (sum / window) as u8;
            let add = y.saturating_add(radius).saturating_add(1).min(height - 1);
            let sub = y.saturating_sub(radius);
            sum += src[add * width + x] as u128;
            sum -= src[sub * width + x] as u128;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{feather, fill, invert, shift_edge, threshold};

    #[test]
    fn simple_mask_operations_match_expected_values() {
        let mut mask = [0, 127, 128, 255, 42];
        threshold(&mut mask, 128);
        assert_eq!(mask, [0, 0, 255, 255, 0]);
        invert(&mut mask);
        assert_eq!(mask, [255, 255, 0, 0, 255]);
        fill(&mut mask, 17);
        assert_eq!(mask, [17; 5]);
    }

    #[test]
    fn shift_edge_expands_and_contracts_at_image_boundaries() {
        let mut expanded = [0, 0, 0, 0, 255, 0, 0, 0, 0];
        shift_edge(&mut expanded, 3, 3, 1, true);
        assert_eq!(expanded, [255; 9]);

        let mut contracted = [255, 255, 255, 255, 255, 255, 255, 255, 255];
        contracted[4] = 0;
        shift_edge(&mut contracted, 3, 3, 1, false);
        assert_eq!(contracted, [0; 9]);
    }

    #[test]
    fn feather_preserves_a_constant_mask_and_ignores_invalid_shapes() {
        let mut constant = [63; 9];
        feather(&mut constant, 3, 3, 2);
        assert_eq!(constant, [63; 9]);

        let mut short = [12, 34, 56];
        let before = short;
        feather(&mut short, 2, 2, 1);
        assert_eq!(short, before);
    }
}
