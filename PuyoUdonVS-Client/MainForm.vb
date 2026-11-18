Imports System.Runtime.InteropServices

''' <summary>
''' ぷようどんVS クライアントのメイン画面。
''' 画面はすべて論理座標 960×640 で描き、ウィンドウの大きさに合わせて拡大縮小する。
''' 60fps の固定フレームでゲームを進める（Application.Idle を使ったゲームループ）。
''' </summary>
Partial Public Class MainForm
    Inherits Form

    Public Enum Screen
        Title
        Connecting
        Waiting
        Countdown
        Playing
        Result
        Settings
    End Enum

    Public Const LogicalW As Integer = 960
    Public Const LogicalH As Integer = 640
    Private Const FrameMs As Double = 1000.0 / 60.0

    ' ---- 状態 ----
    Private _screen As Screen = Screen.Title
    Private _frame As Long
    Private ReadOnly _assets As New Assets()
    Private _sound As SoundEngine
    Private ReadOnly _input As New InputManager()
    Private ReadOnly _settings As ClientSettings = ClientSettings.Load()
    Private ReadOnly _clock As Stopwatch = Stopwatch.StartNew()
    Private _nextFrameMs As Double

    ' ---- 通信 ----
    Private _net As NetClient
    Private _myPlayer As Integer
    Private _roomId As String = ""
    Private _myName As String = ""
    Private _oppName As String = ""
    Private _lastPingMs As Long
    Private _rttMs As Long = -1

    ' ---- 対戦 ----
    Private ReadOnly _game As New PlayerGame()
    Private _seed As Integer
    Private _countdownFrames As Integer
    Private _countdownTotal As Integer
    Private _previewSeq As PieceSequence
    ' 相手
    Private ReadOnly _oppField As New Field()
    Private _oppSeq As PieceSequence
    Private _oppPiece As Piece
    Private _oppPieceVisible As Boolean
    Private _oppIndex As Integer
    Private _oppStateSincePiece As Boolean
    Private _oppChain As Integer
    Private _oppMaxChain As Integer
    Private _oppLeft As Boolean
    Private _oppDeadFrames As Integer = -1
    Private _myDeadFrames As Integer = -1
    ' 結果
    Private _won As Boolean
    Private _resultReason As String = ""
    Private _resultFrames As Integer
    Private _iWantRematch As Boolean
    Private _oppWantsRematch As Boolean
    Private _gameOverSent As Boolean
    Private _escArmedUntil As Long = -1

    ' ---- 演出 ----
    Private _myChainShow As Integer
    Private _myChainShowFrames As Integer
    Private _oppChainShowFrames As Integer
    Private _shake As Single

    ' ---- タイトル画面の入力欄 ----
    Private ReadOnly _txtHost As New TextBox()
    Private ReadOnly _txtPort As New TextBox()
    Private ReadOnly _txtName As New TextBox()
    Private ReadOnly _txtRoom As New TextBox()
    Private _titleMessage As String = ""
    Private _titleMessageIsError As Boolean

    ' ---- テスト用（コマンドライン --autoplay） ----
    Private _autoplay As Boolean
    Private _autoConnect As Boolean

    Public Sub New()
        Text = "ぷようどんVS"
        AutoScaleMode = AutoScaleMode.None
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        KeyPreview = True
        BackColor = Color.FromArgb(60, 36, 18)
        MinimumSize = New Size(640, 460)
        StartPosition = FormStartPosition.CenterScreen

        For Each tb In {_txtHost, _txtPort, _txtName, _txtRoom}
            tb.BorderStyle = BorderStyle.FixedSingle
            tb.BackColor = Color.FromArgb(255, 252, 240)
            tb.ForeColor = Color.FromArgb(70, 40, 20)
            AddHandler tb.KeyDown, AddressOf TitleTextBox_KeyDown
            Controls.Add(tb)
        Next
        _txtName.MaxLength = Protocol.MaxNameLength
        _txtRoom.MaxLength = 20
        _txtPort.MaxLength = 5
        _txtHost.TabIndex = 0 : _txtPort.TabIndex = 1 : _txtName.TabIndex = 2 : _txtRoom.TabIndex = 3

        Try
            _input.Config.LoadFrom(_settings.Controls)
        Catch
            _input.Config.ResetToDefault() ' 壊れた設定は初期設定に戻す
        End Try
        _txtHost.Text = _settings.Host
        _txtPort.Text = _settings.Port.ToString()
        _txtName.Text = _settings.Name
        _txtRoom.Text = _settings.Room
        ParseCommandLine()
    End Sub

    Private Sub ParseCommandLine()
        Dim args = Environment.GetCommandLineArgs()
        For i = 1 To args.Length - 1
            Dim a = args(i).ToLowerInvariant()
            Dim v = If(i + 1 < args.Length, args(i + 1), "")
            Select Case a
                Case "--host" : _txtHost.Text = v
                Case "--port" : _txtPort.Text = v
                Case "--name" : _txtName.Text = v
                Case "--room" : _txtRoom.Text = v
                Case "--connect" : _autoConnect = True
                Case "--autoplay" : _autoplay = True
                Case "--fps" : _showFps = True
            End Select
        Next
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Dim dpi = DeviceDpi / 96.0F
        Dim wa = Global.System.Windows.Forms.Screen.FromControl(Me).WorkingArea
        Dim sc = Math.Min(dpi, Math.Min((wa.Width - 40) / CSng(LogicalW), (wa.Height - 60) / CSng(LogicalH)))
        ClientSize = New Size(CInt(LogicalW * sc), CInt(LogicalH * sc))
        ' CenterToScreen() はマウスのあるモニターへ移すので、DPI の違うモニターだと
        ' 移動後にウィンドウが縮んで左上に寄ってしまう。大きさを決めたのと同じモニターの中央に置く。
        Location = New Point(wa.X + Math.Max(0, (wa.Width - Width) \ 2),
                             wa.Y + Math.Max(0, (wa.Height - Height) \ 2))

        _sound = New SoundEngine()
        _sound.BgmVolume = _settings.BgmVolume
        _sound.SeVolume = _settings.SeVolume
        _sound.StartBgm()

        LayoutTitleControls()
        ShowTitleControls(True)
        timeBeginPeriod(1)
        AddHandler Application.Idle, AddressOf OnApplicationIdle
        If _autoConnect Then BeginConnect()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        RemoveHandler Application.Idle, AddressOf OnApplicationIdle
        SaveSettings()
        If _net IsNot Nothing Then
            _net.Send(Protocol.Simple("LEAVE"))
            _net.Close()
            _net = Nothing
        End If
        _sound?.Dispose()
        timeEndPeriod(1)
        MyBase.OnFormClosing(e)
    End Sub

    Private Sub SaveSettings()
        _settings.Host = _txtHost.Text.Trim()
        Dim p As Integer
        If Integer.TryParse(_txtPort.Text, p) Then _settings.Port = p
        If Not _autoplay Then
            _settings.Name = _txtName.Text.Trim()
            _settings.Room = _txtRoom.Text.Trim()
        End If
        _settings.Controls = _input.Config.ToDictionary()
        If _sound IsNot Nothing Then
            _settings.BgmVolume = _sound.BgmVolume
            _settings.SeVolume = _sound.SeVolume
        End If
        _settings.Save()
    End Sub

    ' =====================================================================
    ' ゲームループ
    ' =====================================================================

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeMessage
        Public hWnd As IntPtr
        Public msg As UInteger
        Public wParam As IntPtr
        Public lParam As IntPtr
        Public time As UInteger
        Public pt As Point
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function PeekMessage(ByRef lpMsg As NativeMessage, hWnd As IntPtr, wMsgFilterMin As UInteger, wMsgFilterMax As UInteger, wRemoveMsg As UInteger) As Boolean
    End Function

    <DllImport("winmm.dll")>
    Private Shared Function timeBeginPeriod(uPeriod As UInteger) As UInteger
    End Function

    <DllImport("winmm.dll")>
    Private Shared Function timeEndPeriod(uPeriod As UInteger) As UInteger
    End Function

    Private Function AppIsIdle() As Boolean
        Dim m As New NativeMessage()
        Return Not PeekMessage(m, IntPtr.Zero, 0, 0, 0)
    End Function

    Private Sub OnApplicationIdle(sender As Object, e As EventArgs)
        While AppIsIdle() AndAlso Not IsDisposed
            Dim now = _clock.Elapsed.TotalMilliseconds
            If now < _nextFrameMs Then
                If _nextFrameMs - now > 1.5 Then Threading.Thread.Sleep(1) Else Threading.Thread.Yield()
                Continue While
            End If
            ' 処理落ちしても最大4フレームぶんだけ追いつく
            Dim steps = 0
            While now >= _nextFrameMs AndAlso steps < 4
                UpdateFrame()
                _nextFrameMs += FrameMs
                steps += 1
            End While
            If now >= _nextFrameMs Then _nextFrameMs = now + FrameMs
            Invalidate()
            Update()
        End While
    End Sub

    Private Sub UpdateFrame()
        _frame += 1
        PumpNetwork()
        Select Case _screen
            Case Screen.Countdown
                UpdateCountdown()
            Case Screen.Playing
                UpdatePlaying()
            Case Screen.Result
                _resultFrames += 1
                If _autoplay AndAlso _resultFrames = 150 AndAlso CanRematch() Then RequestRematch()
                UpdateBoardsWhileIdle()
                If _input.WasPressed(Keys.Escape) AndAlso _resultFrames > 30 Then LeaveToTitle("")
            Case Screen.Title
                If _input.WasPressed(Keys.Escape) Then ShowTitleMessage("", False)
            Case Screen.Settings
                UpdateSettings()
            Case Screen.Waiting, Screen.Connecting
                If _input.WasPressed(Keys.Escape) Then LeaveToTitle("")
        End Select
        HandleGlobalKeys()
        UpdateEffects()
        _input.EndFrame()
    End Sub

    Private Sub HandleGlobalKeys()
        If _txtHost.Focused OrElse _txtPort.Focused OrElse _txtName.Focused OrElse _txtRoom.Focused Then Return
        If _input.WasPressed(Keys.F3) Then _showFps = Not _showFps
    End Sub

    ' =====================================================================
    ' キー入力
    ' =====================================================================

    Private Const WM_KEYDOWN As Integer = &H100
    Private Const WM_SYSKEYDOWN As Integer = &H104

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If msg.Msg = WM_KEYDOWN OrElse msg.Msg = WM_SYSKEYDOWN Then
            ' lParam の bit30 が立っていれば OS の自動リピートなので無視（リピートは自前で行う）
            Dim isRepeat = (msg.LParam.ToInt64() And &H40000000L) <> 0
            Dim key = keyData And Keys.KeyCode
            If Not isRepeat Then _input.OnKeyDown(key)
            ' 対戦中は矢印キーやスペースをコントロールに渡さない
            If _screen <> Screen.Title Then
                If key = Keys.Enter Then HandleEnterKey()
                Return True
            End If
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Protected Overrides Sub OnDeactivate(e As EventArgs)
        MyBase.OnDeactivate(e)
        _input.Reset()
    End Sub

    Private Sub HandleEnterKey()
        If _screen = Screen.Result AndAlso _resultFrames > 30 Then
            If CanRematch() AndAlso Not _iWantRematch Then
                _sound?.Play(Sfx.Click)
                RequestRematch()
            End If
        End If
    End Sub

    Private Sub TitleTextBox_KeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter Then
            e.SuppressKeyPress = True
            _sound?.Play(Sfx.Click)
            BeginConnect()
        End If
    End Sub

    ' =====================================================================
    ' 通信
    ' =====================================================================

    Private Sub BeginConnect()
        If _screen <> Screen.Title Then Return
        Dim host = _txtHost.Text.Trim()
        Dim name = _txtName.Text.Trim()
        Dim room = _txtRoom.Text.Trim()
        Dim port As Integer
        If host = "" Then ShowTitleMessage("サーバーのIPアドレスを入力してください。", True) : _txtHost.Focus() : Return
        If Not Integer.TryParse(_txtPort.Text.Trim(), port) OrElse port < 1 OrElse port > 65535 Then
            ShowTitleMessage("ポート番号は 1〜65535 の数字で入力してください。", True) : _txtPort.Focus() : Return
        End If
        If name = "" Then ShowTitleMessage("ニックネームを入力してください。", True) : _txtName.Focus() : Return
        If name.Length > Protocol.MaxNameLength Then
            ShowTitleMessage($"ニックネームは{Protocol.MaxNameLength}文字までです。", True) : _txtName.Focus() : Return
        End If
        Try
            _net = New NetClient(host, port)
        Catch ex As UriFormatException
            ShowTitleMessage("IPアドレスの形式が正しくありません。", True) : _txtHost.Focus() : Return
        End Try
        _myName = name
        _roomId = room
        SaveSettings()
        ShowTitleMessage("", False)
        ShowTitleControls(False)
        _screen = Screen.Connecting
        _rttMs = -1
        _net.Start()
    End Sub

    Private Sub Send(json As String)
        _net?.Send(json)
    End Sub

    Private Sub PumpNetwork()
        If _net Is Nothing Then Return
        Dim ev As New NetEvent()
        While _net IsNot Nothing AndAlso _net.Poll(ev)
            Select Case ev.Kind
                Case NetEventKind.Connected
                    Send(Protocol.Join(_myName, _roomId))
                    _lastPingMs = Environment.TickCount64
                Case NetEventKind.Message
                    HandleMessage(ev.Text)
                Case NetEventKind.Failed
                    OnConnectionLost(ev.Text)
                    Return
            End Select
        End While
        If _net Is Nothing OrElse Not _net.IsOpen Then Return
        ' 死活監視: 5秒ごとに PING、15秒受信が無ければ切断とみなす
        Dim nowMs = Environment.TickCount64
        If nowMs - _lastPingMs >= Protocol.PingIntervalMs Then
            _lastPingMs = nowMs
            Send(Protocol.Ping(nowMs))
        End If
        If _net.MillisSinceReceive > Protocol.ReceiveTimeoutMs Then
            OnConnectionLost("サーバーからの応答がありません（タイムアウト）。")
        End If
    End Sub

    Private Sub OnConnectionLost(message As String)
        LeaveToTitle(message, sendLeave:=False)
    End Sub

    Private Sub HandleMessage(text As String)
        Dim m = Protocol.Parse(text)
        If m Is Nothing Then Return
        Select Case m.Type
            Case "JOINED"
                _myPlayer = m.Int("player", 1)
                _roomId = m.Str("room", _roomId)
                If _screen = Screen.Connecting Then _screen = Screen.Waiting
            Case "WAITING"
                If _screen = Screen.Connecting OrElse _screen = Screen.Waiting Then _screen = Screen.Waiting
            Case "START"
                StartMatch(m.Int("seed"), Math.Max(0, m.Int("countdown", 3)), m.Str("opponent", "あいて"))
            Case "OPP_PIECE"
                OnOppPiece(m.Int("x"), m.Int("y"), m.Int("rot"))
            Case "OPP_STATE"
                OnOppState(m.Str("board"), m.Int("chain"))
            Case "GARBAGE"
                Dim n = m.Int("count")
                If n > 0 AndAlso _screen = Screen.Playing AndAlso _game.Phase <> PlayerGame.Phases.Dead Then
                    _game.AddGarbage(n)
                End If
            Case "RESULT"
                ShowResult(m.Int("winner"), m.Str("reason", "gameover"))
            Case "OPP_LEFT"
                _oppLeft = True
                _oppWantsRematch = False
                Toast("相手の接続が切れました")
            Case "OPP_REMATCH"
                _oppWantsRematch = True
            Case "ERROR"
                Dim code = m.Int("code")
                If (code = 1003 OrElse code = 1004) AndAlso
                   (_screen = Screen.Countdown OrElse _screen = Screen.Playing OrElse _screen = Screen.Result) Then
                    ' 決着の直後などは、相手の RESULT と自分の送信が行き違って 1004 になることがある。
                    ' サーバーはそのメッセージを無視するだけなので、対戦は続ける。
                    If _screen <> Screen.Result Then Toast($"サーバーがメッセージを受け付けませんでした（{code}）")
                Else
                    LeaveToTitle(ErrorText(code, m.Str("message")))
                End If
            Case "PONG"
                If m.Has("t") Then _rttMs = Math.Max(0, Environment.TickCount64 - m.Numbers("t"))
        End Select
    End Sub

    Private Shared Function ErrorText(code As Integer, message As String) As String
        Select Case code
            Case 1001 : Return "そのルームは満員です（エラー1001）。別のルームIDを入力してください。"
            Case 1002 : Return $"ニックネームが正しくありません（エラー1002）。1〜{Protocol.MaxNameLength}文字で入力してください。"
            Case 1003 : Return "サーバーが受け付けられないメッセージを送りました（エラー1003）。"
            Case 1004 : Return "サーバーと状態が合いませんでした（エラー1004）。"
        End Select
        Return $"サーバーからエラーが返されました（{code}: {message}）。"
    End Function

    ''' <summary>接続を閉じてタイトルへ戻る。message が空でなければタイトルに表示する。</summary>
    Private Sub LeaveToTitle(message As String, Optional sendLeave As Boolean = True)
        If _net IsNot Nothing Then
            If sendLeave Then _net.Send(Protocol.Simple("LEAVE"))
            _net.Close()
            _net = Nothing
        End If
        _game.StopGame()
        _screen = Screen.Title
        ShowTitleMessage(message, message <> "")
        ShowTitleControls(True)
    End Sub

    ' =====================================================================
    ' 対戦の開始・進行
    ' =====================================================================

    Private Sub StartMatch(seed As Integer, countdown As Integer, opponent As String)
        _seed = seed
        _oppName = opponent
        _previewSeq = New PieceSequence(seed)
        _oppSeq = New PieceSequence(seed)
        _game.StopGame()
        _game.Field.Clear()
        _game.PendingGarbage = 0
        _game.Phase = PlayerGame.Phases.Idle
        _oppField.Clear()
        _oppIndex = 0
        _oppStateSincePiece = False
        _oppPieceVisible = False
        _oppChain = 0
        _oppMaxChain = 0
        _oppLeft = False
        _oppDeadFrames = -1
        _myDeadFrames = -1
        _iWantRematch = False
        _oppWantsRematch = False
        _gameOverSent = False
        _myChainShowFrames = 0
        _oppChainShowFrames = 0
        _effects.Clear()
        _texts.Clear()
        _countdownTotal = countdown * 60
        _countdownFrames = _countdownTotal
        _screen = Screen.Countdown
        _input.Reset()
        ShowTitleControls(False)
        _sound?.Play(Sfx.Matching)
        If _countdownTotal = 0 Then BeginPlaying()
    End Sub

    Private Sub UpdateCountdown()
        _countdownFrames -= 1
        If _countdownFrames <= 0 Then BeginPlaying()
    End Sub

    Private Sub BeginPlaying()
        _screen = Screen.Playing
        _input.Reset()
        _game.Start(_seed)
        _goFrames = 50
        DrainGameEvents()
    End Sub

    Private Sub UpdatePlaying()
        If _input.WasPressed(Keys.Escape) Then
            If _frame <= _escArmedUntil Then
                LeaveToTitle("")
                Return
            End If
            _escArmedUntil = _frame + 90
            Toast("もう一度 Esc でタイトルに戻ります")
        End If
        Dim inp = If(_autoplay, AutoplayInput(), _input.Sample(Form.ActiveForm Is Me))
        _game.Update(inp)
        DrainGameEvents()
        If _game.Phase = PlayerGame.Phases.Dead AndAlso _myDeadFrames < 0 Then _myDeadFrames = 0
        UpdateBoardsWhileIdle()
    End Sub

    Private Sub UpdateBoardsWhileIdle()
        If _myDeadFrames >= 0 Then _myDeadFrames += 1
        If _oppDeadFrames >= 0 Then _oppDeadFrames += 1
        If _myChainShowFrames > 0 Then _myChainShowFrames -= 1
        If _oppChainShowFrames > 0 Then _oppChainShowFrames -= 1
    End Sub

    ''' <summary>自分のゲームで起きたことを、送信・効果音・演出に振り分ける。</summary>
    Private Sub DrainGameEvents()
        For Each ev In _game.Events
            Select Case ev.Kind
                Case GameEventKind.PieceChanged
                    Dim p = _game.Current
                    Send(Protocol.PieceMsg(p.X, Math.Max(0, p.Y), p.Rot))
                Case GameEventKind.Moved
                    _sound?.Play(Sfx.Move)
                Case GameEventKind.Rotated
                    _sound?.Play(Sfx.Rotate)
                Case GameEventKind.Locked
                    _sound?.Play(Sfx.Fallen)
                    If ev.Value = 1 Then _shake = Math.Max(_shake, 2.5F)
                Case GameEventKind.StateChanged
                    Send(Protocol.State(_game.Field.ToBoardString(), ev.Value))
                Case GameEventKind.EraseStarted
                    Send(Protocol.Clear(ev.Value, ev.Value2))
                    _sound?.Play(Sfx.Rensa)
                    _myChainShow = ev.Value2
                    _myChainShowFrames = 150
                    SpawnEraseEffects(True, _game.ErasingCells, _game.Field)
                    Dim c = _game.LastEraseCenter
                    AddChainText(True, c.X, c.Y, ev.Value2)
                Case GameEventKind.GarbageLanded
                    _sound?.Play(Sfx.Ozyama)
                    _shake = Math.Max(_shake, Math.Min(8.0F, 2 + ev.Value / 4.0F))
                Case GameEventKind.GameOver
                    If Not _gameOverSent Then
                        _gameOverSent = True
                        Send(Protocol.Simple("GAME_OVER"))
                    End If
            End Select
        Next
        _game.Events.Clear()
    End Sub

    ' ---- 相手の盤面 ----

    Private Sub OnOppPiece(x As Integer, y As Integer, rot As Integer)
        If _screen <> Screen.Playing OrElse _oppSeq Is Nothing Then Return
        ' 盤面（STATE）の後に来た最初の PIECE は新しいうどん。落下順は自分と同じなので種類がわかる
        If _oppStateSincePiece Then
            _oppIndex += 1
            _oppStateSincePiece = False
        End If
        Dim pair = _oppSeq(_oppIndex)
        Dim p As Piece
        p.X = Math.Clamp(x, 0, Field.Cols - 1)
        p.Y = Math.Clamp(y, 0, Field.Rows - 1)
        p.Rot = ((rot Mod 4) + 4) Mod 4
        p.Axis = pair.Axis
        p.Child = pair.Child
        _oppPiece = p
        _oppPieceVisible = True
    End Sub

    Private Sub OnOppState(board As String, chain As Integer)
        If _screen <> Screen.Playing AndAlso _screen <> Screen.Result Then Return
        Dim before = _oppField.CopyCells()
        If Not _oppField.LoadBoardString(board) Then Return
        _oppStateSincePiece = True
        _oppPieceVisible = False
        ' 消えたうどんの位置に演出を出す
        If chain > 0 AndAlso chain <> _oppChain Then
            SpawnOppEraseEffects(CountRemoved(before))
            _oppChainShowFrames = 150
            _oppMaxChain = Math.Max(_oppMaxChain, chain)
            _sound?.Play(Sfx.Rensa)
            AddChainText(False, 2.5F, 6, chain)
        End If
        _oppChain = chain
    End Sub

    ''' <summary>列ごとに減ったセルの数から、消えた位置を推定する（演出用）。</summary>
    Private Function CountRemoved(before As Byte(,)) As List(Of (P As Point, Kind As Byte))
        Dim list As New List(Of (P As Point, Kind As Byte))
        For x = 0 To Field.Cols - 1
            Dim cntBefore = 0, cntAfter = 0
            For y = 0 To Field.Rows - 1
                If before(x, y) <> 0 Then cntBefore += 1
                If _oppField(x, y) <> 0 Then cntAfter += 1
            Next
            Dim diff = cntBefore - cntAfter
            For k = 0 To diff - 1
                Dim y = Field.Rows - cntAfter - 1 - k
                If y >= 0 Then list.Add((New Point(x, y), CByte(Math.Max(1, CInt(before(x, Math.Max(0, y)))))))
            Next
        Next
        Return list
    End Function

    ' ---- 結果 ----

    Private Sub ShowResult(winner As Integer, reason As String)
        If _screen <> Screen.Playing AndAlso _screen <> Screen.Countdown Then Return
        _game.StopGame()
        _won = (winner = _myPlayer)
        _resultReason = reason
        _resultFrames = 0
        _screen = Screen.Result
        If _won Then
            If reason <> "left" AndAlso _oppDeadFrames < 0 Then _oppDeadFrames = 0
            _sound?.Play(Sfx.Win)
        Else
            If _myDeadFrames < 0 Then _myDeadFrames = 0
            _sound?.Play(Sfx.Lose)
        End If
        If reason = "left" Then _oppLeft = True
    End Sub

    Private Function CanRematch() As Boolean
        Return _screen = Screen.Result AndAlso Not _oppLeft AndAlso _net IsNot Nothing
    End Function

    Private Sub RequestRematch()
        _iWantRematch = True
        Send(Protocol.Simple("REMATCH"))
    End Sub

    ' =====================================================================
    ' 自動操作（テスト用: --autoplay）
    ' =====================================================================

    Private _botTargetX As Integer
    Private _botTargetRot As Integer
    Private _botIndex As Integer = -1
    Private _botWait As Integer
    Private ReadOnly _botRng As New Random()

    Private Function AutoplayInput() As GameInput
        Dim g As GameInput
        If _game.Phase <> PlayerGame.Phases.Falling Then Return g
        Dim p = _game.Current
        If _botIndex <> _game.PieceIndex Then
            _botIndex = _game.PieceIndex
            ' 一番低い列を狙う（同じ高さならランダム）
            Dim best = -1, bestScore = Integer.MinValue
            For x = 0 To Field.Cols - 1
                Dim score = _game.Field.LowestFree(x) * 10 + _botRng.Next(15)
                If score > bestScore Then bestScore = score : best = x
            Next
            _botTargetX = best
            _botTargetRot = _botRng.Next(4)
            _botWait = 6 + _botRng.Next(10)
        End If
        If _frame Mod 3 <> 0 Then Return g
        If p.Rot <> _botTargetRot Then
            g.RotateCW = True
        ElseIf p.X < _botTargetX Then
            g.MoveRight = True
        ElseIf p.X > _botTargetX Then
            g.MoveLeft = True
        ElseIf _botWait > 0 Then
            _botWait -= 1
        Else
            g.HardDrop = True
        End If
        Return g
    End Function
End Class
