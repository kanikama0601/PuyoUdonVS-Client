Imports System.Text

''' <summary>
''' 盤面（6列×12行）。セルの値は 0=空、1〜4=うどんの種類、5=おじゃまそば。
''' 座標は x=列(0〜5、左から)、y=行(0〜11、上から)。
''' </summary>
Public Class Field
    Public Const Cols As Integer = 6
    Public Const Rows As Integer = 12
    Public Const Empty As Byte = 0
    Public Const Garbage As Byte = 5
    Public Const ColorCount As Integer = 4
    ''' <summary>出現位置（軸うどんの列・行）。ここが埋まっていると新しいうどんを置けない。</summary>
    Public Const SpawnX As Integer = 2
    Public Const SpawnY As Integer = 0
    ''' <summary>4つ以上つながると消える。</summary>
    Public Const EraseCount As Integer = 4

    Private ReadOnly _cells(Cols - 1, Rows - 1) As Byte

    Default Public Property Cell(x As Integer, y As Integer) As Byte
        Get
            Return _cells(x, y)
        End Get
        Set(value As Byte)
            _cells(x, y) = value
        End Set
    End Property

    Public Shared Function InBounds(x As Integer, y As Integer) As Boolean
        Return x >= 0 AndAlso x < Cols AndAlso y >= 0 AndAlso y < Rows
    End Function

    ''' <summary>落下中のうどんが入れるか。盤面より上（y&lt;0）は1行ぶんだけ空きとして扱う。</summary>
    Public Function IsFree(x As Integer, y As Integer) As Boolean
        If x < 0 OrElse x >= Cols Then Return False
        If y >= Rows Then Return False
        If y < 0 Then Return y >= -1
        Return _cells(x, y) = Empty
    End Function

    Public Sub Clear()
        Array.Clear(_cells)
    End Sub

    Public Function IsSpawnBlocked() As Boolean
        Return _cells(SpawnX, SpawnY) <> Empty
    End Function

    ''' <summary>列の中で一番下の空きセルの行（無ければ -1）。</summary>
    Public Function LowestFree(x As Integer) As Integer
        For y = Rows - 1 To 0 Step -1
            If _cells(x, y) = Empty Then Return y
        Next
        Return -1
    End Function

    ''' <summary>宙に浮いたセルを落とす。動いたセルの一覧（アニメーション用）を返す。</summary>
    Public Function ApplyGravity() As List(Of CellMove)
        Dim moves As New List(Of CellMove)
        For x = 0 To Cols - 1
            Dim writeY = Rows - 1
            For y = Rows - 1 To 0 Step -1
                Dim c = _cells(x, y)
                If c = Empty Then Continue For
                If y <> writeY Then
                    _cells(x, writeY) = c
                    _cells(x, y) = Empty
                    moves.Add(New CellMove(x, y, writeY, c))
                End If
                writeY -= 1
            Next
        Next
        Return moves
    End Function

    ''' <summary>
    ''' 消えるセルを探す。同じ種類のうどんが縦横に4つ以上つながったグループと、
    ''' それに隣接するおじゃまそばが対象。うどんの消去数（おじゃまそばは数えない）も返す。
    ''' </summary>
    Public Function FindErase(ByRef erasedUdon As Integer, ByRef groupCount As Integer) As List(Of Point)
        Dim visited(Cols - 1, Rows - 1) As Boolean
        Dim marked(Cols - 1, Rows - 1) As Boolean
        Dim result As New List(Of Point)
        erasedUdon = 0
        groupCount = 0
        Dim stack As New Stack(Of Point)
        Dim group As New List(Of Point)
        For sx = 0 To Cols - 1
            For sy = 0 To Rows - 1
                Dim c = _cells(sx, sy)
                If c = Empty OrElse c = Garbage OrElse visited(sx, sy) Then Continue For
                group.Clear()
                stack.Push(New Point(sx, sy))
                visited(sx, sy) = True
                While stack.Count > 0
                    Dim p = stack.Pop()
                    group.Add(p)
                    For Each d In Dirs
                        Dim nx = p.X + d.X, ny = p.Y + d.Y
                        If InBounds(nx, ny) AndAlso Not visited(nx, ny) AndAlso _cells(nx, ny) = c Then
                            visited(nx, ny) = True
                            stack.Push(New Point(nx, ny))
                        End If
                    Next
                End While
                If group.Count >= EraseCount Then
                    groupCount += 1
                    erasedUdon += group.Count
                    For Each p In group
                        marked(p.X, p.Y) = True
                        result.Add(p)
                    Next
                End If
            Next
        Next
        If result.Count = 0 Then Return result
        ' 消えるうどんに隣接するおじゃまそばも消える
        Dim garbageCells As New List(Of Point)
        For Each p In result
            For Each d In Dirs
                Dim nx = p.X + d.X, ny = p.Y + d.Y
                If InBounds(nx, ny) AndAlso Not marked(nx, ny) AndAlso _cells(nx, ny) = Garbage Then
                    marked(nx, ny) = True
                    garbageCells.Add(New Point(nx, ny))
                End If
            Next
        Next
        result.AddRange(garbageCells)
        Return result
    End Function

    Public Shared ReadOnly Dirs As Point() = {New Point(1, 0), New Point(-1, 0), New Point(0, 1), New Point(0, -1)}

    ''' <summary>左上から右へ、上から下へ並べた72文字の文字列（プロトコルの board）。</summary>
    Public Function ToBoardString() As String
        Dim sb As New StringBuilder(Cols * Rows)
        For y = 0 To Rows - 1
            For x = 0 To Cols - 1
                sb.Append(ChrW(AscW("0"c) + _cells(x, y)))
            Next
        Next
        Return sb.ToString()
    End Function

    ''' <summary>72文字の文字列から盤面を復元する。形式が誤っていれば False。</summary>
    Public Function LoadBoardString(s As String) As Boolean
        If s Is Nothing OrElse s.Length <> Cols * Rows Then Return False
        For i = 0 To s.Length - 1
            Dim v = AscW(s(i)) - AscW("0"c)
            If v < 0 OrElse v > Garbage Then Return False
        Next
        For i = 0 To s.Length - 1
            _cells(i Mod Cols, i \ Cols) = CByte(AscW(s(i)) - AscW("0"c))
        Next
        Return True
    End Function

    Public Function CopyCells() As Byte(,)
        Return CType(_cells.Clone(), Byte(,))
    End Function
End Class

''' <summary>重力で動いたセル（fromY → toY）。</summary>
Public Structure CellMove
    Public X As Integer
    Public FromY As Integer
    Public ToY As Integer
    Public Kind As Byte
    Public Sub New(x As Integer, fromY As Integer, toY As Integer, kind As Byte)
        Me.X = x : Me.FromY = fromY : Me.ToY = toY : Me.Kind = kind
    End Sub
End Structure
