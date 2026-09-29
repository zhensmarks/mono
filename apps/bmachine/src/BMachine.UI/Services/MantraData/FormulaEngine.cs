using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BMachine.UI.Services.MantraData;

public sealed class FormulaException : Exception
{
    public FormulaException(string message) : base(message) { }
}

/// <summary>
/// Excel-style formulas for yearbook / ID card tables.
/// Column refs: [NAMA]  Concat: &  Functions: UPPER LOWER PROPER TRIM LEFT RIGHT MID LEN CONCAT IF SUBSTITUTE TTL JKNORM
/// </summary>
public static class FormulaEngine
{
    public static bool LooksLikeFormula(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.TrimStart().StartsWith('=');
    }

    public static string Evaluate(string formula, IReadOnlyDictionary<string, string> cells, IReadOnlyList<string> columns)
    {
        if (string.IsNullOrWhiteSpace(formula)) return string.Empty;
        var src = formula.Trim();
        if (src.StartsWith('=')) src = src[1..];
        if (string.IsNullOrWhiteSpace(src)) return string.Empty;

        var parser = new Parser(src, cells, columns);
        return parser.ParseExpression();
    }

    private sealed class Parser
    {
        private readonly string _src;
        private readonly IReadOnlyDictionary<string, string> _cells;
        private readonly List<string> _columns;
        private int _i;

        public Parser(string src, IReadOnlyDictionary<string, string> cells, IReadOnlyList<string> columns)
        {
            _src = src;
            _cells = cells;
            _columns = columns.ToList();
        }

        public string ParseExpression()
        {
            var left = ParseCompare();
            SkipWs();
            if (_i >= _src.Length) return left;

            // concatenation: a & b & c
            while (Peek() == '&')
            {
                _i++;
                var right = ParseCompare();
                left += right;
                SkipWs();
            }

            SkipWs();
            if (_i < _src.Length)
                throw new FormulaException($"Sisa rumus tidak dikenali: '{_src[_i..]}'");
            return left;
        }

        private string ParseInner()
        {
            var left = ParseCompare();
            SkipWs();
            while (Peek() == '&')
            {
                _i++;
                left += ParseCompare();
                SkipWs();
            }
            return left;
        }

        private string ParseCompare()
        {
            var left = ParsePrimary();
            SkipWs();
            if (_i + 1 < _src.Length && _src[_i] == '<' && _src[_i + 1] == '>')
            {
                _i += 2;
                var right = ParsePrimary();
                return Eq(left, right) ? "FALSE" : "TRUE";
            }
            if (Peek() == '=')
            {
                _i++;
                var right = ParsePrimary();
                return Eq(left, right) ? "TRUE" : "FALSE";
            }
            return left;
        }

        private string ParsePrimary()
        {
            SkipWs();
            if (_i >= _src.Length) return string.Empty;

            char c = _src[_i];
            if (c == '"') return ReadQuoted('"');
            if (c == '\'') return ReadQuoted('\'');
            if (c == '[') return ReadBracketColumn();
            if (c == '(')
            {
                _i++;
                var inner = ParseInner();
                SkipWs();
                Expect(')');
                return inner;
            }
            if (char.IsDigit(c) || (c == '-' && _i + 1 < _src.Length && char.IsDigit(_src[_i + 1])))
                return ReadNumber();

            if (IsIdentStart(c))
            {
                var ident = ReadIdent();
                SkipWs();
                if (Peek() == '(')
                    return CallFunction(ident);
                return ResolveColumn(ident);
            }

            throw new FormulaException($"Simbol tidak dikenali di posisi {_i + 1}: '{c}'");
        }

        private string CallFunction(string name)
        {
            Expect('(');
            var args = new List<string>();
            SkipWs();
            if (Peek() != ')')
            {
                args.Add(ParseInner());
                SkipWs();
                while (Peek() == ',')
                {
                    _i++;
                    args.Add(ParseInner());
                    SkipWs();
                }
            }
            Expect(')');
            return EvalFunction(name, args);
        }

        private string EvalFunction(string name, List<string> args)
        {
            var fn = name.ToUpperInvariant();
            string A(int i) => i < args.Count ? args[i] : string.Empty;
            int N(int i, int fallback = 0)
            {
                if (i >= args.Count) return fallback;
                if (int.TryParse(args[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    return n;
                return fallback;
            }

            return fn switch
            {
                "UPPER" => A(0).ToUpperInvariant(),
                "LOWER" => A(0).ToLowerInvariant(),
                "PROPER" or "TITLE" => ExcelParserService.ToTitleCase(A(0)),
                "TRIM" => string.Join(' ', A(0).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
                "LEN" => A(0).Length.ToString(CultureInfo.InvariantCulture),
                "LEFT" => Left(A(0), Math.Max(0, N(1))),
                "RIGHT" => Right(A(0), Math.Max(0, N(1))),
                "MID" => Mid(A(0), N(1, 1), N(2, A(0).Length)),
                "CONCAT" or "CONCATENATE" => string.Concat(args),
                "SUBSTITUTE" => (A(0) ?? "").Replace(A(1), A(2)),
                "IF" => IsTruthy(A(0)) ? A(1) : A(2),
                "TEXT" => ExcelParserService.FormatIndonesianDate(A(0), string.IsNullOrWhiteSpace(A(1)) ? "Full" : A(1)),
                "TTL" => BuildTtl(A(0), A(1)),
                "JKNORM" => YearbookLayoutService.NormalizeGender(A(0)),
                _ => throw new FormulaException($"Fungsi tidak dikenali: {name}")
            };
        }

        private static string BuildTtl(string tempat, string tgl)
        {
            tempat = tempat.Trim();
            tgl = tgl.Trim();
            if (tempat.Length == 0) return tgl;
            if (tgl.Length == 0) return tempat;
            return $"{tempat}, {tgl}";
        }

        private static bool IsTruthy(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return false;
            if (v.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return false;
            if (v.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return true;
            if (v == "0") return false;
            return true;
        }

        private static bool Eq(string a, string b) =>
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string Left(string s, int n)
        {
            if (n <= 0) return string.Empty;
            return n >= s.Length ? s : s[..n];
        }

        private static string Right(string s, int n)
        {
            if (n <= 0) return string.Empty;
            return n >= s.Length ? s : s[^n..];
        }

        private static string Mid(string s, int start1, int len)
        {
            if (len <= 0 || s.Length == 0) return string.Empty;
            int start = Math.Max(1, start1) - 1;
            if (start >= s.Length) return string.Empty;
            int take = Math.Min(len, s.Length - start);
            return s.Substring(start, take);
        }

        private string ResolveColumn(string raw)
        {
            var name = MatchColumn(raw);
            if (name == null)
                throw new FormulaException($"Kolom '{raw}' tidak ada. Pakai [NAMA KOLOM] jika namanya ada spasi.");
            return _cells.TryGetValue(name, out var v) ? v ?? string.Empty : string.Empty;
        }

        private string? MatchColumn(string raw)
        {
            var t = raw.Trim();
            foreach (var c in _columns)
            {
                if (string.Equals(c, t, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }

        private string ReadBracketColumn()
        {
            _i++; // [
            var sb = new StringBuilder();
            while (_i < _src.Length && _src[_i] != ']')
            {
                sb.Append(_src[_i]);
                _i++;
            }
            Expect(']');
            return ResolveColumn(sb.ToString());
        }

        private string ReadQuoted(char q)
        {
            _i++;
            var sb = new StringBuilder();
            while (_i < _src.Length)
            {
                char c = _src[_i++];
                if (c == q)
                {
                    if (_i < _src.Length && _src[_i] == q)
                    {
                        sb.Append(q);
                        _i++;
                        continue;
                    }
                    return sb.ToString();
                }
                sb.Append(c);
            }
            throw new FormulaException("Tanda kutip tidak ditutup.");
        }

        private string ReadNumber()
        {
            int start = _i;
            if (Peek() == '-') _i++;
            while (_i < _src.Length && (char.IsDigit(_src[_i]) || _src[_i] == '.'))
                _i++;
            return _src[start.._i];
        }

        private string ReadIdent()
        {
            int start = _i;
            while (_i < _src.Length && (char.IsLetterOrDigit(_src[_i]) || _src[_i] == '_'))
                _i++;
            return _src[start.._i];
        }

        private void SkipWs()
        {
            while (_i < _src.Length && char.IsWhiteSpace(_src[_i])) _i++;
        }

        private char Peek()
        {
            SkipWs();
            return _i < _src.Length ? _src[_i] : '\0';
        }

        private void Expect(char c)
        {
            SkipWs();
            if (_i >= _src.Length || _src[_i] != c)
                throw new FormulaException($"Diharapkan '{c}'.");
            _i++;
        }

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    }
}
