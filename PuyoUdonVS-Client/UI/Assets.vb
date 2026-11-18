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

    ''' <summary>種類ごとの色（消えたときの飛び散りエフェクト用。盤面のうどんには使わない）。</summary>
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
    Private ReadOnly _privFonts As New List(Of PrivateFontCollection)
    Private ReadOnly _privData As New List(Of IntPtr)
    ''' <summary>タイトル用フォント（埋め込みの nakamorikougei）。読み込めなければ Nothing。</summary>
    Public ReadOnly TitleFont As FontFamily
    ''' <summary>本文用フォント（埋め込みの umeboshi_zatumemo）。読み込めなければ Nothing。</summary>
    Public ReadOnly BodyFont As FontFamily

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
        TitleFont = LoadEmbeddedFont(asm, "nakamorikougei.ttf")
        BodyFont = LoadEmbeddedFont(asm, "umeboshi_zatumemo.ttf")
        FontFamilyName = PickFont("BIZ UDPGothic", "Yu Gothic UI", "Meiryo UI", "MS UI Gothic")
    End Sub

    <Runtime.InteropServices.DllImport("gdi32.dll")>
    Private Shared Function AddFontMemResourceEx(pbFont As IntPtr, cbFont As UInteger, pdv As IntPtr, ByRef pcFonts As UInteger) As IntPtr
    End Function

    Private Function LoadEmbeddedFont(asm As Assembly, fileName As String) As FontFamily
        Try
            Using s = asm.GetManifestResourceStream("PuyoUdonVS_Client.Fonts." & fileName)
                If s Is Nothing Then Return Nothing
                Dim data(CInt(s.Length) - 1) As Byte
                s.ReadExactly(data, 0, data.Length)
                Dim ptr = Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length)
                Runtime.InteropServices.Marshal.Copy(data, 0, ptr, data.Length)
                ' TextBox など GDI 描画のコントロールでも名前で使えるよう、プロセス専用フォントとしても登録する
                Dim n As UInteger = 0
                AddFontMemResourceEx(ptr, CUInt(data.Length), IntPtr.Zero, n)
                Dim pfc As New PrivateFontCollection()
                pfc.AddMemoryFont(ptr, data.Length)
                _privData.Add(ptr)
                _privFonts.Add(pfc)
                Return pfc.Families(0)
            End Using
        Catch
            Return Nothing
        End Try
    End Function

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
        Return New Font(BodyFamily, sizePx, BodyStyle(bold), GraphicsUnit.Pixel)
    End Function

    ''' <summary>本文用の FontFamily（umeboshi_zatumemo、なければ代替フォント）。</summary>
    Public ReadOnly Property BodyFamily As FontFamily
        Get
            If BodyFont IsNot Nothing Then Return BodyFont
            If _fallback Is Nothing Then _fallback = New FontFamily(FontFamilyName)
            Return _fallback
        End Get
    End Property
    Private _fallback As FontFamily

    ''' <summary>そのフォントにある太さだけを返す（umeboshi_zatumemo に太字がなくても落ちないように）。</summary>
    Public Function BodyStyle(bold As Boolean) As FontStyle
        If bold AndAlso BodyFamily.IsStyleAvailable(FontStyle.Bold) Then Return FontStyle.Bold
        If BodyFamily.IsStyleAvailable(FontStyle.Regular) Then Return FontStyle.Regular
        Return CType([Enum].GetValues(GetType(FontStyle)).Cast(Of FontStyle)().First(Function(st) BodyFamily.IsStyleAvailable(st)), FontStyle)
    End Function

    ''' <summary>盤面用のセル画像（下地なし）。kind: 1〜5、px: 一辺のデバイスピクセル。</summary>
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
            ' 色の下地は付けず、うどんと器の画像をそのまま使う（あえて見分けにくくする）
            Dim img = KindImage(kind)
            If img IsNot Nothing Then
                Dim box = px * 0.96F
                Dim sc = Math.Min(box / img.Width, box / img.Height)
                Dim w = img.Width * sc, h = img.Height * sc
                g.DrawImage(img, (px - w) / 2, (px - h) / 2, w, h)
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
        For Each p In _privFonts : p.Dispose() : Next
        For Each p In _privData : Runtime.InteropServices.Marshal.FreeCoTaskMem(p) : Next
        For Each b In _images.Values : b.Dispose() : Next
        _images.Clear()
    End Sub
End Class
