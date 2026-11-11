Imports Microsoft.VisualBasic.ApplicationServices

Namespace My
    Partial Friend Class MyApplication

        Private Sub MyApplication_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
            AddHandler AppDomain.CurrentDomain.UnhandledException,
                Sub(s, ev) WriteCrashLog(TryCast(ev.ExceptionObject, Exception))
        End Sub

        ''' <summary>予期しない例外はログに残し、メッセージを出して終了する。</summary>
        Private Sub MyApplication_UnhandledException(sender As Object, e As UnhandledExceptionEventArgs) Handles Me.UnhandledException
            Dim path = WriteCrashLog(e.Exception)
            MessageBox.Show("予期しないエラーが発生したため終了します。" & vbCrLf & e.Exception.Message & vbCrLf & vbCrLf & "ログ: " & path,
                            "ぷようどんVS", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Sub

        Private Shared Function WriteCrashLog(ex As Exception) As String
            Dim path = IO.Path.Combine(IO.Path.GetTempPath(), "PuyoUdonVS-crash.log")
            Try
                IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}")
            Catch
            End Try
            Return path
        End Function

    End Class
End Namespace
