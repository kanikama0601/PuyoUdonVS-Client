Imports System.Runtime.InteropServices

''' <summary>
''' キーボード入力。押した瞬間はキーイベントで確実に拾い（フレーム間の短い押下も取りこぼさない）、
''' 押しっぱなしは GetAsyncKeyState で毎フレーム確認する。左右移動・回転・高速落下はキーリピートに対応。
''' </summary>
Public Class InputManager
    <DllImport("user32.dll")>
    Private Shared Function GetAsyncKeyState(vKey As Integer) As Short
    End Function

    ' キーリピート（フレーム数）
    Private Const MoveDelay As Integer = 9
    Private Const MoveRepeat As Integer = 2
    Private Const RotateDelay As Integer = 16
    Private Const RotateRepeat As Integer = 8

    Private ReadOnly _pressed As New HashSet(Of Keys)
    Private ReadOnly _held As New Dictionary(Of Keys, Integer)
    ''' <summary>キーの割り当て。</summary>
    Public Property Config As New KeyConfig()

    ''' <summary>このフレームで押されたキー（OS の自動リピートは除く）。</summary>
    Public Function PressedKeys() As Keys()
        Return _pressed.ToArray()
    End Function

    Private _leftFrames, _rightFrames, _cwFrames, _ccwFrames As Integer

    ''' <summary>キーが押された（OS の自動リピートは除く）。</summary>
    Public Sub OnKeyDown(k As Keys)
        _pressed.Add(k)
    End Sub

    Public Function WasPressed(ParamArray ks As Keys()) As Boolean
        For Each k In ks
            If _pressed.Contains(k) Then Return True
        Next
        Return False
    End Function

    Private Function IsHeld(active As Boolean, ks As Keys()) As Boolean
        If Not active Then Return False
        For Each k In ks
            If (GetAsyncKeyState(CInt(k)) And &H8000) <> 0 Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' 1フレームぶんの操作を作る。active=False（ウィンドウが非アクティブ）のときは押しっぱなしを無視。
    ''' </summary>
    Public Function Sample(active As Boolean) As GameInput
        Dim g As GameInput
        Dim LeftKeys = Config.KeysOf(GameAction.MoveLeft)
        Dim RightKeys = Config.KeysOf(GameAction.MoveRight)
        Dim DownKeys = Config.KeysOf(GameAction.SoftDrop)
        Dim CwKeys = Config.KeysOf(GameAction.RotateCW)
        Dim CcwKeys = Config.KeysOf(GameAction.RotateCCW)
        Dim HardKeys = Config.KeysOf(GameAction.HardDrop)
        g.MoveLeft = Repeat(WasPressed(LeftKeys), IsHeld(active, LeftKeys), _leftFrames, MoveDelay, MoveRepeat)
        g.MoveRight = Repeat(WasPressed(RightKeys), IsHeld(active, RightKeys), _rightFrames, MoveDelay, MoveRepeat)
        If g.MoveLeft AndAlso g.MoveRight Then
            ' 両方押されたら後から押したほうを優先
            If _leftFrames < _rightFrames Then g.MoveRight = False Else g.MoveLeft = False
        End If
        g.RotateCW = Repeat(WasPressed(CwKeys), IsHeld(active, CwKeys), _cwFrames, RotateDelay, RotateRepeat)
        g.RotateCCW = Repeat(WasPressed(CcwKeys), IsHeld(active, CcwKeys), _ccwFrames, RotateDelay, RotateRepeat)
        g.HardDrop = WasPressed(HardKeys)
        g.SoftDrop = WasPressed(DownKeys) OrElse IsHeld(active, DownKeys)
        Return g
    End Function

    Private Shared Function Repeat(pressed As Boolean, held As Boolean, ByRef frames As Integer, delay As Integer, interval As Integer) As Boolean
        If pressed Then
            frames = 1
            Return True
        End If
        If Not held Then
            frames = 0
            Return False
        End If
        If frames = 0 Then
            ' 押した瞬間のイベントを取り逃した場合
            frames = 1
            Return True
        End If
        frames += 1
        Return frames > delay AndAlso (frames - delay) Mod interval = 1 Mod interval
    End Function

    ''' <summary>フレームの最後に呼ぶ。押された瞬間の記録を消す。</summary>
    Public Sub EndFrame()
        _pressed.Clear()
    End Sub

    Public Sub Reset()
        _pressed.Clear()
        _leftFrames = 0 : _rightFrames = 0 : _cwFrames = 0 : _ccwFrames = 0
    End Sub
End Class
