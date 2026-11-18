Imports System.Text.Json

Public Enum GameAction
    MoveLeft
    MoveRight
    SoftDrop
    RotateCW
    RotateCCW
    HardDrop
End Enum

''' <summary>
''' 操作とキーの割り当て。1つの操作に最大 SlotCount 個のキーを割り当てられる（空欄は Keys.None）。
''' 同じキーを2つの操作に割り当てることはできない（あとから割り当てたほうが優先され、前の割り当ては外れる）。
''' </summary>
Public NotInheritable Class KeyConfig
    Public Const SlotCount As Integer = 3
    Public Shared ReadOnly ActionCount As Integer = [Enum].GetValues(GetType(GameAction)).Length

    ''' <summary>JSON で使うキー名。</summary>
    Private Shared ReadOnly ActionIds As String() = {"moveLeft", "moveRight", "softDrop", "rotateCW", "rotateCCW", "hardDrop"}
    ''' <summary>画面に出す名前。</summary>
    Public Shared ReadOnly ActionNames As String() = {"左に移動", "右に移動", "高速落下", "時計回り", "反時計回り", "一気に落とす"}

    Private ReadOnly _slots As Keys(,) = New Keys(ActionCount - 1, SlotCount - 1) {}

    Public Sub New()
        ResetToDefault()
    End Sub

    Public Function GetKey(a As GameAction, slot As Integer) As Keys
        Return _slots(a, slot)
    End Function

    ''' <summary>その操作に割り当てられているキー（空欄を除く）。</summary>
    Public Function KeysOf(a As GameAction) As Keys()
        Dim list As New List(Of Keys)
        For i = 0 To SlotCount - 1
            If _slots(a, i) <> Keys.None Then list.Add(_slots(a, i))
        Next
        Return list.ToArray()
    End Function

    ''' <summary>割り当てに使えないキー（メニュー操作・キャンセルに使うもの）。</summary>
    Public Shared Function IsReserved(k As Keys) As Boolean
        Return k = Keys.None OrElse k = Keys.Escape OrElse k = Keys.Enter OrElse k = Keys.F3
    End Function

    ''' <summary>キーを割り当てる。使えないキーなら False。他の枠に同じキーがあれば外す。</summary>
    Public Function SetKey(a As GameAction, slot As Integer, k As Keys) As Boolean
        If IsReserved(k) Then Return False
        RemoveKey(k)
        _slots(a, slot) = k
        Return True
    End Function

    Public Sub ClearKey(a As GameAction, slot As Integer)
        _slots(a, slot) = Keys.None
    End Sub

    Private Sub RemoveKey(k As Keys)
        For a = 0 To ActionCount - 1
            For i = 0 To SlotCount - 1
                If _slots(a, i) = k Then _slots(a, i) = Keys.None
            Next
        Next
    End Sub

    Private Sub Fill(a As GameAction, ParamArray ks As Keys())
        For i = 0 To SlotCount - 1
            _slots(a, i) = If(i < ks.Length, ks(i), Keys.None)
        Next
    End Sub

    ''' <summary>初期設定。反時計回りは空欄。</summary>
    Public Sub ResetToDefault()
        Fill(GameAction.MoveLeft, Keys.Left, Keys.A)
        Fill(GameAction.MoveRight, Keys.Right, Keys.D)
        Fill(GameAction.SoftDrop, Keys.Down, Keys.S)
        Fill(GameAction.RotateCW, Keys.Up)
        Fill(GameAction.RotateCCW)
        Fill(GameAction.HardDrop, Keys.Space)
    End Sub

    ' ---- 保存・読み込み ----

    Public Function ToDictionary() As Dictionary(Of String, String())
        Dim d As New Dictionary(Of String, String())
        For a = 0 To ActionCount - 1
            d(ActionIds(a)) = KeysOf(CType(a, GameAction)).Select(Function(k) k.ToString()).ToArray()
        Next
        Return d
    End Function

    ''' <summary>辞書から読み込む。含まれていない操作は今のまま。形式が正しくなければ例外を投げ、何も変更しない。</summary>
    Public Sub LoadFrom(d As Dictionary(Of String, String()))
        If d Is Nothing Then Return
        Dim src As New Dictionary(Of String, String())(d, StringComparer.OrdinalIgnoreCase)
        For Each key In src.Keys
            If Array.FindIndex(ActionIds, Function(id) String.Equals(id, key, StringComparison.OrdinalIgnoreCase)) < 0 Then
                Throw New FormatException($"知らない操作名があります: {key}")
            End If
        Next
        Dim tmp As New KeyConfig()
        For a = 0 To ActionCount - 1
            tmp.Fill(CType(a, GameAction))
        Next
        Dim touched As New HashSet(Of Integer)
        For a = 0 To ActionCount - 1
            Dim names As String() = Nothing
            If Not src.TryGetValue(ActionIds(a), names) Then Continue For
            touched.Add(a)
            If names Is Nothing Then names = Array.Empty(Of String)()
            Dim slot = 0
            For Each n In names
                If String.IsNullOrWhiteSpace(n) Then Continue For
                Dim k As Keys
                If Not [Enum].TryParse(n.Trim(), True, k) OrElse k = Keys.None OrElse (k And Keys.Modifiers) <> 0 Then
                    Throw New FormatException($"知らないキー名があります: {n}")
                End If
                If IsReserved(k) Then Throw New FormatException($"使えないキーです: {n}")
                If slot >= SlotCount Then Throw New FormatException($"1つの操作に割り当てられるキーは{SlotCount}個までです: {ActionIds(a)}")
                tmp._slots(a, slot) = k
                slot += 1
            Next
        Next
        ' 取り込まない操作は今の割り当てを引き継ぐ（ただし取り込んだキーと重なるものは外す）
        For a = 0 To ActionCount - 1
            If touched.Contains(a) Then Continue For
            For i = 0 To SlotCount - 1
                tmp._slots(a, i) = _slots(a, i)
            Next
        Next
        ' 同じキーが複数の操作にあれば、取り込んだ側を優先
        Dim used As New HashSet(Of Keys)
        For Each a In touched
            For i = 0 To SlotCount - 1
                If tmp._slots(a, i) <> Keys.None AndAlso Not used.Add(tmp._slots(a, i)) Then
                    Throw New FormatException($"同じキーが複数の枠に割り当てられています: {tmp._slots(a, i)}")
                End If
            Next
        Next
        For a = 0 To ActionCount - 1
            If touched.Contains(a) Then Continue For
            For i = 0 To SlotCount - 1
                If used.Contains(tmp._slots(a, i)) Then tmp._slots(a, i) = Keys.None
            Next
        Next
        For a = 0 To ActionCount - 1
            For i = 0 To SlotCount - 1
                _slots(a, i) = tmp._slots(a, i)
            Next
        Next
    End Sub

    Public Function ToJson() As String
        Return JsonSerializer.Serialize(ToDictionary(), New JsonSerializerOptions With {.WriteIndented = True})
    End Function

    Public Sub LoadFromJson(text As String)
        Dim d As Dictionary(Of String, String())
        Try
            d = JsonSerializer.Deserialize(Of Dictionary(Of String, String()))(text)
        Catch ex As JsonException
            Throw New FormatException("JSON の形式が正しくありません。")
        End Try
        If d Is Nothing Then Throw New FormatException("JSON の形式が正しくありません。")
        LoadFrom(d)
    End Sub

    ' ---- 表示用 ----

    Public Shared Function DisplayName(k As Keys) As String
        Select Case k
            Case Keys.None : Return "―"
            Case Keys.Left : Return "←"
            Case Keys.Right : Return "→"
            Case Keys.Up : Return "↑"
            Case Keys.Down : Return "↓"
            Case Keys.Space : Return "Space"
            Case Keys.D0 To Keys.D9 : Return CStr(CInt(k) - CInt(Keys.D0))
            Case Keys.NumPad0 To Keys.NumPad9 : Return "Num" & CStr(CInt(k) - CInt(Keys.NumPad0))
            Case Keys.ControlKey : Return "Ctrl"
            Case Keys.ShiftKey : Return "Shift"
            Case Keys.Menu : Return "Alt"
        End Select
        Return k.ToString()
    End Function
End Class
