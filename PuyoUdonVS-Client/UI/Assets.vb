Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Drawing.Text
Imports System.Reflection

''' <summary>
''' 画像・色・フォント。うどんの種類ごとのスプライトは表示サイズ（デバイスピクセル）ごとに
''' 作ってキャッシュし、毎フレームの拡大縮小を避ける。
''' </summary>
Public NotInheritable Class Assets
    Implements IDisposable

    ''' <summary>種類 1〜5 の画像ファイル名（1〜4=うどん、5=おじゃまそば）。</summary>
    Private Shared ReadOnly KindFiles As String() = {"", "bukkake", "nabeyaki", "curry", "kamatama", "kakesoba"}
    Public Shared ReadOnly KindNames As String() = {"", "ぶっかけ", "鍋焼き", "カレー", "釜玉", "おじゃまそば"}

    ''' <summary>種類ごとの下地の色（見分けやすくするため）。</summary>
    Public Shared ReadOnly KindColors As Color() = {
        Color.Transparent,
        Color.FromArgb(88, 186, 92),    ' ぶっかけ: ねぎの緑
        Color.FromArgb(232, 84, 96),    ' 鍋焼き: かまぼこの赤
        Color.FromArgb(84, 120, 226),   ' カレー: 青
        Color.FromArgb(246, 196, 48),   ' 釜玉: 卵の黄
        Color.FromArgb(120, 112, 104)}  ' そば: 灰

    Private ReadOnly _images As New Dictionary(Of String, Bitmap)
    Private ReadOnly _sprites As New Dictionary(Of (Integer, Integer), Bitmap)
    Private ReadOnly _plain As New Dictionary(Of (String, Integer), Bitmap)
    Public ReadOnly FontFamilyName As String

    Public Sub New()
        Dim asm = Assembly.GetExecutingAssembly()
        For Each name In {"bukkake", "nabeyaki", "curry", "kake", "kamatama", "kakesoba"}
            Using s = asm.GetManifestResourceStream($"PuyoUdonVS_Client.Images.{name}.png")
                If s Is Nothing Then Continue For
                Using tmp As New Bitmap(s)
                    Dim bmp As New Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppPArgb)
                    Using g = Graphics.FromImage(bmp)
                        g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height)
                    End Using
                    _images(name) = bmp
                End Using
            End Using
        Next
        FontFamilyName = PickFont("BIZ UDPGothic", "Yu Gothic UI", "Meiryo UI", "MS UI Gothic")
    End Sub

    Private Shared Function PickFont(ParamArray names As String()) As String
        Using fonts As New InstalledFontCollection()
            Dim installed = fonts.Families.Select(Function(f) f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            For Each n In names
                If installed.Contains(n) Then Return n
            Next
        End Using
        Return SystemFonts.DefaultFont.FontFamily.Name
    End Function

    Public Function MakeFont(sizePx As Single, Optional bold As Boolean = True) As Font
        Return New Font(FontFamilyName, sizePx, If(bold, FontStyle.Bold, FontStyle.Regular), GraphicsUnit.Pixel)
    End Function

    ''' <summary>下地つきのセル画像（盤面用）。kind: 1〜5、px: 一辺のデバイスピクセル。</summary>
    Public Function Cell(kind As Integer, px As Integer) As Bitmap
        px = Math.Max(4, px)
        Dim key = (kind, px)
        Dim bmp As Bitmap = Nothing
        If _sprites.TryGetValue(key, bmp) Then Return bmp
        bmp = New Bitmap(px, px, PixelFormat.Format32bppPArgb)
        Using g = Graphics.FromImage(bmp)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            Dim c = KindColors(kind)
            Dim r As New RectangleF(px * 0.03F, px * 0.03F, px * 0.94F, px * 0.94F)
            Using path As New GraphicsPath()
                path.AddEllipse(r)
                Using pb As New PathGradientBrush(path)
                    pb.CenterPoint = New PointF(px * 0.4F, px * 0.35F)
                    pb.CenterColor = Lighten(c, 0.55F)
                    pb.SurroundColors = {c}
                    g.FillPath(pb, path)
                End Using
                Using pen As New Pen(Darken(c, 0.35F), Math.Max(1.0F, px * 0.04F))
                    g.DrawPath(pen, path)
                End Using
            End Using
            Dim img = KindImage(kind)
            If img IsNot Nothing Then
                Dim box = px * 0.9F
                Dim sc = Math.Min(box / img.Width, box / img.Height)
                Dim w = img.Width * sc, h = img.Height * sc
                g.DrawImage(img, (px - w) / 2, (px - h) / 2 + px * 0.02F, w, h)
            End If
        End Using
        _sprites(key) = bmp
        Return bmp
    End Function

    ''' <summary>下地なしの画像（タイトルの飾りなど）。高さ px に合わせる。</summary>
    Public Function Plain(name As String, px As Integer) As Bitmap
        px = Math.Max(4, px)
        Dim key = (name, px)
        Dim bmp As Bitmap = Nothing
        If _plain.TryGetValue(key, bmp) Then Return bmp
        Dim img As Bitmap = Nothing
        If Not _images.TryGetValue(name, img) Then Return Nothing
        Dim w = Math.Max(1, CInt(img.Width * px / img.Height))
        bmp = New Bitmap(w, px, PixelFormat.Format32bppPArgb)
        Using g = Graphics.FromImage(bmp)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.DrawImage(img, 0, 0, w, px)
        End Using
        _plain(key) = bmp
        Return bmp
    End Function

    Public Function KindImage(kind As Integer) As Bitmap
        Dim img As Bitmap = Nothing
        If kind >= 1 AndAlso kind < KindFiles.Length AndAlso _images.TryGetValue(KindFiles(kind), img) Then Return img
        Return Nothing
    End Function

    ''' <summary>表示サイズが変わったらキャッシュを捨てる。</summary>
    Public Sub ClearCache()
        For Each b In _sprites.Values : b.Dispose() : Next
        For Each b In _plain.Values : b.Dispose() : Next
        _sprites.Clear()
        _plain.Clear()
    End Sub

    Public Shared Function Lighten(c As Color, t As Single) As Color
        Return Color.FromArgb(c.A, CInt(c.R + (255 - c.R) * t), CInt(c.G + (255 - c.G) * t), CInt(c.B + (255 - c.B) * t))
    End Function

    Public Shared Function Darken(c As Color, t As Single) As Color
        Return Color.FromArgb(c.A, CInt(c.R * (1 - t)), CInt(c.G * (1 - t)), CInt(c.B * (1 - t)))
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        ClearCache()
        For Each b In _images.Values : b.Dispose() : Next
        _images.Clear()
    End Sub
End Class
