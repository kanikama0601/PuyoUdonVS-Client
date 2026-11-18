Imports System.Text.Json

''' <summary>
''' 企画書 6.4 のメッセージ（UTF-8 の JSON、必ず "type" を持つ）の組み立てと読み取り。
''' </summary>
Public Module Protocol
    Public Const DefaultPort As Integer = 8080
    Public Const PingIntervalMs As Integer = 5000
    Public Const ReceiveTimeoutMs As Integer = 15000
    Public Const MaxNameLength As Integer = 10

    ' ---- クライアント → サーバー ----

    Public Function Join(name As String, room As String) As String
        Return Build(Sub(w)
                         w.WriteString("type", "JOIN")
                         w.WriteString("name", name)
                         w.WriteString("room", room)
                     End Sub)
    End Function

    Public Function PieceMsg(x As Integer, y As Integer, rot As Integer) As String
        Return Build(Sub(w)
                         w.WriteString("type", "PIECE")
                         w.WriteNumber("x", x)
                         w.WriteNumber("y", y)
                         w.WriteNumber("rot", rot)
                     End Sub)
    End Function

    Public Function State(board As String, chain As Integer) As String
        Return Build(Sub(w)
                         w.WriteString("type", "STATE")
                         w.WriteString("board", board)
                         w.WriteNumber("chain", chain)
                     End Sub)
    End Function

    ''' <summary>offset は自分に予告されていたおじゃまそばと相殺した個数（相手へ送る数から引かれる）。</summary>
    Public Function Clear(erased As Integer, chain As Integer, Optional offset As Integer = 0) As String
        Return Build(Sub(w)
                         w.WriteString("type", "CLEAR")
                         w.WriteNumber("erased", erased)
                         w.WriteNumber("chain", chain)
                         If offset > 0 Then w.WriteNumber("offset", offset)
                     End Sub)
    End Function

    Public Function Simple(type As String) As String
        Return Build(Sub(w) w.WriteString("type", type))
    End Function

    Public Function Ping(t As Long) As String
        Return Build(Sub(w)
                         w.WriteString("type", "PING")
                         w.WriteNumber("t", t)
                     End Sub)
    End Function

    Private Function Build(body As Action(Of Utf8JsonWriter)) As String
        Using ms As New IO.MemoryStream()
            Using w As New Utf8JsonWriter(ms, New JsonWriterOptions With {.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping})
                w.WriteStartObject()
                body(w)
                w.WriteEndObject()
            End Using
            Return System.Text.Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    ' ---- サーバー → クライアント ----

    ''' <summary>受信したメッセージ。形式が誤っていれば Nothing。</summary>
    Public Function Parse(text As String) As ServerMessage
        Try
            Using doc = JsonDocument.Parse(text)
                Dim root = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return Nothing
                Dim t As JsonElement
                If Not root.TryGetProperty("type", t) OrElse t.ValueKind <> JsonValueKind.String Then Return Nothing
                Dim m As New ServerMessage With {.Type = t.GetString()}
                For Each p In root.EnumerateObject()
                    Select Case p.Value.ValueKind
                        Case JsonValueKind.String
                            m.Strings(p.Name) = p.Value.GetString()
                        Case JsonValueKind.Number
                            Dim l As Long
                            If p.Value.TryGetInt64(l) Then
                                m.Numbers(p.Name) = l
                            Else
                                m.Numbers(p.Name) = CLng(Math.Truncate(p.Value.GetDouble()))
                            End If
                    End Select
                Next
                Return m
            End Using
        Catch ex As JsonException
            Return Nothing
        End Try
    End Function
End Module

Public Class ServerMessage
    Public Type As String
    Public ReadOnly Strings As New Dictionary(Of String, String)
    Public ReadOnly Numbers As New Dictionary(Of String, Long)

    Public Function Str(name As String, Optional def As String = "") As String
        Dim s As String = Nothing
        If Strings.TryGetValue(name, s) Then Return s
        Dim n As Long
        If Numbers.TryGetValue(name, n) Then Return n.ToString()
        Return def
    End Function

    Public Function Int(name As String, Optional def As Integer = 0) As Integer
        Dim n As Long
        If Numbers.TryGetValue(name, n) Then Return CInt(Math.Clamp(n, Integer.MinValue, Integer.MaxValue))
        Dim s As String = Nothing
        Dim i As Integer
        If Strings.TryGetValue(name, s) AndAlso Integer.TryParse(s, i) Then Return i
        Return def
    End Function

    Public Function Has(name As String) As Boolean
        Return Strings.ContainsKey(name) OrElse Numbers.ContainsKey(name)
    End Function
End Class
