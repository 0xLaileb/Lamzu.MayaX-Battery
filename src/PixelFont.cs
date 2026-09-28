namespace MayaXBattery;

/// <summary>
/// Hand-drawn bitmap digits. At 16 px an outline font resolves to grey mush no matter how it is
/// hinted, so small icons get glyphs snapped to whole pixels instead. Two widths: the 5x7 face
/// for one or two characters, the 3x5 face for when three have to fit (i.e. "100").
/// </summary>
internal static class PixelFont
{
    internal sealed record Face(int Width, int Height, IReadOnlyDictionary<char, string[]> Glyphs);

    static readonly string[] D5x7_0 = { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." };
    static readonly string[] D5x7_1 = { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." };
    static readonly string[] D5x7_2 = { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" };
    static readonly string[] D5x7_3 = { ".###.", "#...#", "....#", "..##.", "....#", "#...#", ".###." };
    static readonly string[] D5x7_4 = { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." };
    static readonly string[] D5x7_5 = { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." };
    static readonly string[] D5x7_6 = { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." };
    static readonly string[] D5x7_7 = { "#####", "....#", "...#.", "..#..", "..#..", ".#...", ".#..." };
    static readonly string[] D5x7_8 = { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." };
    static readonly string[] D5x7_9 = { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." };
    static readonly string[] D5x7_Q = { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." };

    static readonly string[] D3x5_0 = { "###", "#.#", "#.#", "#.#", "###" };
    static readonly string[] D3x5_1 = { ".#.", "##.", ".#.", ".#.", "###" };
    static readonly string[] D3x5_2 = { "###", "..#", "###", "#..", "###" };
    static readonly string[] D3x5_3 = { "###", "..#", ".##", "..#", "###" };
    static readonly string[] D3x5_4 = { "#.#", "#.#", "###", "..#", "..#" };
    static readonly string[] D3x5_5 = { "###", "#..", "###", "..#", "###" };
    static readonly string[] D3x5_6 = { "###", "#..", "###", "#.#", "###" };
    static readonly string[] D3x5_7 = { "###", "..#", "..#", ".#.", ".#." };
    static readonly string[] D3x5_8 = { "###", "#.#", "###", "#.#", "###" };
    static readonly string[] D3x5_9 = { "###", "#.#", "###", "..#", "###" };
    static readonly string[] D3x5_Q = { "###", "..#", ".##", "...", ".#." };

    internal static readonly Face Large = new(5, 7, new Dictionary<char, string[]>
    {
        ['0'] = D5x7_0, ['1'] = D5x7_1, ['2'] = D5x7_2, ['3'] = D5x7_3, ['4'] = D5x7_4,
        ['5'] = D5x7_5, ['6'] = D5x7_6, ['7'] = D5x7_7, ['8'] = D5x7_8, ['9'] = D5x7_9,
        ['?'] = D5x7_Q,
    });

    internal static readonly Face Tall = new(3, 7, new Dictionary<char, string[]>
    {
        ['1'] = new[] { ".#.", "##.", ".#.", ".#.", ".#.", ".#.", "###" },
        ['0'] = new[] { "###", "#.#", "#.#", "#.#", "#.#", "#.#", "###" },
    });

    internal static readonly Face Small = new(3, 5, new Dictionary<char, string[]>
    {
        ['0'] = D3x5_0, ['1'] = D3x5_1, ['2'] = D3x5_2, ['3'] = D3x5_3, ['4'] = D3x5_4,
        ['5'] = D3x5_5, ['6'] = D3x5_6, ['7'] = D3x5_7, ['8'] = D3x5_8, ['9'] = D3x5_9,
        ['?'] = D3x5_Q,
    });

    internal static bool Supports(Face face, string text) => text.All(face.Glyphs.ContainsKey);

    /// <summary>Width in glyph units for a string, with one unit of tracking between characters.</summary>
    internal static int MeasureUnits(Face face, string text) =>
        text.Length * face.Width + (text.Length - 1);
}

