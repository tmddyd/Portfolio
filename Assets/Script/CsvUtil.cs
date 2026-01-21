using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class CsvUtil
{
    // 기존: 한 줄 파싱(유지)
    public static List<string> ParseLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString().Trim());
        return result;
    }

    // 추가: CSV 전체(text) 파싱(권장)
    public static List<string[]> Parse(string csv)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(csv)) return rows;

        csv = csv.Trim('\uFEFF'); // BOM 제거

        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (!inQuotes)
            {
                if (c == ',')
                {
                    row.Add(field.ToString().Trim());
                    field.Clear();
                    continue;
                }

                if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                        i++;

                    row.Add(field.ToString().Trim());
                    field.Clear();

                    // 완전 빈 줄은 스킵
                    if (!IsRowAllEmpty(row))
                        rows.Add(row.ToArray());

                    row.Clear();
                    continue;
                }
            }

            field.Append(c);
        }

        // 마지막 행 처리
        row.Add(field.ToString().Trim());
        if (!IsRowAllEmpty(row))
            rows.Add(row.ToArray());

        return rows;
    }

    private static bool IsRowAllEmpty(List<string> row)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(row[i]))
                return false;
        }
        return true;
    }

    // 헤더 정규화(권장): 공백 제거 + 소문자
    public static string NormalizeHeader(string header)
        => (header ?? "").Trim().Replace(" ", "").Replace("\t", "").ToLowerInvariant();

    public static Dictionary<string, int> BuildHeaderMap(string[] headerRow)
    {
        var map = new Dictionary<string, int>();
        if (headerRow == null) return map;

        for (int i = 0; i < headerRow.Length; i++)
        {
            var key = NormalizeHeader(headerRow[i]);
            if (string.IsNullOrEmpty(key)) continue;
            if (!map.ContainsKey(key))
                map.Add(key, i);
        }
        return map;
    }

    public static bool TryGetCell(string[] row, Dictionary<string, int> headerMap, out string value, params string[] keys)
    {
        value = "";
        if (row == null || headerMap == null || keys == null) return false;

        foreach (var k in keys)
        {
            var key = NormalizeHeader(k);
            if (headerMap.TryGetValue(key, out int idx))
            {
                if (idx >= 0 && idx < row.Length)
                {
                    value = (row[idx] ?? "").Trim();
                    return true;
                }
            }
        }
        return false;
    }

    public static int ToInt(string s, int defaultValue = 0)
    {
        if (int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            return v;

        // "1.0" 같은 값도 들어오면 보정
        if (ToFloatTry(s, out float f))
            return (int)MathF.Round(f);

        return defaultValue;
    }

    public static float ToFloat(string s, float defaultValue = 0f)
    {
        if (ToFloatTry(s, out float v))
            return v;
        return defaultValue;
    }

    private static bool ToFloatTry(string s, out float v)
    {
        v = 0f;
        if (string.IsNullOrWhiteSpace(s)) return false;

        string t = s.Trim();
        // 0,15 형태 보정
        if (t.Contains(",") && !t.Contains("."))
            t = t.Replace(",", ".");

        return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
