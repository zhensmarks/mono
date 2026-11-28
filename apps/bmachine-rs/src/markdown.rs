// ============================================================
// Markdown — renderer deskripsi & komentar Trello ala Trello
// asli (keputusan abeng 27 Nov 20:54).
//
// Slint 1.19 TIDAK punya rich text (satu `Text` = satu gaya),
// jadi parsing + word-wrap dikerjakan di sini (Rust), lalu
// `.slint` merendernya sebagai baris-baris segmen
// (`MarkdownView` di ui/trello-detail.slint, model `MdLine`).
//
// Didukung: **bold**, *italic*, ***bold-italic***, `code`
// inline, blok kode ``` ```, list bullet (`- `) + bernomor
// (`1. `), heading `#`/`##`/`###`, quote `> `, link
// `[teks](url)` + URL telanjang (otomatis jadi link),
// mention `@nama` (chip aksen, klik = toast — belum ada data
// member, Fase 6). Baris baru tunggal = baris baru (ala
// Trello, bukan digabung jadi spasi).
//
// Word-wrap: greedy per kata dengan estimasi lebar karakter
// (font proporsional Inter; faktor aman 0,97) — di Slint
// tiap baris sudah jadi, segmen tidak perlu wrap sendiri.
// Estimasi tidak sempurna (font proporsional); bila screenshot
// verifikasi menunjukkan baris meluber, naikkan faktor aman.
// ============================================================

/// Jenis segmen inline.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MdSegKind {
    Plain,
    Bold,
    Italic,
    BoldItalic,
    Code,
    Link,
    Mention,
}

/// Satu segmen inline: teks + gaya (+ URL untuk link).
#[derive(Debug, Clone, PartialEq)]
pub struct MdSeg {
    pub text: String,
    pub kind: MdSegKind,
    pub url: String,
}

impl MdSeg {
    fn plain(text: &str) -> Self {
        MdSeg { text: text.to_string(), kind: MdSegKind::Plain, url: String::new() }
    }
}

/// Jenis baris hasil render.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MdLineKind {
    Para,
    H1,
    H2,
    H3,
    Bullet,
    Numbered,
    Quote,
    Code,
    Blank,
}

/// Satu baris render: kind + indent + marker + segmen
/// (maks 6 — lebih dari itu dipecah jadi baris lanjutan)
/// + `text` untuk blok kode (teks penuh multi-baris).
/// `gap` = ruang (px) setelah baris ini.
#[derive(Debug, Clone)]
pub struct MdLine {
    pub kind: MdLineKind,
    pub indent: i32,
    pub marker: String,
    pub text: String,
    pub segments: Vec<MdSeg>,
    pub gap: f32,
}

impl MdLine {
    fn blank() -> Self {
        MdLine { kind: MdLineKind::Blank, indent: 0, marker: String::new(), text: String::new(), segments: vec![], gap: 0.0 }
    }
}

/// Jumlah maksimum segmen per baris (slot di struct Slint
/// `MdLine` — lebih dari itu dipecah jadi baris lanjutan).
pub const MAX_SEGMENTS_PER_LINE: usize = 6;

/// Lebar indent per tingkat list (px) — harus sama dengan
/// yang dipakai `.slint` (marker 20px + indent 18px/tingkat).
pub const LIST_MARKER_W: f32 = 20.0;
pub const LIST_INDENT_W: f32 = 18.0;
/// Lebar bar quote (3px) + celah (8px) di `.slint`.
pub const QUOTE_BAR_W: f32 = 11.0;

/// Ukuran font (px) per jenis baris — harus sama dengan yang
/// dipakai `.slint` (heading lebih besar dari isi 12px).
fn font_px_for(kind: MdLineKind, base: f32) -> f32 {
    match kind {
        MdLineKind::H1 => base * 1.33,
        MdLineKind::H2 => base * 1.17,
        MdLineKind::H3 => base * 1.08,
        _ => base,
    }
}

// ------------------------- lebar karakter -------------------------

/// Estimasi lebar satu karakter, dalam satuan em (lebar huruf
/// relatif terhadap ukuran font). Font proporsional Inter.
fn char_width_em(c: char) -> f32 {
    match c {
        ' ' => 0.25,
        '!' | '.' | ',' | ':' | ';' | '\'' | '"' | '|' | '(' | ')' | '[' | ']' | '{' | '}' | '<' | '>' => 0.30,
        'i' | 'I' | 'l' | 'j' => 0.28,
        't' | 'f' => 0.34,
        '-' | '~' | '/' | '\\' => 0.33,
        'm' | 'w' | 'M' | 'W' | '@' | '%' => 0.84,
        'A'..='Z' => 0.68,
        '0'..='9' => 0.55,
        '#' | '$' | '&' | '+' | '=' | '?' | '*' | '_' | '^' => 0.56,
        _ if is_wide_char(c) => 1.0,
        _ => 0.52,
    }
}

fn is_wide_char(c: char) -> bool {
    let u = c as u32;
    (0x1100..=0x11ff).contains(&u)   // Hangul Jamo
        || (0x2e80..=0x9fff).contains(&u) // CJK
        || (0xac00..=0xd7a3).contains(&u) // Hangul
        || (0xf900..=0xfaff).contains(&u) // CJK compat
}

fn seg_text_width(seg: &MdSeg, font_px: f32) -> f32 {
    if seg.kind == MdSegKind::Code {
        // Monospace (JetBrains Mono): semua karakter sama lebar.
        return seg.text.chars().count() as f32 * 0.60 * font_px;
    }
    let bold = matches!(seg.kind, MdSegKind::Bold | MdSegKind::BoldItalic);
    let mult = if bold { 1.05 } else { 1.0 };
    seg.text.chars().map(|c| char_width_em(c) * mult * font_px).sum()
}

// ------------------------- parsing inline -------------------------

fn flush_buf(segs: &mut Vec<MdSeg>, buf: &mut String) {
    if !buf.is_empty() {
        segs.push(MdSeg::plain(buf));
        buf.clear();
    }
}

fn merge_adjacent(mut segs: Vec<MdSeg>) -> Vec<MdSeg> {
    let mut out: Vec<MdSeg> = Vec::new();
    for s in segs.drain(..) {
        if s.text.is_empty() {
            continue;
        }
        if let Some(last) = out.last_mut() {
            if last.kind == s.kind && last.url == s.url {
                last.text.push_str(&s.text);
                continue;
            }
        }
        out.push(s);
    }
    out
}

/// Cari penanda penutup: `n` karakter `c` berurutan mulai dari
/// `from`. Untuk `n == 1`, lewati pasangan `**` (itu pembuka
/// bold, bukan penutup italic).
fn find_marker(chars: &[char], from: usize, c: char, n: usize) -> Option<usize> {
    let mut j = from;
    while j + n <= chars.len() {
        if chars[j..j + n].iter().all(|&x| x == c) {
            if n == 1 {
                // Jangan makan `*` pertama dari `**`.
                if j + 1 < chars.len() && chars[j + 1] == c {
                    j += 2;
                    continue;
                }
                // Jangan makan `*` kedua dari `**`.
                if j > from && chars[j - 1] == c {
                    j += 1;
                    continue;
                }
            }
            return Some(j);
        }
        j += 1;
    }
    None
}

fn is_url_char(c: char) -> bool {
    !c.is_whitespace() && !matches!(c, ')' | ']' | '<' | '"' | '\'')
}

fn starts_bare_url(chars: &[char], i: usize) -> bool {
    let rest: String = chars[i..].iter().collect();
    rest.starts_with("http://") || rest.starts_with("https://") || rest.starts_with("www.")
}

/// Parse `[teks](url)` mulai dari `[` di posisi `i`.
/// Kembalikan (teks, url, posisi setelah `)`) bila valid
/// (url harus http(s)://), else None.
fn parse_link(chars: &[char], i: usize) -> Option<(String, String, usize)> {
    let mut j = i + 1;
    let mut text = String::new();
    while j < chars.len() && chars[j] != ']' {
        if chars[j] == '[' {
            return None; // tidak ada nesting
        }
        text.push(chars[j]);
        j += 1;
    }
    if j >= chars.len() || text.is_empty() {
        return None;
    }
    if j + 1 >= chars.len() || chars[j + 1] != '(' {
        return None;
    }
    let mut k = j + 2;
    let mut url = String::new();
    while k < chars.len() && chars[k] != ')' {
        url.push(chars[k]);
        k += 1;
    }
    if k >= chars.len() {
        return None;
    }
    if !(url.starts_with("http://") || url.starts_with("https://")) {
        return None;
    }
    Some((text, url, k + 1))
}

/// Parse segmen inline dari satu potong teks (tanpa `\n`).
pub fn parse_inline(s: &str) -> Vec<MdSeg> {
    let chars: Vec<char> = s.chars().collect();
    let mut segs: Vec<MdSeg> = Vec::new();
    let mut buf = String::new();
    let mut i = 0usize;
    while i < chars.len() {
        let c = chars[i];
        // Escape: `\*` `\_` `` \` `` `\[` `\]` `\\` `\@` -> literal.
        if c == '\\' && i + 1 < chars.len()
            && matches!(chars[i + 1], '*' | '_' | '`' | '[' | ']' | '\\' | '@')
        {
            buf.push(chars[i + 1]);
            i += 2;
            continue;
        }
        // Code span `...`.
        if c == '`' {
            if let Some(end) = find_marker(&chars, i + 1, '`', 1) {
                if end > i + 1 {
                    let text: String = chars[i + 1..end].iter().collect();
                    flush_buf(&mut segs, &mut buf);
                    segs.push(MdSeg { text, kind: MdSegKind::Code, url: String::new() });
                    i = end + 1;
                    continue;
                }
            }
            buf.push(c);
            i += 1;
            continue;
        }
        // Link [teks](url).
        if c == '[' {
            if let Some((text, url, end)) = parse_link(&chars, i) {
                flush_buf(&mut segs, &mut buf);
                segs.push(MdSeg { text, kind: MdSegKind::Link, url });
                i = end;
                continue;
            }
            buf.push(c);
            i += 1;
            continue;
        }
        // Bold / italic.
        if c == '*' || c == '_' {
            let triple = i + 2 < chars.len() && chars[i + 1] == c && chars[i + 2] == c;
            let dbl = i + 1 < chars.len() && chars[i + 1] == c;
            if triple {
                if let Some(end) = find_marker(&chars, i + 3, c, 3) {
                    let inner: String = chars[i + 3..end].iter().collect();
                    if !inner.trim().is_empty() {
                        flush_buf(&mut segs, &mut buf);
                        segs.push(MdSeg { text: inner, kind: MdSegKind::BoldItalic, url: String::new() });
                        i = end + 3;
                        continue;
                    }
                }
                // Tanpa penutup: telan ketiganya sebagai literal
                // (jangan biarkan `*` sisanya dibaca ulang sebagai
                // pembuka italic).
                buf.push(c);
                buf.push(c);
                buf.push(c);
                i += 3;
                continue;
            } else if dbl {
                if let Some(end) = find_marker(&chars, i + 2, c, 2) {
                    let inner: String = chars[i + 2..end].iter().collect();
                    if !inner.trim().is_empty() {
                        flush_buf(&mut segs, &mut buf);
                        segs.push(MdSeg { text: inner, kind: MdSegKind::Bold, url: String::new() });
                        i = end + 2;
                        continue;
                    }
                }
                buf.push(c);
                buf.push(c);
                i += 2;
                continue;
            } else {
                // Italic tunggal. `_` intra-kata (foo_bar) BUKAN
                // italic (ala CommonMark); `*` boleh intra-kata.
                let word_ok = if c == '_' {
                    let prev_word = i > 0 && (chars[i - 1].is_alphanumeric());
                    let next_word = i + 1 < chars.len() && chars[i + 1].is_alphanumeric();
                    !(prev_word && next_word)
                } else {
                    true
                };
                if word_ok {
                    if let Some(end) = find_marker(&chars, i + 1, c, 1) {
                        let inner: String = chars[i + 1..end].iter().collect();
                        if !inner.trim().is_empty() {
                            flush_buf(&mut segs, &mut buf);
                            segs.push(MdSeg { text: inner, kind: MdSegKind::Italic, url: String::new() });
                            i = end + 1;
                            continue;
                        }
                    }
                }
            }
            buf.push(c);
            i += 1;
            continue;
        }
        // Mention @nama — bukan bagian dari email (char sebelum
        // @ bukan huruf/angka).
        if c == '@' {
            let prev_ok = i == 0 || !chars[i - 1].is_alphanumeric();
            if prev_ok {
                let mut j = i + 1;
                while j < chars.len()
                    && (chars[j].is_alphanumeric() || matches!(chars[j], '_' | '-' | '.'))
                {
                    j += 1;
                }
                if j > i + 1 {
                    let name: String = chars[i..j].iter().collect();
                    flush_buf(&mut segs, &mut buf);
                    segs.push(MdSeg { text: name, kind: MdSegKind::Mention, url: String::new() });
                    i = j;
                    continue;
                }
            }
            buf.push(c);
            i += 1;
            continue;
        }
        // URL telanjang.
        if starts_bare_url(&chars, i) {
            let mut j = i;
            while j < chars.len() && is_url_char(chars[j]) {
                j += 1;
            }
            let mut end = j;
            while end > i && matches!(chars[end - 1], '.' | ',' | ';' | ':' | '!' | '?') {
                end -= 1;
            }
            if end > i {
                let mut url: String = chars[i..end].iter().collect();
                if url.starts_with("www.") {
                    url = format!("https://{url}");
                }
                let text: String = chars[i..end].iter().collect();
                flush_buf(&mut segs, &mut buf);
                segs.push(MdSeg { text, kind: MdSegKind::Link, url });
                i = end;
                continue;
            }
        }
        buf.push(c);
        i += 1;
    }
    flush_buf(&mut segs, &mut buf);
    merge_adjacent(segs)
}

// ------------------------- word wrap -------------------------

/// Satu kata beserta gayanya (unit wrap). `space_before` = ada spasi
/// di sumber sebelum kata ini (antar kata maupun antar segmen —
/// mis. `**tebal** biasa`: kata "biasa" didahului spasi dari segmen
/// sebelumnya; tanpa ini render jadi "tebalbiasa").
#[derive(Debug, Clone)]
struct Word {
    text: String,
    kind: MdSegKind,
    url: String,
    width: f32,
    space_before: bool,
}

fn seg_to_words(seg: &MdSeg, font_px: f32, prev_ended_with_space: bool) -> Vec<Word> {
    let mut out = Vec::new();
    // Kata pertama: spasi di awal segmen ini, atau segmen
    // sebelumnya berakhir dengan spasi. Kata berikut: selalu
    // dipisah spasi (split(' ') membuang pemisahnya).
    let mut space_before = prev_ended_with_space || seg.text.starts_with(' ');
    for w in seg.text.split(' ') {
        if w.is_empty() {
            continue;
        }
        let probe = MdSeg { text: w.to_string(), kind: seg.kind, url: seg.url.clone() };
        out.push(Word {
            text: w.to_string(),
            kind: seg.kind,
            url: seg.url.clone(),
            width: seg_text_width(&probe, font_px),
            space_before,
        });
        space_before = true;
    }
    out
}

/// Pecah satu kata panjang jadi potongan-potongan muat lebar.
/// Hanya potongan pertama mewarisi `space_before` kata asal.
fn split_long_word(word: &Word, avail: f32, font_px: f32) -> Vec<Word> {
    let mut out = Vec::new();
    let mut cur = String::new();
    let mut cur_w = 0.0f32;
    let mut first_piece = true;
    for ch in word.text.chars() {
        let probe = MdSeg { text: ch.to_string(), kind: word.kind, url: word.url.clone() };
        let cw = seg_text_width(&probe, font_px);
        if !cur.is_empty() && cur_w + cw > avail {
            out.push(Word { text: std::mem::take(&mut cur), kind: word.kind, url: word.url.clone(), width: cur_w, space_before: if first_piece { word.space_before } else { false } });
            cur_w = 0.0;
            first_piece = false;
        }
        cur.push(ch);
        cur_w += cw;
    }
    if !cur.is_empty() {
        out.push(Word { text: cur, kind: word.kind, url: word.url.clone(), width: cur_w, space_before: if first_piece { word.space_before } else { false } });
    }
    out
}

fn words_to_segments(words: &[Word]) -> Vec<MdSeg> {
    let mut segs: Vec<MdSeg> = Vec::new();
    for w in words {
        if let Some(last) = segs.last_mut() {
            if last.kind == w.kind && last.url == w.url {
                if w.space_before {
                    last.text.push(' ');
                }
                last.text.push_str(&w.text);
                continue;
            }
        }
        // Spasi antar-segmen ditempel di depan teks segmen baru
        // (kecuali kata pertama baris — spasi awal baris dibuang).
        let text = if w.space_before && !segs.is_empty() {
            format!(" {}", w.text)
        } else {
            w.text.clone()
        };
        segs.push(MdSeg { text, kind: w.kind, url: w.url.clone() });
    }
    segs
}

/// Bungkus segmen-segmen jadi baris-baris muat `avail_px`.
/// Baris pertama memakai `marker`, baris lanjutan marker "".
fn wrap_segments(
    segs: &[MdSeg],
    avail_px: f32,
    font_px: f32,
    kind: MdLineKind,
    indent: i32,
    marker: &str,
) -> Vec<MdLine> {
    let avail = (avail_px * 0.97).max(24.0);
    let space_w = 0.25 * font_px;
    let mut words: Vec<Word> = Vec::new();
    let mut prev_space = false;
    for s in segs {
        words.extend(seg_to_words(s, font_px, prev_space));
        prev_space = s.text.ends_with(' ');
    }
    // Kata yang sendirian sudah lebih lebar dari baris -> pecah.
    let mut flat: Vec<Word> = Vec::new();
    for w in words {
        if w.width > avail {
            flat.extend(split_long_word(&w, avail, font_px));
        } else {
            flat.push(w);
        }
    }
    let mut rows: Vec<MdLine> = Vec::new();
    let mut cur: Vec<Word> = Vec::new();
    let mut cur_w = 0.0f32;
    let mut first = true;
    for w in flat {
        let w_width = w.width;
        let add = if cur.is_empty() { w_width } else { cur_w + space_w + w_width };
        if !cur.is_empty() && add > avail {
            rows.push(make_row(kind, indent, if first { marker } else { "" }, &cur, 0.0));
            first = false;
            cur = vec![w];
            cur_w = w_width;
        } else {
            cur_w = add;
            cur.push(w);
        }
    }
    if !cur.is_empty() || rows.is_empty() {
        rows.push(make_row(kind, indent, if first { marker } else { "" }, &cur, 0.0));
    }
    // Pecah baris yang segmennya > batas slot Slint.
    let mut out: Vec<MdLine> = Vec::new();
    for row in rows {
        if row.segments.len() <= MAX_SEGMENTS_PER_LINE {
            out.push(row);
        } else {
            let mut rest = row.segments;
            let mut first_chunk = true;
            while !rest.is_empty() {
                let take = rest.len().min(MAX_SEGMENTS_PER_LINE);
                let chunk: Vec<MdSeg> = rest.drain(..take).collect();
                out.push(MdLine {
                    kind: row.kind,
                    indent: row.indent,
                    marker: if first_chunk { row.marker.clone() } else { String::new() },
                    text: String::new(),
                    segments: chunk,
                    gap: 0.0,
                });
                first_chunk = false;
            }
        }
    }
    out
}

fn make_row(kind: MdLineKind, indent: i32, marker: &str, words: &[Word], gap: f32) -> MdLine {
    MdLine {
        kind,
        indent,
        marker: marker.to_string(),
        text: String::new(),
        segments: words_to_segments(words),
        gap,
    }
}

// ------------------------- parsing blok -------------------------

/// Parse Markdown lengkap jadi baris-baris render.
/// `width_px` = lebar area teks (px logis), `base_font_px`
/// = ukuran font isi (12px di detail/komentar).
pub fn parse_markdown(src: &str, width_px: f32, base_font_px: f32) -> Vec<MdLine> {
    let src = src.replace("\r\n", "\n").replace('\r', "\n");
    let lines: Vec<&str> = src.lines().collect();
    let mut out: Vec<MdLine> = Vec::new();
    let mut i = 0usize;
    let mut in_code = false;
    let mut code_buf: Vec<String> = Vec::new();
    // Paragraf berjalan: baris-barisnya dirender satu-satu
    // (baris baru tunggal = baris baru, ala Trello).
    let mut para: Vec<String> = Vec::new();
    let flush_para = |out: &mut Vec<MdLine>, para: &mut Vec<String>| {
        if para.is_empty() {
            return;
        }
        let rows = std::mem::take(para);
        let n = rows.len();
        for (idx, line) in rows.into_iter().enumerate() {
            let segs = parse_inline(&line);
            let font_px = font_px_for(MdLineKind::Para, base_font_px);
            let mut wrapped = wrap_segments(&segs, width_px, font_px, MdLineKind::Para, 0, "");
            let last = wrapped.len().saturating_sub(1);
            for (wi, w) in wrapped.iter_mut().enumerate() {
                if wi == last && idx == n - 1 {
                    w.gap = 8.0;
                }
            }
            out.append(&mut wrapped);
        }
    };

    while i < lines.len() {
        let raw = lines[i];
        let line = raw.trim_end();
        let stripped = line.trim_start();
        let lead = line.len() - stripped.len();

        // Pagar blok kode ``` — buka/tutup.
        if stripped.starts_with("```") {
            if in_code {
                // Tutup blok.
                in_code = false;
                let text = code_buf.join("\n");
                code_buf.clear();
                out.push(MdLine {
                    kind: MdLineKind::Code,
                    indent: 0,
                    marker: String::new(),
                    text,
                    segments: vec![],
                    gap: 8.0,
                });
            } else {
                flush_para(&mut out, &mut para);
                in_code = true;
            }
            i += 1;
            continue;
        }
        if in_code {
            code_buf.push(line.to_string());
            i += 1;
            continue;
        }

        // Baris kosong = pemisah blok.
        if stripped.is_empty() {
            flush_para(&mut out, &mut para);
            out.push(MdLine::blank());
            i += 1;
            continue;
        }

        // Heading # / ## / ### (butuh spasi setelah pagar).
        if stripped.starts_with('#') {
            let hashes = stripped.chars().take_while(|&c| c == '#').count();
            if (1..=3).contains(&hashes)
                && stripped.chars().nth(hashes) == Some(' ')
            {
                flush_para(&mut out, &mut para);
                let content = stripped[hashes + 1..].trim();
                let kind = match hashes {
                    1 => MdLineKind::H1,
                    2 => MdLineKind::H2,
                    _ => MdLineKind::H3,
                };
                let segs = parse_inline(content);
                let font_px = font_px_for(kind, base_font_px);
                let mut wrapped = wrap_segments(&segs, width_px, font_px, kind, 0, "");
                let last = wrapped.len().saturating_sub(1);
                for (wi, w) in wrapped.iter_mut().enumerate() {
                    if wi == last {
                        w.gap = 8.0;
                    }
                }
                out.append(&mut wrapped);
                i += 1;
                continue;
            }
        }

        // Quote > ...
        if stripped.starts_with("> ") || stripped == ">" {
            flush_para(&mut out, &mut para);
            let content = stripped.strip_prefix('>').unwrap_or("").trim_start();
            let segs = parse_inline(content);
            let font_px = font_px_for(MdLineKind::Quote, base_font_px);
            let avail = (width_px - QUOTE_BAR_W).max(24.0);
            let mut wrapped = wrap_segments(&segs, avail, font_px, MdLineKind::Quote, 0, "");
            let last = wrapped.len().saturating_sub(1);
            for (wi, w) in wrapped.iter_mut().enumerate() {
                if wi == last {
                    w.gap = 8.0;
                }
            }
            out.append(&mut wrapped);
            i += 1;
            continue;
        }

        // Bullet - / * / + (indent 0..3 spasi; 2 spasi = tingkat 1).
        if lead <= 3 {
            if let Some(rest) = stripped
                .strip_prefix("- ")
                .or_else(|| stripped.strip_prefix("* "))
                .or_else(|| stripped.strip_prefix("+ "))
            {
                flush_para(&mut out, &mut para);
                let indent = (lead / 2) as i32;
                let segs = parse_inline(rest);
                let font_px = font_px_for(MdLineKind::Bullet, base_font_px);
                let avail = (width_px - LIST_MARKER_W - LIST_INDENT_W * indent as f32).max(24.0);
                let mut wrapped =
                    wrap_segments(&segs, avail, font_px, MdLineKind::Bullet, indent, "•");
                // Baris lanjutan item yang sama (indent >= 2 spasi).
                let mut j = i + 1;
                while j < lines.len() {
                    let l2 = lines[j].trim_end();
                    let s2 = l2.trim_start();
                    let lead2 = l2.len() - s2.len();
                    if lead2 >= 2 && !s2.is_empty() && !s2.starts_with("```") {
                        let segs2 = parse_inline(s2);
                        let mut w2 = wrap_segments(
                            &segs2,
                            avail,
                            font_px,
                            MdLineKind::Bullet,
                            indent,
                            "",
                        );
                        wrapped.append(&mut w2);
                        j += 1;
                    } else {
                        break;
                    }
                }
                let last = wrapped.len().saturating_sub(1);
                for (wi, w) in wrapped.iter_mut().enumerate() {
                    if wi == last {
                        w.gap = 8.0;
                    }
                }
                out.append(&mut wrapped);
                i = j;
                continue;
            }
            // Numbered 1. / 1)
            if let Some((num, rest)) = parse_numbered(stripped) {
                flush_para(&mut out, &mut para);
                let indent = (lead / 2) as i32;
                let segs = parse_inline(rest);
                let font_px = font_px_for(MdLineKind::Numbered, base_font_px);
                let avail = (width_px - LIST_MARKER_W - LIST_INDENT_W * indent as f32).max(24.0);
                let mut wrapped = wrap_segments(
                    &segs,
                    avail,
                    font_px,
                    MdLineKind::Numbered,
                    indent,
                    &format!("{num}."),
                );
                let last = wrapped.len().saturating_sub(1);
                for (wi, w) in wrapped.iter_mut().enumerate() {
                    if wi == last {
                        w.gap = 8.0;
                    }
                }
                out.append(&mut wrapped);
                i += 1;
                continue;
            }
        }

        // Baris biasa = paragraf (satu baris = satu baris render).
        para.push(line.to_string());
        i += 1;
    }
    if in_code {
        // Pagar penutup hilang — tetap render isi sebagai kode.
        let text = code_buf.join("\n");
        out.push(MdLine {
            kind: MdLineKind::Code,
            indent: 0,
            marker: String::new(),
            text,
            segments: vec![],
            gap: 8.0,
        });
    }
    flush_para(&mut out, &mut para);
    // Buang baris kosong di akhir (ruang kosong di bawah).
    while matches!(out.last(), Some(l) if l.kind == MdLineKind::Blank) {
        out.pop();
    }
    out
}

/// Parse `12. teks` / `12) teks` -> (12, "teks").
fn parse_numbered(s: &str) -> Option<(u32, &str)> {
    let mut digits = String::new();
    for c in s.chars() {
        if c.is_ascii_digit() {
            digits.push(c);
        } else {
            break;
        }
    }
    if digits.is_empty() || digits.len() > 3 {
        return None;
    }
    let rest = &s[digits.len()..];
    if let Some(after) = rest.strip_prefix(". ").or_else(|| rest.strip_prefix(") ")) {
        digits.parse::<u32>().ok().map(|n| (n, after))
    } else {
        None
    }
}

// ------------------------- toolbar editor -------------------------

/// Baris pertama deskripsi sebagai teks polos (tanpa sintaks
/// Markdown) — dipakai subtitle satu-baris di daftar kartu
/// (CardListWindow) supaya `**tebal**` / `# Judul` tidak tampil
/// mentah di sana. Marker blok di awal baris dikupas, sisanya
/// di-parse inline lalu teks segmen digabung (teks link/mention/
/// kode dipertahankan, sintaksnya dibuang).
pub fn plain_first_line(desc: &str) -> String {
    let first = desc.lines().next().unwrap_or("").trim();
    let mut s = first;
    loop {
        let t = s.trim_start();
        if let Some(rest) = t.strip_prefix('#') {
            s = rest.trim_start();
            continue;
        }
        if let Some(rest) = t.strip_prefix('>') {
            s = rest.trim_start();
            continue;
        }
        if let Some(rest) = t
            .strip_prefix("- ")
            .or_else(|| t.strip_prefix("* "))
            .or_else(|| t.strip_prefix("+ "))
        {
            s = rest;
            continue;
        }
        let digits: String = t.chars().take_while(|c| c.is_ascii_digit()).collect();
        if !digits.is_empty() {
            let rest = &t[digits.len()..];
            if let Some(r) = rest
                .strip_prefix(". ")
                .or_else(|| rest.strip_prefix(") "))
            {
                s = r;
                continue;
            }
        }
        break;
    }
    parse_inline(s)
        .into_iter()
        .map(|seg| seg.text)
        .collect::<String>()
        .trim()
        .to_string()
}

/// Sisipkan snippet Markdown (toolbar editor) ke dalam `text`.
/// `cursor`/`anchor` = byte offset kursor/seleksi dari Slint
/// (`cursor-position-byte-offset`, `anchor-position-byte-offset`).
/// Ada seleksi -> bungkus seleksi; tidak ada -> sisipkan
/// snippet di posisi kursor. Kembalikan teks baru (posisi
/// kursor setelah sisipan tidak bisa diatur dari Slint —
/// TextInput tidak mengekspos setter kursor).
pub fn md_insert(kind: &str, text: &str, cursor: i32, anchor: i32) -> String {
    let len = text.len() as i32;
    let clamp_byte = |p: i32| -> usize {
        let mut p = p.clamp(0, len) as usize;
        while p < text.len() && !text.is_char_boundary(p) {
            p += 1;
        }
        p
    };
    let cur = clamp_byte(cursor);
    let anc = clamp_byte(anchor);
    let (lo, hi) = if cur <= anc { (cur, anc) } else { (anc, cur) };
    let has_sel = lo != hi;

    // Prefix tiap baris (untuk list).
    let prefix_lines = |s: &str, prefix: &dyn Fn(usize) -> String| -> String {
        let mut out = String::new();
        for (idx, line) in s.split('\n').enumerate() {
            if idx > 0 {
                out.push('\n');
            }
            out.push_str(&prefix(idx));
            out.push_str(line);
        }
        out
    };

    if has_sel {
        let sel = &text[lo..hi];
        let replaced = match kind {
            "bold" => format!("**{sel}**"),
            "italic" => format!("*{sel}*"),
            "code" => format!("`{sel}`"),
            "link" => format!("[{sel}](https://)"),
            "mention" => format!("@{sel}"),
            "bullet" => prefix_lines(sel, &|_| "- ".to_string()),
            "numbered" => prefix_lines(sel, &|i| format!("{}. ", i + 1)),
            _ => return text.to_string(),
        };
        let mut out = String::with_capacity(text.len() + replaced.len());
        out.push_str(&text[..lo]);
        out.push_str(&replaced);
        out.push_str(&text[hi..]);
        return out;
    }

    let snippet: String = match kind {
        "bold" => "**teks tebal**".to_string(),
        "italic" => "*teks miring*".to_string(),
        "code" => "`kode`".to_string(),
        "link" => "[teks](https://)".to_string(),
        "mention" => "@".to_string(),
        "bullet" => {
            let at_line_start = cur == 0 || text.as_bytes().get(cur - 1) == Some(&b'\n');
            if at_line_start {
                "- ".to_string()
            } else {
                "\n- ".to_string()
            }
        }
        "numbered" => {
            let at_line_start = cur == 0 || text.as_bytes().get(cur - 1) == Some(&b'\n');
            if at_line_start {
                "1. ".to_string()
            } else {
                "\n1. ".to_string()
            }
        }
        _ => return text.to_string(),
    };
    let mut out = String::with_capacity(text.len() + snippet.len());
    out.push_str(&text[..cur]);
    out.push_str(&snippet);
    out.push_str(&text[cur..]);
    out
}

// ------------------------- unit tests -------------------------

#[cfg(test)]
mod tests {
    use super::*;

    fn kinds(segs: &[MdSeg]) -> Vec<MdSegKind> {
        segs.iter().map(|s| s.kind).collect()
    }

    #[test]
    fn inline_bold_italic_code() {
        let segs = parse_inline("Crop **foto** dan *centering* plus `guide`.");
        assert_eq!(
            kinds(&segs),
            vec![
                MdSegKind::Plain,
                MdSegKind::Bold,
                MdSegKind::Plain,
                MdSegKind::Italic,
                MdSegKind::Plain,
                MdSegKind::Code,
                MdSegKind::Plain
            ]
        );
        assert_eq!(segs[1].text, "foto");
        assert_eq!(segs[3].text, "centering");
        assert_eq!(segs[5].text, "guide");
    }

    #[test]
    fn inline_bold_italic_triple() {
        let segs = parse_inline("***penting*** sekali");
        assert_eq!(segs[0].kind, MdSegKind::BoldItalic);
        assert_eq!(segs[0].text, "penting");
    }

    #[test]
    fn inline_underscore_intra_word_not_italic() {
        let segs = parse_inline("file foto_pas.jpg jangan miring");
        assert!(segs.iter().all(|s| s.kind == MdSegKind::Plain));
        let segs = parse_inline("_miring_ dong");
        assert_eq!(segs[0].kind, MdSegKind::Italic);
    }

    #[test]
    fn inline_link_and_bare_url() {
        let segs = parse_inline("lihat [panduan](https://example.com/a) dan https://trello.com/c/xyz.");
        assert_eq!(segs[1].kind, MdSegKind::Link);
        assert_eq!(segs[1].text, "panduan");
        assert_eq!(segs[1].url, "https://example.com/a");
        let link = segs.iter().find(|s| s.kind == MdSegKind::Link && s.text.contains("trello")).unwrap();
        assert_eq!(link.url, "https://trello.com/c/xyz");
        // Titik di akhir URL tidak ikut.
        assert!(!link.url.ends_with('.'));
    }

    #[test]
    fn inline_mention_not_email() {
        let segs = parse_inline("tanya @alif dan @surya-2 ya");
        let mentions: Vec<&MdSeg> =
            segs.iter().filter(|s| s.kind == MdSegKind::Mention).collect();
        assert_eq!(mentions.len(), 2);
        assert_eq!(mentions[0].text, "@alif");
        let segs = parse_inline("email ke budi@sekolah.id bukan mention");
        assert!(segs.iter().all(|s| s.kind != MdSegKind::Mention));
    }

    #[test]
    fn inline_unclosed_markers_are_literal() {
        let segs = parse_inline("bintang ** tanpa tutup dan * satu");
        assert!(segs.iter().all(|s| s.kind == MdSegKind::Plain));
    }

    #[test]
    fn blocks_lists_heading_quote_code() {
        let md = "# Judul\n\n- satu\n- **dua** tebal\n\n1. pertama\n2. kedua\n\n> kutipan *miring*\n\n```\nbaris kode\nkedua\n```\n\nparagraf biasa";
        let lines = parse_markdown(md, 360.0, 12.0);
        let kinds: Vec<MdLineKind> = lines.iter().map(|l| l.kind).collect();
        assert!(kinds.contains(&MdLineKind::H1));
        assert_eq!(kinds.iter().filter(|k| **k == MdLineKind::Bullet).count(), 2);
        assert_eq!(kinds.iter().filter(|k| **k == MdLineKind::Numbered).count(), 2);
        assert!(kinds.contains(&MdLineKind::Quote));
        assert!(kinds.contains(&MdLineKind::Code));
        assert!(kinds.contains(&MdLineKind::Para));
        let code = lines.iter().find(|l| l.kind == MdLineKind::Code).unwrap();
        assert_eq!(code.text, "baris kode\nkedua");
        let numbered: Vec<&MdLine> =
            lines.iter().filter(|l| l.kind == MdLineKind::Numbered).collect();
        assert_eq!(numbered[0].marker, "1.");
        assert_eq!(numbered[1].marker, "2.");
    }

    #[test]
    fn wrap_never_exceeds_width() {
        let md = "Ini kalimat yang cukup panjang dengan **kata tebal** di tengahnya dan sebuah [tautan panjang](https://example.com/jalan/panjang) supaya baris terpaksa membungkus.";
        let lines = parse_markdown(md, 200.0, 12.0);
        assert!(lines.len() > 1);
        for l in &lines {
            if l.kind == MdLineKind::Para {
                // Spasi antar-kata hidup DI DALAM teks segmen (bukan
                // antar-segmen), jadi lebar baris = jumlah lebar
                // segmen saja — tanpa tambahan per batas segmen.
                let w: f32 = l.segments.iter().map(|s| seg_text_width(s, 12.0)).sum::<f32>();
                assert!(w <= 200.0 * 0.97 + 1.0, "baris meluber: {w} {l:?}");
                assert!(l.segments.len() <= MAX_SEGMENTS_PER_LINE);
            }
        }
    }

    #[test]
    fn wrap_short_text_stays_one_line() {
        let lines = parse_markdown("Edit pas foto sekolah.", 360.0, 12.0);
        let paras: Vec<&MdLine> = lines.iter().filter(|l| l.kind == MdLineKind::Para).collect();
        assert_eq!(paras.len(), 1);
        assert_eq!(paras[0].segments.len(), 1);
    }

    #[test]
    fn wrap_keeps_space_between_segments() {
        // Regresi 28 Nov 2026: spasi antar-segmen hilang di render
        // ("**Diperbaiki** via" jadi "Diperbaikivia").
        let lines = parse_markdown(
            "**Diperbaiki** via editor — *cek lagi* [panduan](https://trello.com/b/contoh) @achmadysaifuloh dan `accent`",
            2000.0,
            12.0,
        );
        // Satu paragraf bisa jadi >1 MdLine (chunk maks 6 segmen)
        // — gabung semua baris Para untuk teks render utuh.
        let rendered: String = lines
            .iter()
            .filter(|l| l.kind == MdLineKind::Para)
            .flat_map(|l| l.segments.iter())
            .map(|s| s.text.as_str())
            .collect();
        assert_eq!(
            rendered,
            "Diperbaiki via editor — cek lagi panduan @achmadysaifuloh dan accent"
        );
        // Kata pertama baris tidak boleh berawalan spasi.
        let first = lines.iter().find(|l| l.kind == MdLineKind::Para).unwrap();
        assert!(!first.segments[0].text.starts_with(' '));
    }

    #[test]
    fn plain_first_line_strips_markdown() {
        assert_eq!(
            plain_first_line("**Diperbaiki** via editor — *cek lagi* [panduan](https://trello.com/b/contoh) @achmadysaifuloh"),
            "Diperbaiki via editor — cek lagi panduan @achmadysaifuloh"
        );
        assert_eq!(plain_first_line("# Catatan MI Manaratul Islam\nisi"), "Catatan MI Manaratul Islam");
        assert_eq!(plain_first_line("- PAS FOTO (20RB)\n- ID CARD"), "PAS FOTO (20RB)");
        assert_eq!(plain_first_line("1. Cek logo terlihat\n2. Simpan"), "Cek logo terlihat");
        assert_eq!(plain_first_line("> Catatan QC: terang"), "Catatan QC: terang");
        assert_eq!(plain_first_line("Kode warna `accent` dipakai"), "Kode warna accent dipakai");
        assert_eq!(plain_first_line(""), "");
        assert_eq!(plain_first_line("\n\nisi"), "");
    }

    #[test]
    fn empty_and_plain_text() {
        assert!(parse_markdown("", 360.0, 12.0).is_empty());
        let lines = parse_markdown("NAMA SEKOLAH : MI MANARATUL ISLAM\n\nALAMAT : Jl. Madrasah No.12", 360.0, 12.0);
        let paras: Vec<&MdLine> = lines.iter().filter(|l| l.kind == MdLineKind::Para).collect();
        assert_eq!(paras.len(), 2);
    }

    #[test]
    fn md_insert_wrap_selection() {
        assert_eq!(md_insert("bold", "crop foto", 5, 9), "crop **foto**");
        assert_eq!(md_insert("italic", "crop foto", 0, 4), "*crop* foto");
        assert_eq!(md_insert("code", "a b", 0, 1), "`a` b");
        assert_eq!(md_insert("link", "panduan", 0, 7), "[panduan](https://)");
        assert_eq!(md_insert("bullet", "satu\ndua", 0, 8), "- satu\n- dua");
        assert_eq!(md_insert("numbered", "satu\ndua", 0, 8), "1. satu\n2. dua");
    }

    #[test]
    fn md_insert_snippet_at_cursor() {
        assert_eq!(md_insert("bold", "halo ", 5, 5), "halo **teks tebal**");
        assert_eq!(md_insert("bullet", "halo", 4, 4), "halo\n- ");
        assert_eq!(md_insert("bullet", "", 0, 0), "- ");
        assert_eq!(md_insert("mention", "tanya ", 6, 6), "tanya @");
        // Offset byte di tengah karakter multi-byte dijepit ke boundary.
        assert_eq!(md_insert("bold", "aé b", 2, 2), "aé**teks tebal** b");
    }
}
