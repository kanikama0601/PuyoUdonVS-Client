Imports System.Collections.Concurrent
Imports System.Net.WebSockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Channels

Public Enum NetEventKind
    Connected
    Message
    ''' <summary>通信エラーや切断（意図しないもの）。</summary>
    Failed
End Enum

Public Structure NetEvent
    Public Kind As NetEventKind
    Public Text As String
End Structure

''' <summary>
''' WebSocket クライアント。受信は別スレッドで行い、<see cref="Inbox"/> に積む。
''' UI スレッド（ゲームループ）から <see cref="Poll"/> で取り出して処理する。
''' 送信はキューに積んで1本の送信タスクで順番に送る。
''' </summary>
Public NotInheritable Class NetClient
    Implements IDisposable

    Public Const ConnectTimeoutMs As Integer = 5000

    Private ReadOnly _ws As New ClientWebSocket()
    Private ReadOnly _cts As New CancellationTokenSource()
    Private ReadOnly _inbox As New ConcurrentQueue(Of NetEvent)
    Private ReadOnly _outbox As Channel(Of String) = Channel.CreateUnbounded(Of String)(New UnboundedChannelOptions With {.SingleReader = True})
    Private _closing As Integer
    Private _lastReceiveTicks As Long = Environment.TickCount64

    Public ReadOnly Property Uri As Uri

    Public Sub New(host As String, port As Integer)
        Dim h = host.Trim()
        If h.Contains(":"c) AndAlso Not h.StartsWith("["c) Then h = "[" & h & "]" ' IPv6
        Uri = New Uri($"ws://{h}:{port}/udon")
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10)
    End Sub

    ''' <summary>最後に何かを受信してからの経過ミリ秒。</summary>
    Public ReadOnly Property MillisSinceReceive As Long
        Get
            Return Environment.TickCount64 - Interlocked.Read(_lastReceiveTicks)
        End Get
    End Property

    Public ReadOnly Property IsOpen As Boolean
        Get
            Return _ws.State = WebSocketState.Open AndAlso Volatile.Read(_closing) = 0
        End Get
    End Property

    Public Sub Start()
        Task.Run(AddressOf RunAsync)
    End Sub

    Private Async Function RunAsync() As Task
        Try
            Using timeout As New CancellationTokenSource(ConnectTimeoutMs)
                Using linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _cts.Token)
                    Try
                        Await _ws.ConnectAsync(Uri, linked.Token)
                    Catch ex As OperationCanceledException When timeout.IsCancellationRequested
                        Throw New TimeoutException("サーバーに接続できませんでした（タイムアウト）。")
                    End Try
                End Using
            End Using
            Interlocked.Exchange(_lastReceiveTicks, Environment.TickCount64)
            _inbox.Enqueue(New NetEvent With {.Kind = NetEventKind.Connected})
            Dim sendTask = Task.Run(AddressOf SendLoopAsync)
            Await ReceiveLoopAsync()
        Catch ex As Exception
            Fail(Describe(ex))
        End Try
    End Function

    Private Async Function ReceiveLoopAsync() As Task
        Dim buffer(16 * 1024 - 1) As Byte
        Dim ms As New IO.MemoryStream()
        While Not _cts.IsCancellationRequested
            Dim r = Await _ws.ReceiveAsync(New ArraySegment(Of Byte)(buffer), _cts.Token)
            If r.MessageType = WebSocketMessageType.Close Then
                Fail("サーバーとの接続が切れました。")
                Return
            End If
            ms.Write(buffer, 0, r.Count)
            If r.EndOfMessage Then
                Interlocked.Exchange(_lastReceiveTicks, Environment.TickCount64)
                If r.MessageType = WebSocketMessageType.Text Then
                    _inbox.Enqueue(New NetEvent With {.Kind = NetEventKind.Message, .Text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, CInt(ms.Length))})
                End If
                ms.SetLength(0)
            End If
        End While
    End Function

    Private Async Function SendLoopAsync() As Task
        Try
            While Await _outbox.Reader.WaitToReadAsync(_cts.Token)
                Dim text As String = Nothing
                While _outbox.Reader.TryRead(text)
                    Dim bytes = Encoding.UTF8.GetBytes(text)
                    Await _ws.SendAsync(New ArraySegment(Of Byte)(bytes), WebSocketMessageType.Text, True, _cts.Token)
                End While
            End While
        Catch ex As Exception
            Fail(Describe(ex))
        End Try
    End Function

    Private Sub Fail(message As String)
        If Volatile.Read(_closing) <> 0 Then Return
        If Interlocked.Exchange(_closing, 1) <> 0 Then Return
        _inbox.Enqueue(New NetEvent With {.Kind = NetEventKind.Failed, .Text = message})
        _cts.Cancel()
    End Sub

    Private Function Describe(ex As Exception) As String
        If TypeOf ex Is TimeoutException Then Return ex.Message
        If TypeOf ex Is OperationCanceledException Then Return "通信が中断されました。"
        If TypeOf ex Is WebSocketException Then
            If _ws.State = WebSocketState.Open OrElse _ws.State = WebSocketState.Aborted Then
                Return "サーバーとの接続が切れました。"
            End If
            Return $"サーバー（{Uri}）に接続できませんでした。IPアドレスとポート番号を確認してください。"
        End If
        Return "通信エラー: " & ex.Message
    End Function

    ''' <summary>1件のメッセージ（JSON 文字列）を送信キューに積む。</summary>
    Public Sub Send(json As String)
        If Volatile.Read(_closing) <> 0 Then Return
        _outbox.Writer.TryWrite(json)
    End Sub

    Public Function Poll(ByRef ev As NetEvent) As Boolean
        Return _inbox.TryDequeue(ev)
    End Function

    ''' <summary>
    ''' こちらから切断する（LEAVE 送信後など）。キューに残った送信を少し待ってから閉じる。
    ''' 以後 Failed イベントは発生しない。
    ''' </summary>
    Public Sub Close()
        If Interlocked.Exchange(_closing, 1) <> 0 Then Return
        _outbox.Writer.TryComplete()
        Task.Run(Async Function()
                     Try
                         Dim deadline = Environment.TickCount64 + 1000
                         While _outbox.Reader.Count > 0 AndAlso Environment.TickCount64 < deadline
                             Await Task.Delay(20)
                         End While
                         If _ws.State = WebSocketState.Open Then
                             Using t As New CancellationTokenSource(1000)
                                 Await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", t.Token)
                             End Using
                         End If
                     Catch
                     Finally
                         _cts.Cancel()
                         _ws.Dispose()
                     End Try
                 End Function)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Close()
    End Sub
End Class
