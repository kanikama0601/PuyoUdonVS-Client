''' <summary>1フレームぶんの操作（キーリピート処理済み）。</summary>
Public Structure GameInput
    Public MoveLeft As Boolean
    Public MoveRight As Boolean
    Public RotateCW As Boolean
    Public RotateCCW As Boolean
    Public HardDrop As Boolean
    Public SoftDrop As Boolean
End Structure

Public Enum GameEventKind
    ''' <summary>新しいうどんが出現した。</summary>
    Spawned
    ''' <summary>落下中のうどんの位置・向きが変わった（PIECE 送信用）。</summary>
    PieceChanged
    Moved
    Rotated
    ''' <summary>うどんを設置した。Value=1 なら高速落下での設置。</summary>
    Locked
    ''' <summary>盤面が確定した（STATE 送信用）。Value=連鎖数。</summary>
    StateChanged
    ''' <summary>消去が始まった（CLEAR 送信用）。Value=消去数、Value2=連鎖数。</summary>
    EraseStarted
    ''' <summary>おじゃまそばが降ってきた。Value=個数。</summary>
    GarbageDropped
    ''' <summary>降ってきたおじゃまそばが着地した。Value=個数。</summary>
    GarbageLanded
    ''' <summary>連鎖が終わった。Value=最終の連鎖数。</summary>
    ChainEnded
    GameOver
End Enum

Public Structure GameEvent
    Public Kind As GameEventKind
    Public Value As Integer
    Public Value2 As Integer
    Public Value3 As Integer
    Public Sub New(kind As GameEventKind, Optional value As Integer = 0, Optional value2 As Integer = 0, Optional value3 As Integer = 0)
        Me.Kind = kind : Me.Value = value : Me.Value2 = value2 : Me.Value3 = value3
    End Sub
End Structure

''' <summary>アニメーション中の落下セル。</summary>
Public Class FallingCell
    Public X As Integer
    Public Y As Single
    Public ToY As Integer
    Public Kind As Byte
    Public Vy As Single
End Class

''' <summary>
''' 自分の盤面のゲーム進行（落下・回転・設置・消去・連鎖・おじゃまそば・敗北判定）。
''' 60fps の固定フレームで Update を呼ぶ。
''' </summary>
Public Class PlayerGame
    Public Enum Phases
        Idle
        Falling
        Settling
        Erasing
        Dead
    End Enum

    ' ---- 調整用の定数（フレーム数, 60fps） ----
    Private Const FallIntervalStart As Integer = 34
    Private Const FallIntervalMin As Integer = 6
    Private Const SpeedUpEvery As Integer = 60 * 15
    Private Const SoftDropInterval As Integer = 2
    Private Const LockDelay As Integer = 32
    Private Const MaxLockResets As Integer = 12
    Public Const EraseFrames As Integer = 32
    Private Const QuickTurnWindow As Integer = 18
    ''' <summary>1回の設置後に降るおじゃまそばの上限（5段）。</summary>
    Public Const MaxGarbagePerDrop As Integer = 30

    Public ReadOnly Field As New Field()
    Public ReadOnly Events As New List(Of GameEvent)
    Public ReadOnly FallingCells As New List(Of FallingCell)
    Public ReadOnly ErasingCells As New List(Of Point)

    Private _seq As PieceSequence
    Private ReadOnly _garbageRng As New Random()

    Public Property Phase As Phases = Phases.Idle
    Public Property Current As Piece
    Public Property PieceIndex As Integer
    Public Property Chain As Integer
    Public Property MaxChain As Integer
    Public Property PendingGarbage As Integer
    Public Property EraseTimer As Integer
    Public Property FrameCount As Integer
    Public Property DeadFrames As Integer
    ''' <summary>落下中のうどんの見た目上のずれ（0〜1セル）。なめらかな落下表示用。</summary>
    Public Property FallFraction As Single
    ''' <summary>直近の消去で表示する連鎖数と位置（演出用）。</summary>
    Public Property LastEraseCenter As PointF

    Private _fallCounter As Integer
    Private _lockTimer As Integer
    Private _lockResets As Integer
    Private _lastRotateFail As Integer = -1000
    Private _lastSentPiece As (X As Integer, Y As Integer, R As Integer) = (-99, -99, -99)
    Private _garbageDropping As Boolean
    Private _garbageCount As Integer

    Public Sub Start(seed As Integer)
        _seq = New PieceSequence(seed)
        Field.Clear()
        Events.Clear()
        FallingCells.Clear()
        ErasingCells.Clear()
        PieceIndex = 0
        Chain = 0
        MaxChain = 0
        PendingGarbage = 0
        FrameCount = 0
        DeadFrames = 0
        _garbageDropping = False
        Spawn()
    End Sub

    Public Sub StopGame()
        If Phase <> Phases.Dead Then Phase = Phases.Idle
    End Sub

    Public ReadOnly Property Sequence As PieceSequence
        Get
            Return _seq
        End Get
    End Property

    Public Function NextPair(offset As Integer) As PiecePair
        Return _seq(PieceIndex + offset)
    End Function

    Public Sub AddGarbage(count As Integer)
        If count > 0 Then PendingGarbage += count
    End Sub

    Private ReadOnly Property FallInterval As Integer
        Get
            Return Math.Max(FallIntervalMin, FallIntervalStart - (FrameCount \ SpeedUpEvery) * 3)
        End Get
    End Property

    Public Sub Update(input As GameInput)
        Select Case Phase
            Case Phases.Idle
                Return
            Case Phases.Dead
                DeadFrames += 1
                Return
        End Select
        FrameCount += 1
        Select Case Phase
            Case Phases.Falling
                UpdateFalling(input)
            Case Phases.Settling
                UpdateSettling()
            Case Phases.Erasing
                UpdateErasing()
        End Select
    End Sub

    ' ================= 落下中 =================

    Private Sub Spawn()
        If Field.IsSpawnBlocked() Then
            Phase = Phases.Dead
            DeadFrames = 0
            Events.Add(New GameEvent(GameEventKind.GameOver))
            Return
        End If
        Dim pair = _seq(PieceIndex)
        Dim p As Piece
        p.X = Field.SpawnX
        p.Y = Field.SpawnY
        p.Rot = 0
        p.Axis = pair.Axis
        p.Child = pair.Child
        Current = p
        _fallCounter = 0
        _lockTimer = 0
        _lockResets = 0
        FallFraction = 0
        Phase = Phases.Falling
        Events.Add(New GameEvent(GameEventKind.Spawned))
        NotifyPiece(force:=True)
    End Sub

    Private Sub UpdateFalling(input As GameInput)
        If input.RotateCW Then TryRotate(1)
        If input.RotateCCW Then TryRotate(-1)
        If input.MoveLeft Then TryMove(-1)
        If input.MoveRight Then TryMove(1)

        If input.HardDrop Then
            Dim p = Current
            While p.Moved(0, 1).Fits(Field)
                p = p.Moved(0, 1)
            End While
            Current = p
            NotifyPiece()
            Lock(hard:=True)
            Return
        End If

        Dim grounded = Not Current.Moved(0, 1).Fits(Field)
        If grounded Then
            FallFraction = 0
            _fallCounter = 0
            _lockTimer += If(input.SoftDrop, LockDelay, 1)
            If _lockTimer >= LockDelay Then
                Lock(hard:=False)
            End If
            Return
        End If

        Dim interval = If(input.SoftDrop, SoftDropInterval, FallInterval)
        _fallCounter += 1
        If _fallCounter >= interval Then
            _fallCounter = 0
            Current = Current.Moved(0, 1)
            _lockTimer = 0
            NotifyPiece()
        End If
        FallFraction = If(Current.Moved(0, 1).Fits(Field), CSng(_fallCounter) / interval, 0F)
    End Sub

    Private Sub TryMove(dx As Integer)
        Dim p = Current.Moved(dx, 0)
        If p.Fits(Field) Then
            Current = p
            ResetLock()
            Events.Add(New GameEvent(GameEventKind.Moved))
            NotifyPiece()
        End If
    End Sub

    Private Sub TryRotate(dir As Integer)
        Dim p = Current
        p.Rot = (Current.Rot + dir + 4) Mod 4
        Dim ok = False
        If p.Fits(Field) Then
            ok = True
        Else
            ' 壁や床に当たる場合は、軸を反対側へずらして回転する
            Dim kick = p.Moved(-Piece.OffsetX(p.Rot), -Piece.OffsetY(p.Rot))
            If kick.Fits(Field) Then
                p = kick
                ok = True
            End If
        End If
        If Not ok Then
            ' 左右とも塞がっている縦向きのとき、素早く2回押すと上下を入れ替える（クイックターン）
            If FrameCount - _lastRotateFail <= QuickTurnWindow Then
                Dim q = Current
                q.Rot = (Current.Rot + 2) Mod 4
                If q.Fits(Field) Then
                    p = q : ok = True
                Else
                    Dim q2 = q
                    q2.X = Current.ChildX
                    q2.Y = Current.ChildY
                    If q2.Fits(Field) Then p = q2 : ok = True
                End If
                _lastRotateFail = -1000
            Else
                _lastRotateFail = FrameCount
            End If
        End If
        If ok Then
            Current = p
            ResetLock()
            Events.Add(New GameEvent(GameEventKind.Rotated))
            NotifyPiece()
        End If
    End Sub

    Private Sub ResetLock()
        If _lockTimer > 0 AndAlso _lockResets < MaxLockResets Then
            _lockTimer = 0
            _lockResets += 1
        End If
    End Sub

    Private Sub NotifyPiece(Optional force As Boolean = False)
        Dim c = Current
        Dim key As (X As Integer, Y As Integer, R As Integer) = (c.X, c.Y, c.Rot)
        If force OrElse Not key.Equals(_lastSentPiece) Then
            _lastSentPiece = key
            Events.Add(New GameEvent(GameEventKind.PieceChanged))
        End If
    End Sub

    ''' <summary>落下中のうどんが着地する位置（ゴースト表示用）。</summary>
    Public Function GhostCells() As (A As Point, C As Point)
        Dim p = Current
        If p.X = p.ChildX Then
            Dim bottom = Field.LowestFree(p.X)
            Dim upper = bottom - 1
            If p.Rot = 0 Then
                Return (New Point(p.X, bottom), New Point(p.X, upper))
            Else
                Return (New Point(p.X, upper), New Point(p.X, bottom))
            End If
        End If
        Return (New Point(p.X, Field.LowestFree(p.X)), New Point(p.ChildX, Field.LowestFree(p.ChildX)))
    End Function

    ' ================= 設置・重力 =================

    Private Sub Lock(hard As Boolean)
        Dim p = Current
        ' 盤面より上にはみ出たうどんは消える
        If p.Y >= 0 Then Field(p.X, p.Y) = p.Axis
        If p.ChildY >= 0 Then Field(p.ChildX, p.ChildY) = p.Child
        PieceIndex += 1
        FallFraction = 0
        Events.Add(New GameEvent(GameEventKind.Locked, If(hard, 1, 0)))
        Chain = 0
        _garbageDropping = False
        BeginSettle()
    End Sub

    Private Sub BeginSettle()
        FallingCells.Clear()
        For Each m In Field.ApplyGravity()
            FallingCells.Add(New FallingCell With {.X = m.X, .Y = m.FromY, .ToY = m.ToY, .Kind = m.Kind, .Vy = 0.3F})
        Next
        Phase = Phases.Settling
        If FallingCells.Count = 0 Then OnSettled()
    End Sub

    Private Sub UpdateSettling()
        Dim allDone = True
        For Each c In FallingCells
            If c.Y < c.ToY Then
                c.Vy = Math.Min(c.Vy + 0.06F, 0.9F)
                c.Y = Math.Min(c.Y + c.Vy, CSng(c.ToY))
                If c.Y < c.ToY Then allDone = False
            End If
        Next
        If allDone Then
            FallingCells.Clear()
            OnSettled()
        End If
    End Sub

    ''' <summary>アニメーション中で盤面に描かない（落下中の）セルか。</summary>
    Public Function IsCellAnimating(x As Integer, y As Integer) As Boolean
        For Each c In FallingCells
            If c.X = x AndAlso c.ToY = y Then Return True
        Next
        Return False
    End Function

    Private Sub OnSettled()
        Events.Add(New GameEvent(GameEventKind.StateChanged, Chain))
        If _garbageDropping Then
            _garbageDropping = False
            Events.Add(New GameEvent(GameEventKind.GarbageLanded, _garbageCount))
            Spawn()
            Return
        End If
        Dim erased = 0, groups = 0
        Dim cells = Field.FindErase(erased, groups)
        If cells.Count > 0 Then
            Chain += 1
            MaxChain = Math.Max(MaxChain, Chain)
            ErasingCells.Clear()
            ErasingCells.AddRange(cells)
            Dim sx = 0F, sy = 0F
            For Each c In cells
                sx += c.X : sy += c.Y
            Next
            LastEraseCenter = New PointF(sx / cells.Count, sy / cells.Count)
            EraseTimer = 0
            Phase = Phases.Erasing
            ' 相殺: 今回の攻撃力（サーバーと同じ式）ぶん、予告中のおじゃまそばを打ち消す
            Dim attack = Math.Max(1, erased * Chain \ 2)
            Dim offset = Math.Min(PendingGarbage, attack)
            PendingGarbage -= offset
            Events.Add(New GameEvent(GameEventKind.EraseStarted, erased, Chain, offset))
            Return
        End If
        If Chain > 0 Then
            Events.Add(New GameEvent(GameEventKind.ChainEnded, Chain))
            Chain = 0
        End If
        ' 連鎖が終わった。予告されたおじゃまそばがあれば降らせる
        If PendingGarbage > 0 Then
            DropGarbage()
            Return
        End If
        Spawn()
    End Sub

    Private Sub UpdateErasing()
        EraseTimer += 1
        If EraseTimer >= EraseFrames Then
            For Each c In ErasingCells
                Field(c.X, c.Y) = Field.Empty
            Next
            ErasingCells.Clear()
            BeginSettle()
        End If
    End Sub

    ' ================= おじゃまそば =================

    Private Sub DropGarbage()
        Dim n = Math.Min(PendingGarbage, MaxGarbagePerDrop)
        PendingGarbage -= n
        Dim perCol(Field.Cols - 1) As Integer
        Dim fullRows = n \ Field.Cols
        For x = 0 To Field.Cols - 1
            perCol(x) = fullRows
        Next
        Dim rest = n Mod Field.Cols
        Dim cols = Enumerable.Range(0, Field.Cols).OrderBy(Function(i) _garbageRng.Next()).Take(rest)
        For Each x In cols
            perCol(x) += 1
        Next
        FallingCells.Clear()
        Dim dropped = 0
        For x = 0 To Field.Cols - 1
            Dim top = Field.LowestFree(x)
            For k = 0 To perCol(x) - 1
                Dim y = top - k
                If y < 0 Then Exit For ' 積み上がりすぎた分は消える
                Field(x, y) = Field.Garbage
                dropped += 1
                ' 盤面の上から降り注ぐ演出（少しずつ遅れて落ちる）
                FallingCells.Add(New FallingCell With {
                    .X = x, .Y = -1 - k - CSng(_garbageRng.NextDouble()) * 0.8F, .ToY = y,
                    .Kind = Field.Garbage, .Vy = 0.2F})
            Next
        Next
        Events.Add(New GameEvent(GameEventKind.GarbageDropped, n))
        _garbageCount = n
        _garbageDropping = True
        Phase = Phases.Settling
        If FallingCells.Count = 0 Then OnSettled()
    End Sub
End Class
