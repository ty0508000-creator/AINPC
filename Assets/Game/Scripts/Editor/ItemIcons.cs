using UnityEngine;

/// <summary>
/// 장비 아이콘을 32×32 도트로 그린다. 부위마다 모양 하나, 단계마다 재질 색을 바꾸고
/// 마지막에 바깥선을 둘러 낡은 검 아이콘과 같은 느낌을 낸다. 좌표 원점은 왼쪽 아래.
/// </summary>
public static class ItemIcons
{
    const int Size = 32;
    static readonly Color32 Ink = Hex(0x1D1A17);

    public static Texture2D Draw(ItemCategory category, int tier)
    {
        var p = new Color32[Size * Size];
        if (category == ItemCategory.Weapon) Sword(p, tier);
        else if (category == ItemCategory.Armor) ArmorIcon(p, tier);
        else AccessoryIcon(p, tier);
        Outline(p);
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.SetPixels32(p);
        texture.Apply();
        return texture;
    }

    // ── 검 ─────────────────────────────────────────────────────

    static void Sword(Color32[] p, int tier)
    {
        // 날·밝은 쪽·어두운 쪽, 코등이, 손잡이, 보석
        Color32 blade, light, dark, guard, grip, gem;
        switch (tier)
        {
            case 2: blade = Hex(0x9AA3AD); light = Hex(0xD5DADF); dark = Hex(0x6B737C); guard = Hex(0x5A5F66); grip = Hex(0x6B4426); gem = Hex(0x5A5F66); break;
            case 3: blade = Hex(0x8FC6EA); light = Hex(0xE8F6FF); dark = Hex(0x4C8DB8); guard = Hex(0xC8D0DC); grip = Hex(0x2B3A67); gem = Hex(0x3B82F6); break;
            case 4: blade = Hex(0x3B3F4A); light = Hex(0x9AA0B4); dark = Hex(0x23252C); guard = Hex(0x8C6A2E); grip = Hex(0x2A1C2E); gem = Hex(0xA855F7); break;
            default: blade = Hex(0xFFD36B); light = Hex(0xFFF6D0); dark = Hex(0xF07A1E); guard = Hex(0xE8C15A); grip = Hex(0x7A1E14); gem = Hex(0xE53935); break;
        }
        if (tier == 5)   // 날 둘레의 불꽃
            for (int i = 11; i <= 27; i += 2)
            {
                Set(p, i - 2, i + 1, Hex(0xF57C00)); Set(p, i + 1, i - 2, Hex(0xF57C00));
                if (i % 4 == 1) { Set(p, i - 3, i + 2, Hex(0xE53935)); Set(p, i + 2, i - 3, Hex(0xE53935)); }
            }
        Line(p, 10, 10, 26, 26, blade);
        Line(p, 10, 11, 25, 26, light);
        Line(p, 11, 10, 26, 25, dark);
        if (tier >= 3) Line(p, 12, 12, 24, 24, tier == 4 ? light : dark);   // 홈
        Set(p, 27, 27, light);
        Line(p, 6, 13, 13, 6, guard);
        Line(p, 7, 13, 13, 7, guard);
        Line(p, 5, 5, 9, 9, grip);
        Line(p, 5, 6, 8, 9, grip);
        Disc(p, 4, 4, 1, guard);
        Set(p, 9, 9, gem); Set(p, 10, 9, gem);
    }

    // ── 갑옷 ───────────────────────────────────────────────────

    static void ArmorIcon(Color32[] p, int tier)
    {
        Color32 body, light, dark, trim;
        switch (tier)
        {
            case 1: body = Hex(0xC9B48A); light = Hex(0xE3D3AE); dark = Hex(0x9C8660); trim = Hex(0x6B4426); break;
            case 2: body = Hex(0x8B5A2B); light = Hex(0xB07A44); dark = Hex(0x5E3B1B); trim = Hex(0xE8C15A); break;
            case 3: body = Hex(0x9AA0A6); light = Hex(0xCBD0D5); dark = Hex(0x6A7076); trim = Hex(0x5A5F66); break;
            case 4: body = Hex(0xB8C0C8); light = Hex(0xE6EAEE); dark = Hex(0x7D858D); trim = Hex(0x8C6A2E); break;
            default: body = Hex(0x2FA38A); light = Hex(0x6FD8BE); dark = Hex(0x1C6B5A); trim = Hex(0xE8C15A); break;
        }
        // 몸통·어깨·짧은 소매(좌우 대칭)
        for (int y = 6; y <= 25; y++)
            for (int x = 5; x <= 15; x++)
            {
                bool shoulder = y >= 21 && x >= 6;
                bool sleeve = y >= 16 && y <= 23 && x >= 5 && x <= 8;
                bool torso = y <= 22 && x >= 9;
                bool neck = y >= 22 && x >= 13;
                if (!(shoulder || sleeve || torso) || neck) continue;
                Color32 c = body;
                if (tier == 1 && (y % 4 == 0) && x >= 9) c = dark;                          // 누빔
                if (tier == 2 && x == 11 && y % 2 == 0 && y < 20) c = light;                 // 바늘땀
                if (tier == 3 && (x + y) % 2 == 0) c = dark;                                 // 사슬 고리
                if (tier == 4 && y % 3 == 0 && x >= 9) c = dark;                             // 철편 이음
                if (tier == 5 && (x % 3 == 0 && y % 3 == 1 || x % 3 != 0 && y % 3 == 2 && (x + y) % 2 == 0)) c = dark;   // 비늘
                if (x == 9 && y < 21 || x == 6 && y >= 21) c = light;                        // 왼쪽 빛
                Mirror(p, x, y, c);
            }
        for (int x = 9; x <= 15; x++) { Mirror(p, x, 11, trim); Mirror(p, x, 10, trim); }   // 허리띠
        if (tier >= 2) { Set(p, 15, 10, light); Set(p, 16, 10, light); Set(p, 15, 11, light); Set(p, 16, 11, light); }   // 버클
        for (int x = 13; x <= 15; x++) Mirror(p, x, 21, trim);                              // 깃
        if (tier == 5) for (int y = 6; y <= 21; y++) { Mirror(p, 9, y, trim); }             // 금테
        for (int y = 12; y <= 20; y++) Set(p, 16, y, dark);                                  // 앞섶
    }

    // ── 장신구 ─────────────────────────────────────────────────

    static void AccessoryIcon(Color32[] p, int tier)
    {
        switch (tier)
        {
            case 1: Talisman(p, Hex(0xE2CF8C), Hex(0xC4AE68), Hex(0xB8322A), torn: true); break;
            case 2:
                Line(p, 16, 24, 16, 29, Hex(0xC62828));                                     // 매듭 끈
                Disc(p, 15, 28, 1, Hex(0xC62828)); Disc(p, 17, 28, 1, Hex(0xC62828));
                Ring(p, 16, 17, 7, 3, Hex(0x4FBF8F));
                Ring(p, 16, 17, 7, 1, Hex(0x8BE3BC));
                for (int x = 14; x <= 18; x += 2) Line(p, x, 3, x, 10, Hex(0xC62828));     // 술
                break;
            case 3:
                for (int a = 0; a < 360; a += 2)
                {
                    float r = a * Mathf.Deg2Rad;
                    for (int t = 0; t < 3; t++)
                        Set(p, Mathf.RoundToInt(16 + Mathf.Cos(r) * (10 - t)), Mathf.RoundToInt(15 + Mathf.Sin(r) * (7 - t)),
                            t == 0 ? Hex(0x8C96A6) : t == 1 ? Hex(0xC8D0DC) : Hex(0xF2F5F9));
                }
                foreach (int x in new[] { 11, 16, 21 }) Disc(p, x, 8 + (x == 16 ? 0 : 1), 1, Hex(0x3B82F6));
                break;
            case 4:
                for (int i = 0; i < 12; i++)
                {
                    float r = i * 30f * Mathf.Deg2Rad;
                    int x = Mathf.RoundToInt(16 + Mathf.Cos(r) * 9), y = Mathf.RoundToInt(17 + Mathf.Sin(r) * 9);
                    Disc(p, x, y, 2, Hex(0xBFE6FF)); Set(p, x - 1, y + 1, Hex(0xFFFFFF));
                }
                Disc(p, 16, 7, 2, Hex(0xE8A13A));
                for (int x = 15; x <= 17; x++) Line(p, x, 2, x, 5, Hex(0x7E57C2));
                break;
            default:
                for (int y = 2; y <= 29; y++)                                                // 빛무리
                    for (int x = 8; x <= 23; x++) Set(p, x, y, new Color32(255, 236, 150, (byte)(x == 8 || x == 23 || y == 2 || y == 29 ? 60 : 120)));
                Talisman(p, Hex(0xFFE27A), Hex(0xF2C14E), Hex(0x2563EB), torn: false);
                Disc(p, 16, 27, 1, Hex(0xFFFFFF));
                break;
        }
    }

    static void Talisman(Color32[] p, Color32 paper, Color32 edge, Color32 glyph, bool torn)
    {
        for (int y = 4; y <= 27; y++)
            for (int x = 11; x <= 20; x++)
            {
                if (torn && y >= 25 && x >= 18 + (27 - y)) continue;   // 찢긴 귀퉁이
                Set(p, x, y, x == 11 || y == 4 ? edge : paper);
            }
        Line(p, 13, 23, 18, 23, glyph);
        Line(p, 15, 7, 15, 22, glyph);
        Line(p, 16, 7, 16, 22, glyph);
        Line(p, 13, 18, 18, 18, glyph);
        Line(p, 13, 13, 18, 10, glyph);
        Disc(p, 15, 26 - (torn ? 1 : 0), 0, glyph);
    }

    // ── 그리기 도구 ─────────────────────────────────────────────

    static void Set(Color32[] p, int x, int y, Color32 c)
    {
        if (x >= 0 && x < Size && y >= 0 && y < Size) p[y * Size + x] = c;
    }

    static void Mirror(Color32[] p, int x, int y, Color32 c) { Set(p, x, y, c); Set(p, Size - 1 - x, y, c); }

    static void Line(Color32[] p, int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            Set(p, x0, y0, c);
            if (x0 == x1 && y0 == y1) return;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    static void Disc(Color32[] p, int cx, int cy, int r, Color32 c)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r + r) Set(p, cx + x, cy + y, c);
    }

    static void Ring(Color32[] p, int cx, int cy, int r, int thickness, Color32 c)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                int d = x * x + y * y;
                if (d <= r * r + r && d > (r - thickness) * (r - thickness) + (r - thickness)) Set(p, cx + x, cy + y, c);
            }
    }

    /// <summary>불투명한 칸과 맞닿은 빈칸을 바깥선 색으로 칠한다. 반투명 빛무리는 선을 두르지 않는다.</summary>
    static void Outline(Color32[] p)
    {
        var copy = (Color32[])p.Clone();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (copy[y * Size + x].a != 0) continue;
                bool edge = false;
                foreach (var (ox, oy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + ox, ny = y + oy;
                    if (nx >= 0 && nx < Size && ny >= 0 && ny < Size && copy[ny * Size + nx].a == 255) edge = true;
                }
                if (edge) p[y * Size + x] = Ink;
            }
    }

    static Color32 Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
}
