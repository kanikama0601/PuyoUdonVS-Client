''' <summary>
''' 2つ1組で落ちてくるうどん。X,Y は軸うどんの位置、Rot は子うどんの向き
''' （0=上、1=右、2=下、3=左）。
''' </summary>
Public Structure Piece
    Public X As Integer
    Public Y As Integer
    Public Rot As Integer
    Public Axis As Byte
    Public Child As Byte

    Public Shared ReadOnly OffsetX As Integer() = {0, 1, 0, -1}
    Public Shared ReadOnly OffsetY As Integer() = {-1, 0, 1, 0}

    Public ReadOnly Property ChildX As Integer
        Get
            Return X + OffsetX(Rot)
        End Get
    End Property

    Public ReadOnly Property ChildY As Integer
        Get
            Return Y + OffsetY(Rot)
        End Get
    End Property

    Public Function Fits(f As Field) As Boolean
        Return f.IsFree(X, Y) AndAlso f.IsFree(ChildX, ChildY)
    End Function

    Public Function Moved(dx As Integer, dy As Integer) As Piece
        Dim p = Me
        p.X += dx
        p.Y += dy
        Return p
    End Function
End Structure

''' <summary>うどんセット（軸と子の種類）。</summary>
Public Structure PiecePair
    Public Axis As Byte
    Public Child As Byte
End Structure

''' <summary>
''' 落下順。サーバーから受け取った seed を .NET の Random の初期値にして生成するので、
''' 両プレイヤーに同じ順序のうどんセットが供給される。
''' 1セットごとに 軸 = Next(1, 5)、子 = Next(1, 5) の順で乱数を引く。
''' </summary>
Public Class PieceSequence
    Private ReadOnly _rng As Random
    Private ReadOnly _list As New List(Of PiecePair)

    Public Sub New(seed As Integer)
        _rng = New Random(seed)
    End Sub

    Default Public ReadOnly Property Item(index As Integer) As PiecePair
        Get
            While _list.Count <= index
                Dim p As PiecePair
                p.Axis = CByte(_rng.Next(1, Field.ColorCount + 1))
                p.Child = CByte(_rng.Next(1, Field.ColorCount + 1))
                _list.Add(p)
            End While
            Return _list(index)
        End Get
    End Property
End Class
