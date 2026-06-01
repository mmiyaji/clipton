using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

var root = FindRepoRoot();
var output = Path.Combine(root, "artifacts", "store");
Directory.CreateDirectory(output);

var iconPath = Path.Combine(root, "src", "Clipton.WinUI", "Assets", "Clipton.png");
using var icon = File.Exists(iconPath) ? Image.FromFile(iconPath) : null;

var sets = new[]
{
    new Shot("01-quick-menu", "Quick menu", "Paste recent clips without leaving the keyboard.", "Fast access", "クイックメニュー", "キーボードだけで履歴を選び、すぐ貼り付け。", "高速アクセス", DrawQuickMenu),
    new Shot("02-history", "Searchable history", "Find recent text, images, files, and protected entries quickly.", "History", "検索できる履歴", "テキスト、画像、ファイル、秘匿済み履歴をすばやく確認。", "履歴", DrawHistory),
    new Shot("03-snippets", "Organized snippets", "Keep reusable messages in folders and mask matched clipboard text.", "Snippets", "登録メッセージを整理", "よく使う文面をフォルダ化し、一致した履歴は登録名で表示。", "登録単語", DrawSnippets),
    new Shot("04-settings", "Store-ready controls", "Configure theme, language, hotkeys, startup, and history retention.", "Settings", "配信向けの設定", "テーマ、言語、ホットキー、スタートアップ、保存件数を調整。", "設定", DrawSettings),
    new Shot("05-privacy", "Local-first privacy", "Encrypted local storage, sensitive masking, and no cloud sync by default.", "Privacy", "プライバシー保護", "暗号化保存、機密マスク、クラウド同期なしを前提に設計。", "プライバシー", DrawPrivacy)
};

foreach (var shot in sets)
{
    Render(shot, Locale.En, Path.Combine(output, shot.Name + "-en.png"));
    Render(shot, Locale.Ja, Path.Combine(output, shot.Name + "-ja.png"));
}

Console.WriteLine($"Generated {sets.Length * 2} store screenshots in {output}");

void Render(Shot shot, Locale locale, string path)
{
    using var bitmap = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bitmap);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

    using var background = new LinearGradientBrush(new Rectangle(0, 0, 1920, 1080), Color.FromArgb(18, 20, 23), Color.FromArgb(32, 36, 42), 35);
    g.FillRectangle(background, 0, 0, 1920, 1080);
    FillRound(g, new RectangleF(118, 92, 1684, 896), 34, Color.FromArgb(36, 39, 45));
    FillRound(g, new RectangleF(150, 124, 1620, 832), 28, Color.FromArgb(28, 31, 36));

    if (icon is not null)
    {
        g.DrawImage(icon, new Rectangle(198, 170, 74, 74));
    }

    var isJa = locale == Locale.Ja;
    using var brand = FontOf(isJa, 32, FontStyle.Bold);
    using var title = FontOf(isJa, isJa ? 62 : 74, FontStyle.Bold);
    using var subtitle = FontOf(isJa, 28, FontStyle.Regular);
    using var badge = FontOf(isJa, 23, FontStyle.Bold);

    DrawText(g, "Clipton", brand, Color.White, new RectangleF(292, 178, 360, 52));
    var tag = isJa ? shot.TagJa : shot.TagEn;
    FillRound(g, new RectangleF(198, 284, 250, 54), 27, Color.FromArgb(54, 94, 120));
    DrawText(g, tag, badge, Color.FromArgb(220, 245, 255), new RectangleF(228, 295, 210, 34));

    DrawText(g, isJa ? shot.TitleJa : shot.TitleEn, title, Color.White, new RectangleF(198, 370, 610, 186));
    DrawText(g, isJa ? shot.SubtitleJa : shot.SubtitleEn, subtitle, Color.FromArgb(205, 214, 225), new RectangleF(202, 584, 572, 130));

    var frame = new RectangleF(808, 152, 826, 776);
    DrawWindowFrame(g, frame);
    shot.Draw(g, frame, isJa);

    bitmap.Save(path, ImageFormat.Png);
}

void DrawQuickMenu(Graphics g, RectangleF frame, bool isJa)
{
    var menu = new RectangleF(frame.X + 150, frame.Y + 96, 526, 584);
    FillRound(g, menu, 13, Color.FromArgb(46, 49, 54));
    DrawRound(g, menu, 13, Color.FromArgb(70, 75, 82), 1);
    DrawSearch(g, new RectangleF(menu.X + 22, menu.Y + 20, menu.Width - 44, 44), isJa ? "履歴を検索" : "Search history");

    var rows = isJa
        ? new[] { ("Aa", "プロジェクト共有リンク", "たった今"), ("▣", "スクリーンショット 2026-05-21", "1分前"), ("※", "※※※ ※※※ ※※※", "機密"), ("Folder", "登録メッセージ", "3件"), ("Aa", "会議メモの下書き", "10分前") }
        : new[] { ("Aa", "Project share link", "Now"), ("▣", "Screenshot 2026-05-21", "1 min"), ("※", "※※※ ※※※ ※※※", "Masked"), ("Folder", "Snippets", "3 items"), ("Aa", "Meeting note draft", "10 min") };

    using var rowFont = FontOf(isJa, 22, FontStyle.Regular);
    using var small = FontOf(isJa, 16, FontStyle.Regular);
    var y = menu.Y + 86;
    for (var i = 0; i < rows.Length; i++)
    {
        var selected = i == 0;
        var rect = new RectangleF(menu.X + 10, y, menu.Width - 20, 74);
        FillRound(g, rect, 9, selected ? Color.FromArgb(74, 78, 86) : Color.FromArgb(0, 0, 0, 0));
        var glyphRect = new RectangleF(rect.X + 16, rect.Y + 17, 38, 38);
        if (rows[i].Item1 == "Folder")
        {
            DrawFolderGlyph(g, glyphRect);
        }
        else
        {
            DrawGlyph(g, rows[i].Item1, glyphRect, isJa);
        }
        DrawText(g, rows[i].Item2, rowFont, Color.White, new RectangleF(rect.X + 70, rect.Y + 14, 330, 29));
        DrawText(g, rows[i].Item3, small, Color.FromArgb(174, 183, 194), new RectangleF(rect.Right - 98, rect.Y + 18, 82, 24), ContentAlignment.TopRight);
        y += 82;
    }

    DrawKeyHint(g, frame, isJa ? "↑↓ 選択  Enter 貼り付け  T テキスト貼り付け" : "↑↓ Select  Enter Paste  T Plain text");
}

void DrawHistory(Graphics g, RectangleF frame, bool isJa)
{
    DrawAppShell(g, frame, isJa ? "履歴" : "History");
    DrawSearch(g, new RectangleF(frame.X + 206, frame.Y + 86, 500, 44), isJa ? "履歴を検索" : "Search clips");
    var labels = isJa
        ? new[] { "プロジェクト共有リンク", "※※※ ※※※ ※※※", "画像 1280 x 720", "見積書.pdf", "登録: 返信テンプレート" }
        : new[] { "Project share link", "※※※ ※※※ ※※※", "Image 1280 x 720", "Estimate.pdf", "Snippet: Reply template" };
    for (var i = 0; i < labels.Length; i++)
    {
        DrawHistoryRow(g, new RectangleF(frame.X + 206, frame.Y + 154 + i * 92, 446, 72), labels[i], i);
    }
    FillRound(g, new RectangleF(frame.X + 676, frame.Y + 154, 118, 118), 12, Color.FromArgb(58, 65, 73));
    using var f = FontOf(isJa, 22, FontStyle.Bold);
    DrawText(g, isJa ? "画像プレビュー" : "Image preview", f, Color.White, new RectangleF(frame.X + 690, frame.Y + 286, 180, 34));
    DrawMountain(g, new RectangleF(frame.X + 700, frame.Y + 182, 70, 62));
}

void DrawSnippets(Graphics g, RectangleF frame, bool isJa)
{
    DrawAppShell(g, frame, isJa ? "登録メッセージ" : "Snippets");
    var folders = isJa ? new[] { "返信", "開発", "サポート", "請求" } : new[] { "Replies", "Engineering", "Support", "Billing" };
    using var font = FontOf(isJa, 19, FontStyle.Regular);
    for (var i = 0; i < folders.Length; i++)
    {
        var r = new RectangleF(frame.X + 204, frame.Y + 94 + i * 60, 214, 46);
        FillRound(g, r, 8, i == 0 ? Color.FromArgb(66, 72, 82) : Color.FromArgb(38, 42, 48));
        DrawFolderIcon(g, new RectangleF(r.X + 16, r.Y + 14, 28, 20));
        DrawText(g, folders[i], font, Color.White, new RectangleF(r.X + 52, r.Y + 11, 140, 24));
    }
    var editor = new RectangleF(frame.X + 448, frame.Y + 94, 326, 430);
    FillRound(g, editor, 14, Color.FromArgb(38, 42, 48));
    using var title = FontOf(isJa, 24, FontStyle.Bold);
    using var body = FontOf(isJa, 18, FontStyle.Regular);
    DrawText(g, isJa ? "登録名: 返信テンプレート" : "Name: Reply template", title, Color.White, new RectangleF(editor.X + 24, editor.Y + 24, 280, 34));
    DrawText(g, isJa ? "一致した履歴は登録名で表示され、本文はマスクされます。" : "Matched clipboard text is shown by snippet name and the body is masked.", body, Color.FromArgb(205, 214, 225), new RectangleF(editor.X + 24, editor.Y + 86, 260, 110));
    FillRound(g, new RectangleF(editor.X + 24, editor.Y + 220, 252, 86), 10, Color.FromArgb(30, 33, 38));
    DrawText(g, "※※※ ※※※ ※※※", title, Color.FromArgb(238, 238, 238), new RectangleF(editor.X + 44, editor.Y + 247, 220, 34));
}

void DrawSettings(Graphics g, RectangleF frame, bool isJa)
{
    DrawAppShell(g, frame, isJa ? "設定" : "Settings");
    var items = isJa
        ? new[] { ("テーマ", "システム / ライト / ダーク"), ("言語", "システム / 日本語 / English"), ("グローバルホットキー", "Ctrl + Shift + V"), ("保存履歴数", "1,000 件"), ("スタートアップ", "Windows起動時に開始") }
        : new[] { ("Theme", "System / Light / Dark"), ("Language", "System / Japanese / English"), ("Global hotkey", "Ctrl + Shift + V"), ("Saved history limit", "1,000 items"), ("Startup", "Start with Windows") };
    for (var i = 0; i < items.Length; i++)
    {
        DrawSettingCard(g, new RectangleF(frame.X + 206, frame.Y + 94 + i * 84, 568, 64), items[i].Item1, items[i].Item2, isJa);
    }
}

void DrawPrivacy(Graphics g, RectangleF frame, bool isJa)
{
    DrawAppShell(g, frame, isJa ? "プライバシー" : "Privacy");
    var cards = isJa
        ? new[] { ("※", "機密っぽい内容を自動マスク"), ("🔒", "履歴はローカルで暗号化保存"), ("◫", "複数ファイルで差分永続化"), ("☁", "標準ではクラウド送信なし") }
        : new[] { ("※", "Automatic sensitive masking"), ("🔒", "Encrypted local history"), ("◫", "Segmented persistence files"), ("☁", "No cloud sync by default") };
    for (var i = 0; i < cards.Length; i++)
    {
        var x = frame.X + 206 + (i % 2) * 292;
        var y = frame.Y + 118 + (i / 2) * 158;
        DrawPrivacyCard(g, new RectangleF(x, y, 258, 126), cards[i].Item1, cards[i].Item2, isJa);
    }
    DrawKeyHint(g, frame, isJa ? "ローカル保存先: history\\manifest.dat / base.dat / delta.dat" : "Local storage: history\\manifest.dat / base.dat / delta.dat");
}

void DrawWindowFrame(Graphics g, RectangleF frame)
{
    FillRound(g, frame, 24, Color.FromArgb(22, 24, 28));
    DrawRound(g, frame, 24, Color.FromArgb(76, 82, 90), 1);
    FillRound(g, new RectangleF(frame.X + 16, frame.Y + 16, frame.Width - 32, frame.Height - 32), 18, Color.FromArgb(31, 34, 39));
}

void DrawAppShell(Graphics g, RectangleF frame, string heading)
{
    FillRound(g, new RectangleF(frame.X + 42, frame.Y + 58, 126, frame.Height - 116), 16, Color.FromArgb(38, 42, 48));
    FillRound(g, new RectangleF(frame.X + 190, frame.Y + 58, frame.Width - 232, frame.Height - 116), 16, Color.FromArgb(32, 35, 40));
    using var h = FontOf(true, 28, FontStyle.Bold);
    DrawText(g, heading, h, Color.White, new RectangleF(frame.X + 206, frame.Y + 50, 360, 42));
    var nav = new[] { "⌘", "◷", "✦", "⚙" };
    using var navFont = FontOf(false, 22, FontStyle.Regular);
    for (var i = 0; i < nav.Length; i++)
    {
        var r = new RectangleF(frame.X + 68, frame.Y + 92 + i * 66, 74, 48);
        FillRound(g, r, 10, i == 0 ? Color.FromArgb(62, 69, 78) : Color.FromArgb(0, 0, 0, 0));
        DrawText(g, nav[i], navFont, Color.FromArgb(209, 222, 235), r, ContentAlignment.MiddleCenter);
    }
}

void DrawSearch(Graphics g, RectangleF rect, string text)
{
    FillRound(g, rect, 10, Color.FromArgb(37, 40, 46));
    DrawRound(g, rect, 10, Color.FromArgb(70, 75, 82), 1);
    using var font = FontOf(true, 17, FontStyle.Regular);
    DrawText(g, "⌕", font, Color.FromArgb(180, 190, 202), new RectangleF(rect.X + 16, rect.Y + 12, 26, 22));
    DrawText(g, text, font, Color.FromArgb(180, 190, 202), new RectangleF(rect.X + 48, rect.Y + 11, rect.Width - 60, 24));
}

void DrawHistoryRow(Graphics g, RectangleF rect, string label, int index)
{
    FillRound(g, rect, 10, index == 0 ? Color.FromArgb(58, 64, 73) : Color.FromArgb(38, 42, 48));
    DrawGlyph(g, index == 2 ? "▣" : index == 1 ? "※" : "Aa", new RectangleF(rect.X + 14, rect.Y + 17, 38, 38), true);
    using var font = FontOf(true, 19, FontStyle.Regular);
    DrawText(g, label, font, Color.White, new RectangleF(rect.X + 66, rect.Y + 16, rect.Width - 88, 28));
}

void DrawSettingCard(Graphics g, RectangleF rect, string title, string value, bool isJa)
{
    FillRound(g, rect, 10, Color.FromArgb(38, 42, 48));
    using var t = FontOf(isJa, 19, FontStyle.Bold);
    using var v = FontOf(isJa, 17, FontStyle.Regular);
    DrawText(g, title, t, Color.White, new RectangleF(rect.X + 20, rect.Y + 10, 250, 24));
    DrawText(g, value, v, Color.FromArgb(191, 202, 214), new RectangleF(rect.X + 20, rect.Y + 36, 390, 22));
    FillRound(g, new RectangleF(rect.Right - 78, rect.Y + 19, 44, 24), 12, Color.FromArgb(71, 132, 164));
}

void DrawPrivacyCard(Graphics g, RectangleF rect, string glyph, string label, bool isJa)
{
    FillRound(g, rect, 14, Color.FromArgb(38, 42, 48));
    using var iconFont = FontOf(isJa, 30, FontStyle.Bold);
    using var textFont = FontOf(isJa, 20, FontStyle.Bold);
    DrawText(g, glyph, iconFont, Color.FromArgb(106, 204, 255), new RectangleF(rect.X + 22, rect.Y + 18, 54, 40), ContentAlignment.MiddleCenter);
    DrawText(g, label, textFont, Color.White, new RectangleF(rect.X + 82, rect.Y + 25, 142, 68));
}

void DrawGlyph(Graphics g, string glyph, RectangleF rect, bool isJa)
{
    FillRound(g, rect, 8, Color.FromArgb(52, 58, 66));
    using var font = FontOf(isJa, 15, FontStyle.Bold);
    DrawText(g, glyph, font, Color.FromArgb(112, 210, 255), rect, ContentAlignment.MiddleCenter);
}

void DrawMountain(Graphics g, RectangleF rect)
{
    using var pen = new Pen(Color.FromArgb(105, 204, 255), 3);
    g.DrawEllipse(pen, rect.X + 50, rect.Y + 5, 12, 12);
    var points = new[] { new PointF(rect.X + 4, rect.Bottom - 8), new PointF(rect.X + 24, rect.Y + 30), new PointF(rect.X + 40, rect.Bottom - 8) };
    g.DrawLines(pen, points);
    var points2 = new[] { new PointF(rect.X + 32, rect.Bottom - 8), new PointF(rect.X + 50, rect.Y + 22), new PointF(rect.Right - 4, rect.Bottom - 8) };
    g.DrawLines(pen, points2);
}

void DrawFolderIcon(Graphics g, RectangleF rect)
{
    using var pen = new Pen(Color.FromArgb(105, 204, 255), 2);
    using var brush = new SolidBrush(Color.FromArgb(38, 82, 103));
    var path = new GraphicsPath();
    path.AddLine(rect.X, rect.Y + 6, rect.X + 9, rect.Y + 6);
    path.AddLine(rect.X + 12, rect.Y, rect.X + 28, rect.Y);
    path.AddLine(rect.X + 28, rect.Bottom, rect.X, rect.Bottom);
    path.CloseFigure();
    g.FillPath(brush, path);
    g.DrawPath(pen, path);
}

void DrawFolderGlyph(Graphics g, RectangleF rect)
{
    FillRound(g, rect, 8, Color.FromArgb(52, 58, 66));
    DrawFolderIcon(g, new RectangleF(rect.X + 7, rect.Y + 10, 24, 18));
}

void DrawKeyHint(Graphics g, RectangleF frame, string text)
{
    using var font = FontOf(true, 18, FontStyle.Regular);
    FillRound(g, new RectangleF(frame.X + 98, frame.Bottom - 82, frame.Width - 196, 42), 21, Color.FromArgb(37, 40, 46));
    DrawText(g, text, font, Color.FromArgb(202, 213, 225), new RectangleF(frame.X + 118, frame.Bottom - 73, frame.Width - 236, 24), ContentAlignment.MiddleCenter);
}

void FillRound(Graphics g, RectangleF rect, float radius, Color color)
{
    using var brush = new SolidBrush(color);
    using var path = Rounded(rect, radius);
    g.FillPath(brush, path);
}

void DrawRound(Graphics g, RectangleF rect, float radius, Color color, float width)
{
    using var pen = new Pen(color, width);
    using var path = Rounded(rect, radius);
    g.DrawPath(pen, path);
}

GraphicsPath Rounded(RectangleF rect, float radius)
{
    var path = new GraphicsPath();
    var d = radius * 2;
    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
    path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, ContentAlignment align = ContentAlignment.TopLeft)
{
    using var brush = new SolidBrush(color);
    using var format = new StringFormat { Trimming = StringTrimming.EllipsisWord };
    if (align is ContentAlignment.MiddleCenter or ContentAlignment.TopCenter)
    {
        format.Alignment = StringAlignment.Center;
    }
    else if (align is ContentAlignment.TopRight or ContentAlignment.MiddleRight)
    {
        format.Alignment = StringAlignment.Far;
    }

    if (align is ContentAlignment.MiddleCenter or ContentAlignment.MiddleLeft or ContentAlignment.MiddleRight)
    {
        format.LineAlignment = StringAlignment.Center;
    }

    g.DrawString(text, font, brush, rect, format);
}

Font FontOf(bool isJa, float size, FontStyle style) => new(isJa ? "Yu Gothic UI" : "Segoe UI", size, style, GraphicsUnit.Pixel);

string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Clipton.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

internal enum Locale
{
    En,
    Ja
}

internal sealed record Shot(
    string Name,
    string TitleEn,
    string SubtitleEn,
    string TagEn,
    string TitleJa,
    string SubtitleJa,
    string TagJa,
    Action<Graphics, RectangleF, bool> Draw);
