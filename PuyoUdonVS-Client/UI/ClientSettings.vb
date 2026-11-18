Imports System.IO
Imports System.Text.Json

''' <summary>前回の入力内容と音量を %APPDATA%\PuyoUdonVS\client.json に保存する。</summary>
Public Class ClientSettings
    Public Property Host As String = "127.0.0.1"
    Public Property Port As Integer = Protocol.DefaultPort
    Public Property Name As String = ""
    Public Property Room As String = ""
    Public Property BgmVolume As Single = 0.6F
    Public Property SeVolume As Single = 0.8F

    Private Shared ReadOnly FilePath As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PuyoUdonVS", "client.json")

    Public Shared Function Load() As ClientSettings
        Try
            If File.Exists(FilePath) Then
                Dim s = JsonSerializer.Deserialize(Of ClientSettings)(File.ReadAllText(FilePath))
                If s IsNot Nothing Then Return s
            End If
        Catch
        End Try
        Return New ClientSettings()
    End Function

    Public Sub Save()
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True}))
        Catch
        End Try
    End Sub
End Class
