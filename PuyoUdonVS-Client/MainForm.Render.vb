Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Drawing.Text

' 描画・演出・マウス操作
Partial Public Class MainForm

    ' ---- レイアウト（論理座標） ----
    Private Const Cell As Integer = 40
    Private Const MyBoardX As Integer = 48
    Private Const OppBoardX As Integer = 672
    Private Const BoardY As Integer = 106
    Private Const BoardW As Integer = Cell * Field.Cols
    Private Const BoardH As Integer = Cell * Field.Rows

    ' ---- 色 ----
    Private Shared ReadOnly ColBrownDark As Color = Color.FromArgb(74, 44, 22)
    Private Shared ReadOnly ColBrown As Color = Color.FromArgb(120, 72, 34)
    Private Shared ReadOnly ColCream As Color = Color.FromArgb(255, 248, 228)
    Private Shared ReadOnly ColDashi As Color = Color.FromArgb(240, 190, 90)
    Private Shared ReadOnly ColRed As Color = Color.FromArgb(214, 48, 40)
    Private Shared ReadOnly ColBoardBg As Color = Color.FromArgb(34, 42, 58)

    ' ---- 描画用の状態 ----
    Private _scale As Single = 1
    Private _ox As Single, _oy As Single
    Private _bg As Bitmap
    Private _bgSize As Size
    Private _spritePx As Integer
    Private ReadOnly _fonts As New Dictionary(Of (Single, Boolean), Font)
    Private ReadOnly _buttons As New List(Of UiButton)
    Private _mouse As PointF = New PointF(-1, -1)
    Private _goFrames As Integer
    Private _toast As String = ""
    Private _toastFrames As Integer
    Private _baseMatrix As Matrix
    Private _offX As Single, _offY As Single
    Private _matchStatic As Bitmap
    Private _matchStaticKey As String = ""
    Private _showFps As Boolean
    Private _fps As Integer
    Private _fpsCount As Integer
    Private _fpsStartMs As Double

    Private Sub CountFps()
        _fpsCount += 1
        Dim now = _clock.Elapsed.TotalMilliseconds
        If now - _fpsStartMs >= 1000 Then
            _fps = CInt(Math.Round(_fpsCount * 1000 / (now - _fpsStartMs)))
            _fpsCount = 0
            _fpsStartMs = now
        End If
    End Sub

    Private Class UiButton
        Public Rect As RectangleF
        Public Label As String
        Public Enabled As Boolean = True
        Public Action As Action
        Public Primary As Boolean
    End Class

    ' ---- 演出 ----
    Private Class Particle
        Public X, Y, Vx, Vy As Single
        Public Life, MaxLife As Integer
        Public Color As Color
        Public Size As Single
        Public Strand As Boolean
        Public Angle As Single
    End Class

    Private Class FloatText
        Public Text As String
        Public X, Y As Single
        Public Life As Integer
        Public Color As Color
        Public Size As Single
    End Class

    Private ReadOnly _effects As New List(Of Particle)
    Private ReadOnly _texts As New List(Of FloatText)
    Private ReadOnly _fxRng As New Random()

    Private Function GetFont(sizePx As Single, Optional bold As Boolean = True) As Font
        Dim key = (sizePx, bold)
        Dim f As Font = Nothing
        If Not _fonts.TryGetValue(key, f) Then
            f = _assets.MakeFont(sizePx, bold)
            _fonts(key) = f
        End If
        Return f
    End Function

    Private Sub Toast(text As String)
        _toast = text
        _toastFrames = 150
    End Sub

    Private Sub UpdateEffects()
        For i = _effects.Count - 1 To 0 Step -1
            Dim p = _effects(i)
            p.X += p.Vx : p.Y += p.Vy
            p.Vy += 0.25F
            p.Vx *= 0.98F
            p.Angle += p.Vx * 0.05F
            p.Life -= 1
            If p.Life <= 0 Then _effects.RemoveAt(i)
        Next
        For i = _texts.Count - 1 To 0 Step -1
            Dim t = _texts(i)
            t.Life -= 1
            t.Y -= 0.4F
            If t.Life <= 0 Then _texts.RemoveAt(i)
        Next
        If _shake > 0 Then _shake = Math.Max(0, _shake - 0.35F)
        If _goFrames > 0 Then _goFrames -= 1
        If _toastFrames > 0 Then _toastFrames -= 1
    End Sub

    Private Sub SpawnBurst(cx As Single, cy As Single, c As Color, count As Integer)
        For k = 0 To count - 1
            Dim ang = _fxRng.NextDouble() * Math.PI * 2
            Dim sp = 1.5 + _fxRng.NextDouble() * 4
            Dim strand = (k Mod 2 = 0)
            _effects.Add(New Particle With {
                .X = cx, .Y = cy,
                .Vx = CSng(Math.Cos(ang) * sp), .Vy = CSng(Math.Sin(ang) * sp - 3),
                .Life = 30 + _fxRng.Next(20), .MaxLife = 50,
                .Color = If(strand, Color.FromArgb(255, 250, 236), c),
                .Size = If(strand, 10 + CSng(_fxRng.NextDouble() * 8), 4 + CSng(_fxRng.NextDouble() * 5)),
                .Strand = strand, .Angle = CSng(_fxRng.NextDouble() * Math.PI)})
        Next
    End Sub

    Private Sub SpawnEraseEffects(mine As Boolean, cells As List(Of Point), f As Field)
        Dim bx = If(mine, MyBoardX, OppBoardX)
        For Each c In cells
            Dim kind = f(c.X, c.Y)
            SpawnBurst(bx + c.X * Cell + Cell / 2.0F, BoardY + c.Y * Cell + Cell / 2.0F, Assets.KindColors(Math.Clamp(CInt(kind), 1, 5)), 5)
        Next
    End Sub

    Private Sub SpawnOppEraseEffects(cells As List(Of (P As Point, Kind As Byte)))
        For Each c In cells
            SpawnBurst(OppBoardX + c.P.X * Cell + Cell / 2.0F, BoardY + c.P.Y * Cell + Cell / 2.0F, Assets.KindColors(Math.Clamp(CInt(c.Kind), 1, 5)), 4)
        Next
    End Sub

    Private Sub AddChainText(mine As Boolean, cx As Single, cy As Single, chain As Integer)
        Dim bx = If(mine, MyBoardX, OppBoardX)
        _texts.Add(New FloatText With {
            .Text = $"{chain}れんさ!",
            .X = Math.Clamp(bx + (cx + 0.5F) * Cell, bx + 70, bx + BoardW - 70),
            .Y = BoardY + (cy + 0.5F) * Cell,
            .Life = 70,
            .Color = If(chain >= 3, Color.FromArgb(255, 90, 60), Color.FromArgb(255, 220, 80)),
            .Size = Math.Min(44, 26 + chain * 3)})
    End Sub

    ' =====================================================================
    ' 拡大縮小とタイトル画面の入力欄
    ' =====================================================================

    Private Sub UpdateScale()
        Dim cw = Math.Max(1, ClientSize.Width), ch = Math.Max(1, ClientSize.Height)
        _scale = Math.Min(cw / CSng(LogicalW), ch / CSng(LogicalH))
        _ox = (cw - LogicalW * _scale) / 2
        _oy = (ch - LogicalH * _scale) / 2
        Dim px = CInt(Math.Round(Cell * _scale))
        If px <> _spritePx Then
            _spritePx = px
            _assets.ClearCache()
        End If
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        If ClientSize.Width <= 0 OrElse ClientSize.Height <= 0 Then Return
        UpdateScale()
        LayoutTitleControls()
    End Sub

    Private Function ToDevice(r As RectangleF) As Rectangle
        Return Rectangle.Round(New RectangleF(_ox + r.X * _scale, _oy + r.Y * _scale, r.Width * _scale, r.Height * _scale))
    End Function

    Private Function ToLogical(p As Point) As PointF
        Return New PointF((p.X - _ox) / _scale, (p.Y - _oy) / _scale)
    End Function

    ' タイトル画面のカード内レイアウト
    Private Shared ReadOnly TitleCard As New RectangleF(290, 186, 380, 366)
    Private Const FieldX As Single = 316
    Private Const FieldW As Single = 328
    Private Shared ReadOnly FieldLabelY As Single() = {200, 268, 336, 404}

    Private Sub LayoutTitleControls()
        UpdateScale()
        Dim f = New Font(_assets.FontFamilyName, Math.Max(9.0F, 17 * _scale), FontStyle.Regular, GraphicsUnit.Pixel)
        Dim boxes = {_txtHost, _txtPort, _txtName, _txtRoom}
        For i = 0 To boxes.Length - 1
            Dim w = If(boxes(i) Is _txtPort, 120.0F, FieldW)
            Dim r = ToDevice(New RectangleF(FieldX, FieldLabelY(i) + 22, w, 30))
            Dim old = boxes(i).Font
            boxes(i).Font = f
            If old IsNot Nothing AndAlso old IsNot f AndAlso old IsNot Control.DefaultFont Then old.Dispose()
            boxes(i).Bounds = r
        Next
    End Sub

    Private Sub ShowTitleControls(visible As Boolean)
        For Each tb In {_txtHost, _txtPort, _txtName, _txtRoom}
            tb.Visible = visible
        Next
        If visible Then
            If _txtName.Text.Trim() = "" Then _txtName.Focus() Else _txtRoom.Focus()
        Else
            ActiveControl = Nothing
            Focus()
        End If
    End Sub

    Private Sub ShowTitleMessage(text As String, isError As Boolean)
        _titleMessage = text
        _titleMessageIsError = isError
    End Sub

    ' =====================================================================
    ' マウス
    ' =====================================================================

    ' 設定画面のスライダー（論理座標）
    Private Shared ReadOnly BgmSlider As New RectangleF(300, 268, 360, 16)
    Private Shared ReadOnly SeSlider As New RectangleF(300, 368, 360, 16)
    Private _dragSlider As Integer ' 0=なし 1=BGM 2=SE

    Private Shared Function SliderHit(r As RectangleF) As RectangleF
        Dim h = r
        h.Inflate(12, 14)
        Return h
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        _mouse = ToLogical(e.Location)
        If _dragSlider <> 0 Then SetSliderFromMouse()
        Dim hand = _buttons.Any(Function(b) b.Enabled AndAlso b.Rect.Contains(_mouse)) OrElse
                   (_screen = Screen.Settings AndAlso _settingsTab = 0 AndAlso (SliderHit(BgmSlider).Contains(_mouse) OrElse SliderHit(SeSlider).Contains(_mouse))) OrElse
                   (_screen = Screen.Settings AndAlso _settingsTab = 1 AndAlso HitSlot(_mouse).Action >= 0)
        Cursor = If(hand, Cursors.Hand, Cursors.Default)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        _mouse = New PointF(-1, -1)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        _mouse = ToLogical(e.Location)
        If _screen = Screen.Settings AndAlso _settingsTab = 1 Then
            Dim hs = HitSlot(_mouse)
            If hs.Action >= 0 Then
                If e.Button = MouseButtons.Right Then
                    ' 右クリックでその枠を空欄にする
                    _input.Config.ClearKey(CType(hs.Action, GameAction), hs.Slot)
                    _capAction = -1
                ElseIf e.Button = MouseButtons.Left Then
                    _capAction = hs.Action : _capSlot = hs.Slot
                    _input.Reset()
                End If
                Return
            End If
        End If
        If e.Button <> MouseButtons.Left Then Return
        If _screen = Screen.Settings AndAlso _settingsTab = 0 AndAlso _sound IsNot Nothing AndAlso _sound.Available Then
            If SliderHit(BgmSlider).Contains(_mouse) Then
                _dragSlider = 1
            ElseIf SliderHit(SeSlider).Contains(_mouse) Then
                _dragSlider = 2
            End If
            If _dragSlider <> 0 Then
                Capture = True
                SetSliderFromMouse()
                Return
            End If
        End If
        For Each b In _buttons.ToArray()
            If b.Enabled AndAlso b.Rect.Contains(_mouse) Then
                _sound?.Play(Sfx.Click)
                b.Action?.Invoke()
                Return
            End If
        Next
        ' 空いている所をクリックしたらゲーム操作用にフォームへフォーカスを戻す
        If _screen <> Screen.Title Then Focus()
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If _dragSlider <> 0 Then
            Dim which = _dragSlider
            _dragSlider = 0
            Capture = False
            ' 効果音の音量は、離したときにサンプルを1つ鳴らして確かめられるようにする
            If which = 2 Then _sound?.Play(Sfx.Rotate)
        End If
    End Sub

    Private Sub SetSliderFromMouse()
        Dim r = If(_dragSlider = 1, BgmSlider, SeSlider)
        Dim v = CSng(Math.Round(Math.Clamp((_mouse.X - r.X) / r.Width, 0F, 1F), 2))
        If _dragSlider = 1 Then _sound.BgmVolume = v Else _sound.SeVolume = v
    End Sub

    Private Sub OpenSettings()
        If _screen <> Screen.Title Then Return
        ShowTitleControls(False)
        _screen = Screen.Settings
        _settingsTab = 0
        _capAction = -1
    End Sub

    Private Sub CloseSettings()
        _capAction = -1
        _dragSlider = 0
        Capture = False
        _screen = Screen.Title
        ShowTitleControls(True)
    End Sub

    ' =====================================================================
    ' 描画
    ' =====================================================================

    Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
        ' 背景は OnPaint でまとめて描く（ちらつき防止）
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        UpdateScale()
        EnsureBackground()
        Dim isMatch = (_screen = Screen.Countdown OrElse _screen = Screen.Playing OrElse _screen = Screen.Result)
        ' 背景（対戦中は盤面の枠なども含めた1枚）をそのまま転送する。
        If _ox >= 1 OrElse _oy >= 1 Then g.Clear(Color.FromArgb(40, 24, 12))
        g.CompositingMode = CompositingMode.SourceCopy
        g.DrawImage(If(isMatch, EnsureMatchStatic(), _bg), New Rectangle(CInt(Math.Round(_ox)), CInt(Math.Round(_oy)), _bg.Width, _bg.Height))
        g.CompositingMode = CompositingMode.SourceOver
        _baseMatrix?.Dispose()
        _baseMatrix = New Matrix(_scale, 0, 0, _scale, CSng(Math.Round(_ox)), CSng(Math.Round(_oy)))
        g.Transform = _baseMatrix
        _offX = 0 : _offY = 0
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = TextRenderingHint.AntiAlias
        g.InterpolationMode = InterpolationMode.Bilinear
        g.PixelOffsetMode = PixelOffsetMode.Half
        _buttons.Clear()

        Select Case _screen
            Case Screen.Title
                DrawTitle(g)
            Case Screen.Connecting, Screen.Waiting
                DrawWaiting(g)
            Case Screen.Settings
                DrawSettings(g)
            Case Screen.Countdown, Screen.Playing, Screen.Result
                DrawMatch(g)
        End Select

        DrawEffects(g)
        If _screen = Screen.Result Then DrawResultPanel(g)
        DrawBottomBar(g)
        If _toastFrames > 0 AndAlso _toast <> "" Then DrawToast(g)
        CountFps()
        If _showFps Then DrawText(g, $"{_fps} fps", 13, LogicalW - 8, 10, Color.FromArgb(120, 0, 0, 0), StringAlignment.Far, bold:=False)
    End Sub

    ''' <summary>背景（だしの色のグラデーションと、うどんの麺のような波線）。サイズが変わったときだけ作り直す。</summary>
    Private Sub EnsureBackground()
        Dim w = Math.Max(1, CInt(Math.Ceiling(LogicalW * _scale)))
        Dim h = Math.Max(1, CInt(Math.Ceiling(LogicalH * _scale)))
        If _bg IsNot Nothing AndAlso _bgSize = New Size(w, h) Then Return
        _bg?.Dispose()
        _bg = New Bitmap(w, h, PixelFormat.Format32bppPArgb)
        _bgSize = New Size(w, h)
        Using g = Graphics.FromImage(_bg)
            g.ScaleTransform(_scale, _scale)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using lg As New LinearGradientBrush(New Rectangle(0, 0, LogicalW, LogicalH), Color.FromArgb(252, 226, 160), Color.FromArgb(226, 160, 70), LinearGradientMode.Vertical)
                g.FillRectangle(lg, 0, 0, LogicalW, LogicalH)
            End Using
            ' うどんの麺の波線
            Dim rng As New Random(7)
            For k = 0 To 13
                Dim baseY = 20 + k * 46 + rng.Next(-10, 10)
                Dim amp = 6 + rng.Next(10)
                Dim freq = 0.012 + rng.NextDouble() * 0.01
                Dim ph = rng.NextDouble() * 6
                Dim pts As New List(Of PointF)
                For x = -10 To LogicalW + 10 Step 8
                    pts.Add(New PointF(x, CSng(baseY + Math.Sin(x * freq + ph) * amp)))
                Next
                Using pen As New Pen(Color.FromArgb(70, 255, 252, 240), 7 + rng.Next(5))
                    pen.StartCap = LineCap.Round : pen.EndCap = LineCap.Round
                    g.DrawCurve(pen, pts.ToArray())
                End Using
                Using pen As New Pen(Color.FromArgb(40, 255, 255, 255), 2)
                    g.DrawCurve(pen, pts.Select(Function(p) New PointF(p.X, p.Y - 2)).ToArray())
                End Using
            Next
            ' ねぎの散らし
            For k = 0 To 40
                Dim x = rng.Next(LogicalW), y = rng.Next(LogicalH)
                Using b As New SolidBrush(Color.FromArgb(50, 90, 170, 70))
                    g.FillEllipse(b, x, y, 9, 7)
                End Using
            Next
        End Using
    End Sub

    ' ---- 汎用 ----

    Private Sub DrawText(g As Graphics, text As String, size As Single, x As Single, y As Single, c As Color,
                         Optional align As StringAlignment = StringAlignment.Center, Optional bold As Boolean = True)
        Using sf As New StringFormat With {.Alignment = align, .LineAlignment = StringAlignment.Center}
            Using b As New SolidBrush(c)
                g.DrawString(text, GetFont(size, bold), b, x, y, sf)
            End Using
        End Using
    End Sub

    Private _fontFamily As FontFamily
    Private ReadOnly _pathCache As New Dictionary(Of (String, StringAlignment), GraphicsPath)
    Private Const PathBaseSize As Single = 100

    ''' <summary>
    ''' 縁取りつきの文字。文字の輪郭（GraphicsPath）は作るのが重いので、
    ''' 原点・基準サイズで作ってキャッシュし、描くときに拡大・移動したコピーを使う。
    ''' </summary>
    Private Sub DrawOutlined(g As Graphics, text As String, size As Single, cx As Single, cy As Single,
                             fill As Brush, outline As Color, outlineW As Single, Optional align As StringAlignment = StringAlignment.Center)
        If _fontFamily Is Nothing Then _fontFamily = New FontFamily(_assets.FontFamilyName)
        Dim key = (text, align)
        Dim basePath As GraphicsPath = Nothing
        If Not _pathCache.TryGetValue(key, basePath) Then
            If _pathCache.Count > 400 Then
                For Each p In _pathCache.Values : p.Dispose() : Next
                _pathCache.Clear()
            End If
            basePath = New GraphicsPath()
            Using sf As New StringFormat With {.Alignment = align, .LineAlignment = StringAlignment.Center}
                basePath.AddString(text, _fontFamily, CInt(FontStyle.Bold), PathBaseSize, PointF.Empty, sf)
            End Using
            _pathCache(key) = basePath
        End If
        Dim k = size / PathBaseSize
        Using path = CType(basePath.Clone(), GraphicsPath)
            Using m As New Matrix(k, 0, 0, k, cx, cy)
                path.Transform(m)
            End Using
            Using pen As New Pen(outline, outlineW) With {.LineJoin = LineJoin.Round}
                g.DrawPath(pen, path)
            End Using
            g.FillPath(fill, path)
        End Using
    End Sub

    Private Sub DrawOutlined(g As Graphics, text As String, size As Single, cx As Single, cy As Single,
                             fill As Color, outline As Color, outlineW As Single, Optional align As StringAlignment = StringAlignment.Center)
        Using b As New SolidBrush(fill)
            DrawOutlined(g, text, size, cx, cy, b, outline, outlineW, align)
        End Using
    End Sub

    Private Shared Function RoundRect(r As RectangleF, radius As Single) As GraphicsPath
        Dim p As New GraphicsPath()
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2)
        If radius <= 0.5F Then
            p.AddRectangle(r)
            Return p
        End If
        Dim d = radius * 2
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    Private Sub FillRound(g As Graphics, r As RectangleF, radius As Single, c As Color, Optional border As Color = Nothing, Optional borderW As Single = 0)
        Using p = RoundRect(r, radius)
            Using b As New SolidBrush(c)
                g.FillPath(b, p)
            End Using
            If borderW > 0 Then
                Using pen As New Pen(border, borderW)
                    g.DrawPath(pen, p)
                End Using
            End If
        End Using
    End Sub

    Private Sub DrawButton(g As Graphics, r As RectangleF, label As String, action As Action, Optional enabled As Boolean = True, Optional primary As Boolean = True, Optional fontSize As Single = 22)
        Dim hover = enabled AndAlso r.Contains(_mouse)
        Dim baseC = If(primary, ColRed, Color.FromArgb(150, 100, 52))
        If Not enabled Then baseC = Color.FromArgb(160, 150, 140)
        If hover Then baseC = Assets.Lighten(baseC, 0.15F)
        Dim pressOff = If(hover AndAlso (MouseButtons And MouseButtons.Left) <> 0, 2.0F, 0F)
        FillRound(g, New RectangleF(r.X, r.Y + 4, r.Width, r.Height), 14, Assets.Darken(baseC, 0.4F))
        Dim rr As New RectangleF(r.X, r.Y + pressOff, r.Width, r.Height)
        Using p = RoundRect(rr, 14)
            Using lg As New LinearGradientBrush(rr, Assets.Lighten(baseC, 0.2F), baseC, LinearGradientMode.Vertical)
                g.FillPath(lg, p)
            End Using
            Using pen As New Pen(Assets.Darken(baseC, 0.3F), 2)
                g.DrawPath(pen, p)
            End Using
        End Using
        DrawText(g, label, fontSize, rr.X + rr.Width / 2, rr.Y + rr.Height / 2 + 1, If(enabled, Color.White, Color.FromArgb(235, 230, 225)))
        _buttons.Add(New UiButton With {.Rect = r, .Label = label, .Enabled = enabled, .Action = action, .Primary = primary})
    End Sub

    ''' <summary>
    ''' うどんのセル画像を描く。画像は表示サイズで作ってあるので、座標変換を外して
    ''' 拡大縮小なしで転送する（GDI+ で一番速い経路）。_offX/_offY は画面揺れなどのずらし。
    ''' </summary>
    Private Sub DrawSprite(g As Graphics, kind As Integer, x As Single, y As Single, size As Single)
        If kind < 1 OrElse kind > 5 Then Return
        Dim px = CInt(Math.Round(size * _scale))
        Dim bmp = _assets.Cell(kind, px)
        Dim dx = CInt(Math.Round(_baseMatrix.OffsetX + (x + _offX) * _scale))
        Dim dy = CInt(Math.Round(_baseMatrix.OffsetY + (y + _offY) * _scale))
        g.ResetTransform()
        g.DrawImage(bmp, New Rectangle(dx, dy, bmp.Width, bmp.Height))
        RestoreTransform(g)
    End Sub

    Private Sub RestoreTransform(g As Graphics)
        g.Transform = _baseMatrix
        If _offX <> 0 OrElse _offY <> 0 Then g.TranslateTransform(_offX, _offY)
    End Sub

    ''' <summary>対戦画面の動かない部分（背景・盤面の枠・パネル・名前）を1枚にまとめる。</summary>
    Private Function EnsureMatchStatic() As Bitmap
        Dim key = $"{_bg.Width}x{_bg.Height}|{_myName}|{_oppName}|{_roomId}"
        If _matchStatic IsNot Nothing AndAlso key = _matchStaticKey Then Return _matchStatic
        _matchStatic?.Dispose()
        _matchStatic = New Bitmap(_bg.Width, _bg.Height, PixelFormat.Format32bppPArgb)
        _matchStaticKey = key
        Using g = Graphics.FromImage(_matchStatic)
            g.DrawImage(_bg, New Rectangle(0, 0, _bg.Width, _bg.Height))
            g.ScaleTransform(_scale, _scale)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TextRenderingHint = TextRenderingHint.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.Half
            For Each mine In {True, False}
                Dim bx = If(mine, MyBoardX, OppBoardX)
                DrawNamePlate(g, bx - 10, If(mine, _myName, _oppName), If(mine, "あなた", "あいて"), mine)
                DrawBoardFrame(g, bx)
                Using b As New SolidBrush(Color.FromArgb(70, 40, 20, 10))
                    g.FillRectangle(b, bx, 60, BoardW, 40) ' おじゃまそば予告欄
                End Using
                Dim px = If(mine, 308, 564)
                FillRound(g, New RectangleF(px, 54, 88, 196), 12, Color.FromArgb(230, 255, 248, 228), ColBrown, 2.5F)
                DrawText(g, "NEXT", 16, px + 44, 54 + 16, ColBrownDark)
                FillRound(g, New RectangleF(px, 262, 88, 120), 12, Color.FromArgb(230, 255, 248, 228), ColBrown, 2.5F)
                DrawText(g, "れんさ", 15, px + 44, 262 + 16, ColBrownDark)
            Next
            DrawOutlined(g, "VS", 54, 480, 230, ColRed, Color.White, 8)
            Dim info = If(_roomId = "", "自動マッチング", "ルーム " & _roomId)
            DrawText(g, info, 13, 480, 420, ColBrownDark, bold:=False)
        End Using
        Return _matchStatic
    End Function

    Private Sub DrawSpriteFaded(g As Graphics, kind As Integer, x As Single, y As Single, size As Single, alpha As Single)
        If kind < 1 OrElse kind > 5 Then Return
        Dim px = CInt(Math.Round(size * _scale))
        Dim bmp = _assets.Cell(kind, px)
        Dim cm As New ColorMatrix() With {.Matrix33 = Math.Clamp(alpha, 0F, 1F)}
        Using ia As New ImageAttributes()
            ia.SetColorMatrix(cm)
            g.DrawImage(bmp, Rectangle.Round(New RectangleF(x, y, size, size)), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia)
        End Using
    End Sub

    Private Sub DrawPlain(g As Graphics, name As String, cx As Single, cy As Single, height As Single, Optional angle As Single = 0)
        Dim bmp = _assets.Plain(name, CInt(Math.Round(height * _scale)))
        If bmp Is Nothing Then Return
        Dim w = height * bmp.Width / bmp.Height
        Dim st = g.Save()
        g.TranslateTransform(cx, cy)
        If angle <> 0 Then g.RotateTransform(angle)
        g.DrawImage(bmp, -w / 2, -height / 2, w, height)
        g.Restore(st)
    End Sub

    ' =====================================================================
    ' タイトル
    ' =====================================================================

    Private Sub DrawLogo(g As Graphics, cx As Single, cy As Single, size As Single)
        Dim t = _frame / 60.0F
        Dim chars = "ぷようどん"
        Dim cw = size * 0.92F
        Dim startX = cx - (chars.Length * cw + size * 1.15F) / 2 + cw / 2
        For i = 0 To chars.Length - 1
            Dim bob = CSng(Math.Sin(t * 3 + i * 0.7) * size * 0.06)
            Dim x = startX + i * cw
            Using lg As New LinearGradientBrush(New RectangleF(x - cw, cy - size, cw * 2, size * 2), Color.White, Color.FromArgb(255, 236, 190), LinearGradientMode.Vertical)
                DrawOutlined(g, chars(i).ToString(), size, x, cy + bob, lg, ColBrownDark, size * 0.16F)
            End Using
        Next
        Dim vsX = startX + (chars.Length - 0.5F) * cw - size * 0.02F
        Using lg As New LinearGradientBrush(New RectangleF(vsX - size, cy - size, size * 2, size * 2), Color.FromArgb(255, 110, 80), ColRed, LinearGradientMode.Vertical)
            DrawOutlined(g, "VS", size * 1.05F, vsX, cy, lg, Color.White, size * 0.18F, StringAlignment.Near)
        End Using
        Using lg As New LinearGradientBrush(New RectangleF(vsX - size, cy - size, size * 2, size * 2), Color.FromArgb(255, 110, 80), ColRed, LinearGradientMode.Vertical)
            DrawOutlined(g, "VS", size * 1.05F, vsX, cy, lg, ColBrownDark, size * 0.07F, StringAlignment.Near)
        End Using
    End Sub

    Private Sub DrawTitle(g As Graphics)
        DrawLogo(g, 480, 84, 72)
        DrawOutlined(g, "～ さぬきの落ち物うどんバトル ～", 20, 480, 150, ColBrownDark, Color.FromArgb(255, 248, 228), 5)

        ' 左右に揺れるうどんたち
        Dim t = _frame / 60.0F
        Dim deco = {("kake", 150.0F, 260.0F), ("bukkake", 110.0F, 420.0F), ("nabeyaki", 820.0F, 260.0F), ("curry", 860.0F, 420.0F), ("kamatama", 170.0F, 545.0F), ("kakesoba", 800.0F, 545.0F)}
        For i = 0 To deco.Length - 1
            Dim d = deco(i)
            Dim bob = CSng(Math.Sin(t * 2 + i) * 6)
            DrawPlain(g, d.Item1, d.Item2, d.Item3 + bob, 120, CSng(Math.Sin(t * 1.5 + i * 2) * 5))
        Next

        ' 入力カード
        FillRound(g, New RectangleF(TitleCard.X + 4, TitleCard.Y + 6, TitleCard.Width, TitleCard.Height), 20, Color.FromArgb(90, 60, 30, 10))
        FillRound(g, TitleCard, 20, Color.FromArgb(245, 255, 250, 238), ColBrown, 3)
        Dim labels = {"サーバーのIPアドレス", "ポート番号", $"ニックネーム（{Protocol.MaxNameLength}文字まで）", "ルームID（空欄なら自動マッチング）"}
        For i = 0 To labels.Length - 1
            DrawText(g, labels(i), 15, FieldX, FieldLabelY(i) + 10, ColBrownDark, StringAlignment.Near)
        Next
        DrawButton(g, New RectangleF(360, 482, 240, 52), "たいせんする！", AddressOf BeginConnect)
        DrawButton(g, New RectangleF(810, 16, 134, 40), "せってい", AddressOf OpenSettings, primary:=False, fontSize:=18)

        If _titleMessage <> "" Then
            Dim c = If(_titleMessageIsError, Color.FromArgb(200, 30, 20), ColBrownDark)
            FillRound(g, New RectangleF(150, 562, 660, 34), 10, Color.FromArgb(230, 255, 255, 255))
            DrawText(g, _titleMessage, 15, 480, 579, c)
        End If
    End Sub

    ' =====================================================================
    ' 設定
    ' =====================================================================

    Private _settingsTab As Integer ' 0=おと 1=そうさ
    Private _capAction As Integer = -1 ' キー入力待ちの枠（-1=なし）
    Private _capSlot As Integer

    Private Shared ReadOnly SettingsCard As New RectangleF(130, 118, 700, 476)
    Private Const SlotX0 As Single = 320
    Private Const SlotW As Single = 150
    Private Const SlotGap As Single = 8
    Private Const SlotH As Single = 34
    Private Const RowY0 As Single = 212
    Private Const RowDy As Single = 42

    Private Shared Function SlotRect(action As Integer, slot As Integer) As RectangleF
        Return New RectangleF(SlotX0 + slot * (SlotW + SlotGap) - 20, RowY0 + action * RowDy, SlotW, SlotH)
    End Function

    Private Shared Function HitSlot(p As PointF) As (Action As Integer, Slot As Integer)
        For a = 0 To KeyConfig.ActionCount - 1
            For i = 0 To KeyConfig.SlotCount - 1
                If SlotRect(a, i).Contains(p) Then Return (a, i)
            Next
        Next
        Return (-1, -1)
    End Function

    Private Sub SelectSettingsTab(tab As Integer)
        _settingsTab = tab
        _capAction = -1
    End Sub

    ''' <summary>設定画面の毎フレーム処理（キー入力待ちと Esc）。</summary>
    Private Sub UpdateSettings()
        If _capAction < 0 Then
            If _input.WasPressed(Keys.Escape) Then CloseSettings()
            Return
        End If
        For Each k In _input.PressedKeys()
            If k = Keys.Escape Then
                _capAction = -1
            ElseIf k = Keys.Delete OrElse k = Keys.Back Then
                _input.Config.ClearKey(CType(_capAction, GameAction), _capSlot)
                _capAction = -1
            ElseIf KeyConfig.IsReserved(k) Then
                Toast("そのキーは割り当てられません")
            Else
                _input.Config.SetKey(CType(_capAction, GameAction), _capSlot, k)
                _capAction = -1
            End If
            Exit For
        Next
    End Sub

    Private Sub DrawSettings(g As Graphics)
        DrawLogo(g, 480, 62, 40)
        Dim card = SettingsCard
        FillRound(g, New RectangleF(card.X + 4, card.Y + 6, card.Width, card.Height), 20, Color.FromArgb(90, 60, 30, 10))
        FillRound(g, card, 20, Color.FromArgb(245, 255, 250, 238), ColBrown, 3)

        DrawButton(g, New RectangleF(300, 134, 170, 38), "おと", Sub() SelectSettingsTab(0), True, _settingsTab = 0, 18)
        DrawButton(g, New RectangleF(490, 134, 170, 38), "そうさ", Sub() SelectSettingsTab(1), True, _settingsTab = 1, 18)

        If _settingsTab = 0 Then
            If _sound Is Nothing OrElse Not _sound.Available Then
                DrawText(g, "サウンドデバイスが見つからないため、音は鳴りません", 16, 480, 320, ColBrown, bold:=False)
            Else
                DrawSlider(g, "BGM の音量", BgmSlider, _sound.BgmVolume)
                DrawSlider(g, "効果音の音量", SeSlider, _sound.SeVolume)
                DrawText(g, "効果音は、つまみを離すとサンプルが鳴ります", 13, 480, 424, ColBrown, bold:=False)
            End If
            DrawButton(g, New RectangleF(380, 500, 200, 44), "もどる (Esc)", AddressOf CloseSettings, primary:=False, fontSize:=18)
        Else
            DrawText(g, "枠をクリックしてキーを押す　右クリック／Delete：空欄　Esc：取り消し", 13, 480, 192, ColBrown, bold:=False)
            For a = 0 To KeyConfig.ActionCount - 1
                Dim y = RowY0 + a * RowDy + SlotH / 2
                DrawText(g, KeyConfig.ActionNames(a), 18, 160, y, ColBrownDark, StringAlignment.Near)
                For i = 0 To KeyConfig.SlotCount - 1
                    Dim r = SlotRect(a, i)
                    Dim waiting = (_capAction = a AndAlso _capSlot = i)
                    Dim k = _input.Config.GetKey(CType(a, GameAction), i)
                    Dim hover = r.Contains(_mouse)
                    Dim fill = If(waiting, Color.FromArgb(255, 236, 170), If(hover, Color.White, Color.FromArgb(255, 252, 240)))
                    FillRound(g, r, 8, fill, If(waiting, ColRed, ColBrown), If(waiting, 3, 2))
                    If waiting Then
                        DrawText(g, "キーを押して…", 14, r.X + r.Width / 2, y, ColRed)
                    ElseIf k = Keys.None Then
                        DrawText(g, "―", 16, r.X + r.Width / 2, y, Color.FromArgb(170, 150, 130), bold:=False)
                    Else
                        DrawText(g, KeyConfig.DisplayName(k), 16, r.X + r.Width / 2, y, ColBrownDark)
                    End If
                Next
            Next
            DrawButton(g, New RectangleF(145, 500, 160, 44), "初期設定", AddressOf ResetControls, primary:=False, fontSize:=17)
            DrawButton(g, New RectangleF(315, 500, 160, 44), "エクスポート", AddressOf ExportControls, primary:=False, fontSize:=17)
            DrawButton(g, New RectangleF(485, 500, 160, 44), "インポート", AddressOf ImportControls, primary:=False, fontSize:=17)
            DrawButton(g, New RectangleF(655, 500, 160, 44), "もどる (Esc)", AddressOf CloseSettings, primary:=False, fontSize:=17)
        End If
    End Sub

    Private Sub ResetControls()
        _capAction = -1
        _input.Config.ResetToDefault()
        Toast("操作を初期設定にもどしました")
    End Sub

    ''' <summary>ファイルダイアログを開く。開いている間はゲームループを止める。</summary>
    Private Function RunDialog(dlg As FileDialog) As Boolean
        _capAction = -1
        RemoveHandler Application.Idle, AddressOf OnApplicationIdle
        Try
            Return dlg.ShowDialog(Me) = DialogResult.OK
        Finally
            AddHandler Application.Idle, AddressOf OnApplicationIdle
            _nextFrameMs = _clock.Elapsed.TotalMilliseconds
            _input.Reset()
        End Try
    End Function

    Private Sub ExportControls()
        Using dlg As New SaveFileDialog With {
            .Title = "操作設定のエクスポート", .Filter = "JSON ファイル (*.json)|*.json",
            .FileName = "puyoudon-controls.json", .DefaultExt = "json", .OverwritePrompt = True}
            If Not RunDialog(dlg) Then Return
            Try
                IO.File.WriteAllText(dlg.FileName, _input.Config.ToJson(), New Text.UTF8Encoding(False))
                Toast("エクスポートしました")
            Catch ex As Exception
                Toast("書き込めませんでした: " & ex.Message)
            End Try
        End Using
    End Sub

    Private Sub ImportControls()
        Using dlg As New OpenFileDialog With {
            .Title = "操作設定のインポート", .Filter = "JSON ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*", .CheckFileExists = True}
            If Not RunDialog(dlg) Then Return
            Try
                _input.Config.LoadFromJson(IO.File.ReadAllText(dlg.FileName))
                Toast("インポートしました")
            Catch ex As Exception
                Toast("読み込めませんでした: " & ex.Message)
            End Try
        End Using
    End Sub

    Private Sub DrawSlider(g As Graphics, label As String, r As RectangleF, value As Single)
        DrawText(g, label, 18, r.X, r.Y - 24, ColBrownDark, StringAlignment.Near)
        DrawText(g, $"{CInt(Math.Round(value * 100))}", 18, r.Right, r.Y - 24, ColBrown, StringAlignment.Far)
        FillRound(g, r, 8, Color.FromArgb(70, 74, 44, 22))
        Dim fillW = Math.Max(16, r.Width * value)
        FillRound(g, New RectangleF(r.X, r.Y, fillW, r.Height), 8, ColDashi)
        Using b As New SolidBrush(Color.White)
            g.FillEllipse(b, r.X + fillW - 14, r.Y - 4, 24, 24)
        End Using
        Using pen As New Pen(ColBrown, 2)
            g.DrawEllipse(pen, r.X + fillW - 14, r.Y - 4, 24, 24)
        End Using
    End Sub

    ' =====================================================================
    ' 接続中・待機
    ' =====================================================================

    Private Sub DrawWaiting(g As Graphics)
        DrawLogo(g, 480, 70, 48)
        Dim card As New RectangleF(240, 140, 480, 400)
        FillRound(g, New RectangleF(card.X + 4, card.Y + 6, card.Width, card.Height), 20, Color.FromArgb(90, 60, 30, 10))
        FillRound(g, card, 20, Color.FromArgb(245, 255, 250, 238), ColBrown, 3)

        Dim dots = New String("."c, CInt((_frame \ 20) Mod 4))
        Dim title = If(_screen = Screen.Connecting, "サーバーに接続しています", "対戦相手を待っています")
        DrawText(g, title & dots, 26, 480, 190, ColBrownDark)

        ' ぐるぐる回るうどん
        Dim t = _frame / 60.0F
        For i = 0 To 3
            Dim ang = t * 2.2 + i * Math.PI / 2
            Dim x = CSng(480 + Math.Cos(ang) * 70)
            Dim y = CSng(320 + Math.Sin(ang) * 46)
            DrawSprite(g, i + 1, x - 26, y - 26, 52)
        Next
        DrawPlain(g, "kake", 480, 320, 70, CSng(Math.Sin(t * 3) * 8))

        If _screen = Screen.Waiting Then
            Dim roomText = If(_roomId = "", "自動マッチング", _roomId)
            DrawText(g, $"ルームID：{roomText}", 20, 480, 420, ColBrown)
            DrawText(g, $"{_myName}（プレイヤー{_myPlayer}）", 17, 480, 450, ColBrownDark, bold:=False)
        Else
            DrawText(g, _net?.Uri?.ToString(), 15, 480, 430, ColBrown, bold:=False)
        End If
        DrawButton(g, New RectangleF(380, 476, 200, 46), "キャンセル (Esc)", Sub() LeaveToTitle(""), primary:=False, fontSize:=18)
    End Sub

    ' =====================================================================
    ' 対戦画面
    ' =====================================================================

    Private Sub DrawMatch(g As Graphics)
        Dim sx = 0F, sy = 0F
        If _shake > 0 Then
            sx = CSng((_fxRng.NextDouble() - 0.5) * _shake * 2)
            sy = CSng((_fxRng.NextDouble() - 0.5) * _shake * 2)
        End If

        ' 名前・盤面の枠・パネルの枠は EnsureMatchStatic で背景に描き込み済み
        ' ---- 自分 ----
        DrawGarbagePreview(g, MyBoardX, _game.PendingGarbage)
        _offX = sx : _offY = sy
        RestoreTransform(g)
        DrawMyBoardContents(g)
        _offX = 0 : _offY = 0
        RestoreTransform(g)

        ' ---- 相手 ----
        DrawOppBoardContents(g)

        ' ---- ネクスト・連鎖 ----
        Dim myIndex = If(_screen = Screen.Countdown, 0, _game.PieceIndex)
        Dim mySeq = If(_screen = Screen.Countdown OrElse _game.Sequence Is Nothing, _previewSeq, _game.Sequence)
        DrawNextPanel(g, 308, mySeq, If(_screen = Screen.Countdown, -1, myIndex), True)
        DrawNextPanel(g, 564, _oppSeq, If(_screen = Screen.Countdown, -1, _oppIndex), False)
        DrawChainPanel(g, 308, If(_myChainShowFrames > 0, _myChainShow, 0), _game.MaxChain, _myChainShowFrames)
        DrawChainPanel(g, 564, If(_oppChainShowFrames > 0, _oppChain, 0), _oppMaxChain, _oppChainShowFrames)

        ' ---- 中央 ----
        DrawPlain(g, "kake", 480, 320, 86, CSng(Math.Sin(_frame / 40.0) * 4))
        If _rttMs >= 0 Then DrawText(g, $"ping {_rttMs}ms", 13, 480, 440, ColBrownDark, bold:=False)

        ' ---- カウントダウン ----
        If _screen = Screen.Countdown Then
            Dim n = (_countdownFrames + 59) \ 60
            Dim frac = (_countdownFrames Mod 60) / 60.0F
            Dim size = 120 + frac * 60
            FillRound(g, New RectangleF(0, 256, LogicalW, 190), 0, Color.FromArgb(130, 40, 20, 10))
            DrawOutlined(g, n.ToString(), size, 480, 370, Color.White, ColRed, 12)
            DrawOutlined(g, $"{_myName}  vs  {_oppName}", 24, 480, 282, Color.White, ColBrownDark, 5)
        ElseIf _goFrames > 0 Then
            Dim a = Math.Min(1.0F, _goFrames / 20.0F)
            Using b As New SolidBrush(Color.FromArgb(CInt(255 * a), 255, 255, 255))
                DrawOutlined(g, "スタート！", 92, 480, 340, b, Color.FromArgb(CInt(255 * a), ColRed), 12)
            End Using
        End If
    End Sub

    Private Sub DrawNamePlate(g As Graphics, x As Single, name As String, role As String, mine As Boolean)
        Dim r As New RectangleF(x, 12, BoardW + 20, 38)
        FillRound(g, r, 12, If(mine, Color.FromArgb(235, 214, 60, 48), Color.FromArgb(235, 60, 96, 170)), Color.White, 2)
        DrawText(g, role, 13, r.X + 34, r.Y + r.Height / 2, Color.FromArgb(255, 240, 220))
        DrawText(g, name, 20, r.X + r.Width / 2 + 14, r.Y + r.Height / 2 + 1, Color.White)
    End Sub

    Private Sub DrawBoardFrame(g As Graphics, bx As Single)
        Dim outer As New RectangleF(bx - 10, 54, BoardW + 20, BoardH + 62)
        FillRound(g, New RectangleF(outer.X + 4, outer.Y + 6, outer.Width, outer.Height), 16, Color.FromArgb(80, 40, 20, 5))
        Using p = RoundRect(outer, 16)
            Using lg As New LinearGradientBrush(outer, Color.FromArgb(176, 116, 60), Color.FromArgb(118, 70, 32), LinearGradientMode.Horizontal)
                g.FillPath(lg, p)
            End Using
            ' 木目
            Using pen As New Pen(Color.FromArgb(40, 60, 30, 10), 2)
                For k = 0 To 5
                    Dim yy = outer.Y + 20 + k * 90
                    g.DrawBezier(pen, outer.X + 3, yy, outer.X + 3, yy + 30, outer.X + 6, yy + 40, outer.X + 4, yy + 70)
                    g.DrawBezier(pen, outer.Right - 4, yy + 20, outer.Right - 3, yy + 50, outer.Right - 6, yy + 60, outer.Right - 4, yy + 85)
                Next
            End Using
            Using pen As New Pen(ColBrownDark, 3)
                g.DrawPath(pen, p)
            End Using
        End Using
        ' 盤面の内側
        Dim inner As New RectangleF(bx, BoardY, BoardW, BoardH)
        Using lg As New LinearGradientBrush(inner, Color.FromArgb(46, 56, 76), Color.FromArgb(26, 32, 46), LinearGradientMode.Vertical)
            g.FillRectangle(lg, inner)
        End Using
        Using pen As New Pen(Color.FromArgb(22, 255, 255, 255), 1)
            For x = 1 To Field.Cols - 1
                g.DrawLine(pen, bx + x * Cell, BoardY, bx + x * Cell, BoardY + BoardH)
            Next
            For y = 1 To Field.Rows - 1
                g.DrawLine(pen, bx, BoardY + y * Cell, bx + BoardW, BoardY + y * Cell)
            Next
        End Using
        ' 出現位置の × 印（ここが埋まると負け）
        Dim cx = bx + Field.SpawnX * Cell + Cell / 2.0F, cy = BoardY + Field.SpawnY * Cell + Cell / 2.0F
        Using pen As New Pen(Color.FromArgb(120, 255, 80, 70), 4) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
            g.DrawLine(pen, cx - 10, cy - 10, cx + 10, cy + 10)
            g.DrawLine(pen, cx + 10, cy - 10, cx - 10, cy + 10)
        End Using
        Using pen As New Pen(Color.FromArgb(60, 30, 14), 2)
            g.DrawRectangle(pen, inner.X, inner.Y, inner.Width, inner.Height)
        End Using
    End Sub

    Private Function DeadOffset(frames As Integer, x As Integer) As Single
        If frames < 0 Then Return 0
        Dim t = Math.Max(0, frames - x * 3)
        Return t * t * 0.09F
    End Function

    Private Sub DrawMyBoardContents(g As Graphics)
        Dim st = g.Save()
        g.SetClip(New RectangleF(MyBoardX, BoardY, BoardW, BoardH))
        Dim cells = _game.Field.CopyCells()
        Dim erasing As New HashSet(Of Point)(_game.ErasingCells)
        Dim hidden As Func(Of Integer, Integer, Boolean) = Function(x, y) erasing.Contains(New Point(x, y)) OrElse _game.IsCellAnimating(x, y)

        For x = 0 To Field.Cols - 1
            Dim dy = DeadOffset(_myDeadFrames, x)
            For y = 0 To Field.Rows - 1
                Dim k = cells(x, y)
                If k = 0 OrElse hidden(x, y) Then Continue For
                DrawSprite(g, k, MyBoardX + x * Cell, BoardY + y * Cell + dy, Cell)
            Next
        Next

        ' 消えるうどん（点滅してから弾ける）
        If _game.ErasingCells.Count > 0 Then
            Dim t = _game.EraseTimer
            For Each p In _game.ErasingCells
                Dim k = cells(p.X, p.Y)
                Dim x0 = MyBoardX + p.X * Cell, y0 = BoardY + p.Y * Cell
                If t < 20 Then
                    DrawSprite(g, k, x0, y0, Cell)
                    If (t \ 3) Mod 2 = 0 Then
                        Using b As New SolidBrush(Color.FromArgb(150, 255, 255, 255))
                            g.FillEllipse(b, x0 + 2, y0 + 2, Cell - 4, Cell - 4)
                        End Using
                    End If
                Else
                    Dim u = (t - 20) / CSng(PlayerGame.EraseFrames - 20)
                    Dim sz = Cell * (1 + u * 0.6F)
                    DrawSpriteFaded(g, k, x0 + Cell / 2.0F - sz / 2, y0 + Cell / 2.0F - sz / 2, sz, 1 - u)
                End If
            Next
        End If

        ' 重力で落ちている途中のうどん
        For Each c In _game.FallingCells
            DrawSprite(g, c.Kind, MyBoardX + c.X * Cell, BoardY + c.Y * Cell, Cell)
        Next

        ' 操作中のうどんと着地点
        If _game.Phase = PlayerGame.Phases.Falling Then
            Dim p = _game.Current
            Dim ghost = _game.GhostCells()
            DrawGhost(g, MyBoardX, ghost.A, p.Axis)
            DrawGhost(g, MyBoardX, ghost.C, p.Child)
            Dim fy = _game.FallFraction
            DrawSprite(g, p.Child, MyBoardX + p.ChildX * Cell, BoardY + (p.ChildY + fy) * Cell, Cell)
            DrawSprite(g, p.Axis, MyBoardX + p.X * Cell, BoardY + (p.Y + fy) * Cell, Cell)
            ' 軸のうどんに白い縁（回転の中心がわかるように）
            Dim pulse = CInt(140 + 80 * Math.Sin(_frame / 6.0))
            Using pen As New Pen(Color.FromArgb(pulse, 255, 255, 255), 2.5F)
                g.DrawEllipse(pen, MyBoardX + p.X * Cell + 2, BoardY + (p.Y + fy) * Cell + 2, Cell - 4, Cell - 4)
            End Using
        End If
        g.Restore(st)

        If _myDeadFrames >= 0 AndAlso _screen = Screen.Playing Then
            DrawOutlined(g, "あふれた～！", 34, MyBoardX + BoardW / 2.0F, BoardY + BoardH / 2.0F, Color.White, Color.FromArgb(60, 80, 160), 7)
            DrawText(g, "結果を待っています…", 15, MyBoardX + BoardW / 2.0F, BoardY + BoardH / 2.0F + 40, Color.White)
        End If
    End Sub

    Private Sub DrawGhost(g As Graphics, bx As Single, p As Point, kind As Byte)
        If p.Y < 0 Then Return
        ' 色の円は使わず、うどん画像を薄く表示する
        DrawSpriteFaded(g, kind, bx + p.X * Cell, BoardY + p.Y * Cell, Cell, 0.35F)
    End Sub

    Private Sub DrawOppBoardContents(g As Graphics)
        Dim st = g.Save()
        g.SetClip(New RectangleF(OppBoardX, BoardY, BoardW, BoardH))
        Dim cells = _oppField.CopyCells()
        For x = 0 To Field.Cols - 1
            Dim dy = DeadOffset(_oppDeadFrames, x)
            For y = 0 To Field.Rows - 1
                Dim k = cells(x, y)
                If k = 0 Then Continue For
                DrawSprite(g, k, OppBoardX + x * Cell, BoardY + y * Cell + dy, Cell)
            Next
        Next
        If _oppPieceVisible AndAlso _screen = Screen.Playing AndAlso _oppDeadFrames < 0 Then
            Dim p = _oppPiece
            DrawSprite(g, p.Child, OppBoardX + p.ChildX * Cell, BoardY + p.ChildY * Cell, Cell)
            DrawSprite(g, p.Axis, OppBoardX + p.X * Cell, BoardY + p.Y * Cell, Cell)
        End If
        g.Restore(st)
        If _oppLeft Then
            FillRound(g, New RectangleF(OppBoardX + 10, BoardY + BoardH / 2.0F - 30, BoardW - 20, 60), 12, Color.FromArgb(220, 30, 30, 40))
            DrawText(g, "相手が切断しました", 20, OppBoardX + BoardW / 2.0F, BoardY + BoardH / 2.0F, Color.White)
        End If
    End Sub

    ''' <summary>予告おじゃまそば（30個=特大、6個=大、1個=小）。</summary>
    Private Sub DrawGarbagePreview(g As Graphics, bx As Single, count As Integer)
        Dim r As New RectangleF(bx, 60, BoardW, 40)
        If count <= 0 Then Return
        Dim icons As New List(Of Integer)
        Dim n = count
        While n >= 30 AndAlso icons.Count < 6 : icons.Add(30) : n -= 30 : End While
        While n >= 6 AndAlso icons.Count < 6 : icons.Add(6) : n -= 6 : End While
        While n >= 1 AndAlso icons.Count < 6 : icons.Add(1) : n -= 1 : End While
        Dim x = bx + 4.0F
        Dim pulse = CSng(Math.Sin(_frame / 5.0) * 1.5)
        For Each v In icons
            Dim sz = If(v = 30, 38.0F, If(v = 6, 32.0F, 22.0F)) + If(v = 30, pulse, 0)
            DrawSprite(g, Field.Garbage, x, r.Y + (r.Height - sz) / 2, sz)
            If v = 30 Then
                Using pen As New Pen(Color.FromArgb(255, 220, 60), 2.5F)
                    g.DrawEllipse(pen, x + 1, r.Y + (r.Height - sz) / 2 + 1, sz - 2, sz - 2)
                End Using
            End If
            x += sz + 2
        Next
        DrawOutlined(g, $"×{count}", 18, bx + BoardW - 6, r.Y + r.Height / 2, Color.White, ColRed, 4, StringAlignment.Far)
    End Sub

    Private Sub DrawNextPanel(g As Graphics, x As Single, seq As PieceSequence, index As Integer, mine As Boolean)
        Dim r As New RectangleF(x, 54, 88, 196)
        If seq Is Nothing OrElse index < 0 Then
            If seq Is Nothing Then Return
            index = 0
            ' カウントダウン中は最初の1組をネクストとして見せる
            Dim first = seq(0)
            DrawPairVertical(g, first, r.X + r.Width / 2 - 20, r.Y + 34, 40)
            Dim second = seq(1)
            DrawPairVertical(g, second, r.X + r.Width / 2 - 14, r.Y + 126, 28)
            Return
        End If
        Dim n1 = seq(index + 1)
        Dim n2 = seq(index + 2)
        DrawPairVertical(g, n1, r.X + r.Width / 2 - 20, r.Y + 34, 40)
        DrawPairVertical(g, n2, r.X + r.Width / 2 - 14, r.Y + 126, 28)
    End Sub

    Private Sub DrawPairVertical(g As Graphics, pair As PiecePair, x As Single, y As Single, size As Single)
        DrawSprite(g, pair.Child, x, y, size)
        DrawSprite(g, pair.Axis, x, y + size, size)
    End Sub

    Private Sub DrawChainPanel(g As Graphics, x As Single, chain As Integer, maxChain As Integer, frames As Integer)
        Dim r As New RectangleF(x, 262, 88, 120)
        If chain > 0 Then
            Dim pop = If(frames > 140, 1 + (frames - 140) / 20.0F, 1.0F)
            DrawOutlined(g, chain.ToString(), 46 * pop, r.X + r.Width / 2, r.Y + 58, ColRed, Color.White, 6)
        Else
            DrawText(g, "-", 30, r.X + r.Width / 2, r.Y + 58, Color.FromArgb(180, 150, 120))
        End If
        DrawText(g, $"さいだい {maxChain}", 13, r.X + r.Width / 2, r.Y + 100, ColBrown)
    End Sub

    ' =====================================================================
    ' 結果
    ' =====================================================================

    Private Sub DrawResultPanel(g As Graphics)
        Dim appear = Math.Min(1.0F, _resultFrames / 20.0F)
        Using b As New SolidBrush(Color.FromArgb(CInt(90 * appear), 20, 10, 0))
            g.FillRectangle(b, 0, 0, LogicalW, LogicalH)
        End Using
        Dim card As New RectangleF(290, 150 + (1 - appear) * 40, 380, 330)
        FillRound(g, New RectangleF(card.X + 4, card.Y + 6, card.Width, card.Height), 20, Color.FromArgb(90, 60, 30, 10))
        FillRound(g, card, 20, Color.FromArgb(250, 255, 250, 238), ColBrown, 3)

        Dim bounce = CSng(Math.Abs(Math.Sin(_resultFrames / 12.0)) * If(_won, 8, 0))
        If _won Then
            Using lg As New LinearGradientBrush(New RectangleF(card.X, card.Y, card.Width, 100), Color.FromArgb(255, 120, 80), ColRed, LinearGradientMode.Vertical)
                DrawOutlined(g, "かち！", 76, 480, card.Y + 64 - bounce, lg, Color.White, 10)
            End Using
            DrawPlain(g, "kamatama", card.X + 52, card.Y + 64 - bounce, 64, -10)
            DrawPlain(g, "nabeyaki", card.Right - 52, card.Y + 64 - bounce, 64, 10)
        Else
            DrawOutlined(g, "まけ…", 76, 480, card.Y + 64, Color.FromArgb(80, 110, 190), Color.White, 10)
            DrawPlain(g, "kakesoba", card.X + 52, card.Y + 70, 60, -15)
        End If
        Dim reasonText As String
        If _resultReason = "left" Then
            reasonText = If(_won, "相手が切断したため、あなたの勝ちです", "接続が切れたため負けになりました")
        Else
            reasonText = If(_won, "相手のうどんがあふれました！", "うどんがあふれてしまいました")
        End If
        DrawText(g, reasonText, 17, 480, card.Y + 124, ColBrownDark)
        DrawText(g, $"さいだいれんさ  あなた {_game.MaxChain} ／ あいて {_oppMaxChain}", 14, 480, card.Y + 154, ColBrown, bold:=False)

        Dim status As String
        If _oppLeft OrElse _net Is Nothing Then
            status = "相手がいないため再戦できません"
        ElseIf _iWantRematch AndAlso _oppWantsRematch Then
            status = "まもなく再戦がはじまります…"
        ElseIf _iWantRematch Then
            status = "相手の返事を待っています" & New String("."c, CInt((_frame \ 20) Mod 4))
        ElseIf _oppWantsRematch Then
            status = "相手が再戦を希望しています！"
        Else
            status = "もういちど対戦しますか？"
        End If
        DrawText(g, status, 16, 480, card.Y + 192, If(_oppWantsRematch AndAlso Not _iWantRematch, ColRed, ColBrownDark))

        If _resultFrames > 30 Then
            DrawButton(g, New RectangleF(card.X + 24, card.Y + 236, 186, 56), If(_iWantRematch, "待っています…", "再戦する (Enter)"),
                       AddressOf RequestRematch, CanRematch() AndAlso Not _iWantRematch, True, 19)
            DrawButton(g, New RectangleF(card.X + 222, card.Y + 236, 134, 56), "タイトルへ", Sub() LeaveToTitle(""), True, False, 19)
        End If
    End Sub

    ' =====================================================================
    ' 共通の下部バー・演出・通知
    ' =====================================================================

    Private Sub DrawBottomBar(g As Graphics)
        Using b As New SolidBrush(Color.FromArgb(200, 74, 44, 22))
            g.FillRectangle(b, 0, 602, LogicalW, 38)
        End Using
        Dim help As String
        Select Case _screen
            Case Screen.Title
                help = "Enter：接続"
            Case Screen.Settings
                help = "Esc：もどる"
            Case Screen.Countdown, Screen.Playing
                help = "←→：移動　↓：高速落下　Esc×2：やめる　（キー設定はタイトルの「せってい」）"
            Case Screen.Result
                help = "Enter：再戦　Esc：タイトルへ"
            Case Else
                help = "Esc：キャンセル"
        End Select
        DrawText(g, help, 14, 16, 621, ColCream, StringAlignment.Near, bold:=False)

    End Sub

    Private Sub DrawEffects(g As Graphics)
        For Each p In _effects
            Dim a = Math.Clamp(p.Life / 30.0F, 0F, 1.0F)
            If p.Strand Then
                ' 麺のかけら
                Using pen As New Pen(Color.FromArgb(CInt(230 * a), p.Color), 3.5F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                    Dim dx = CSng(Math.Cos(p.Angle) * p.Size / 2), dy = CSng(Math.Sin(p.Angle) * p.Size / 2)
                    g.DrawBezier(pen, p.X - dx, p.Y - dy, p.X - dx / 3 + 3, p.Y - dy / 3 - 3, p.X + dx / 3 - 3, p.Y + dy / 3 + 3, p.X + dx, p.Y + dy)
                End Using
            Else
                Using b As New SolidBrush(Color.FromArgb(CInt(230 * a), p.Color))
                    g.FillEllipse(b, p.X - p.Size / 2, p.Y - p.Size / 2, p.Size, p.Size)
                End Using
            End If
        Next
        For Each t In _texts
            Dim a = Math.Clamp(t.Life / 20.0F, 0F, 1.0F)
            Dim grow = If(t.Life > 60, 1 + (t.Life - 60) / 20.0F, 1.0F)
            Using b As New SolidBrush(Color.FromArgb(CInt(255 * a), t.Color))
                DrawOutlined(g, t.Text, t.Size * grow, t.X, t.Y, b, Color.FromArgb(CInt(255 * a), 70, 30, 10), 6)
            End Using
        Next
    End Sub

    Private Sub DrawToast(g As Graphics)
        Dim a = Math.Min(1.0F, _toastFrames / 20.0F)
        Dim r As New RectangleF(280, 560, 400, 34)
        FillRound(g, r, 12, Color.FromArgb(CInt(220 * a), 30, 20, 10))
        DrawText(g, _toast, 16, 480, r.Y + r.Height / 2, Color.FromArgb(CInt(255 * a), 255, 255, 255))
    End Sub
End Class
